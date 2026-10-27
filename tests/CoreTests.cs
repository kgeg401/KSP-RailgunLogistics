using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using RailgunLogistics;

static class Tests
{
 static int count;
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 static void Throws(Action test,string name){bool failed=false;try{test();}catch{failed=true;}Check(failed,name);}
 static Order New(string player="Alice",uint anchor=25)=>new Order {Id=Guid.NewGuid().ToString("N"),Owner=player,
     VesselId=Guid.NewGuid().ToString(),VesselName="Science Core",Body="Kerbin",Anchor=anchor,Port=50,
     Cargo=new List<Cargo>{new Cargo{Resource="MonoPropellant",Units=500}}};
 static void Main()
 {
   string dir=Path.Combine(Path.GetTempPath(),"RailgunTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
   string path=Path.Combine(dir,"Orders.xml");
   var options=new Options();var depot=new Depot(new DepotState(),options,s=>XmlFile.Save(path,s));
   var request=New();var order=depot.Submit(request,1000);
   Check(order.Launch==1030 && order.Arrival==1150,"server sets charge and transit countdown");
   Check(order.PodId==Rules.PodIdentity(order.Id) && !order.PodSpawned,"server assigns deterministic cargo vessel identity");
   Check(depot.Claim(order.Id,"Alice",order.VesselId,25,1149)==null,"cannot deliver early");
   Throws(()=>depot.Claim(order.Id,"Bob",order.VesselId,25,1200),"second player cannot claim another player's cargo");
   Throws(()=>depot.Submit(New("Alice",25),1001),"one pending order per receiver");
   Check(depot.Submit(request,1002).Id==order.Id && depot.State.Orders.Count==1,"duplicate order packet does not duplicate dispatch");
   var conflict=New();conflict.Id=order.Id;conflict.Cargo[0].Units=900;
   Throws(()=>depot.Submit(conflict,1002),"reused ID with changed cargo rejected");
   string docked=Guid.NewGuid().ToString();
   Check(depot.Claim(order.Id,"Alice",docked,25,1150).VesselId==docked,"receiver can follow its command part after docking");
   Throws(()=>depot.Cancel(order.Id,"Alice"),"cannot cancel after transfer starts");
   var reloaded=new Depot(XmlFile.Read<DepotState>(path),options,s=>XmlFile.Save(path,s));
   Check(reloaded.Get(order.Id).Status=="Applying","grant is durable before fuel application");
   reloaded.Confirm(order.Id,false,order.Cargo);
   Check(reloaded.Get(order.Id).Status=="Applying","no completion without durable vessel receipt");
   reloaded.Confirm(order.Id,true,order.Cargo);
   Check(reloaded.Get(order.Id).Status=="Applying","cargo cannot complete before a durable physical pod exists");
   reloaded.ConfirmPod(order.Id,false);
   Check(!reloaded.Get(order.Id).PodSpawned,"unpersisted pod does not authorize unloading");
   reloaded.ConfirmPod(order.Id,true);
   reloaded.ConfirmPod(order.Id,true);
   Check(reloaded.Get(order.Id).PodSpawned,"pod persistence acknowledgement is idempotent");
   Throws(()=>reloaded.Confirm(order.Id,true,new[]{new Cargo{Resource="MonoPropellant",Units=0}}),"partial or empty unloading cannot complete a delivery");
   string receipts=Rules.AddReceipt("",order.Id);receipts=Rules.AddReceipt(receipts,order.Id);
   Check(receipts.Split(';').Count(x=>x==order.Id)==1,"duplicate grant has one receipt");
   reloaded.Confirm(order.Id,true,order.Cargo);
   reloaded=new Depot(XmlFile.Read<DepotState>(path),options,s=>XmlFile.Save(path,s));
   Check(reloaded.Claim(order.Id,"Alice",docked,25,9999)==null,"delivered orders cannot replay after restart");
   var allocation=Rules.Allocate(800,new double[]{50,90},new double[]{100,100});
   Check(allocation.SequenceEqual(new double[]{50,10}),"tank allocation never overfills");
   Check(Rules.Allocate(12,new double[]{100},new double[]{100}).Sum()==0,"full tanks accept zero");
   Throws(()=>Rules.Allocate(2,new double[]{double.NaN},new double[]{100}),"invalid tank numbers rejected before changes");
   foreach(double qty in new[]{double.NaN,double.PositiveInfinity,-1,0,1000001})
   {var bad=New("Alice",26);bad.Cargo[0].Units=qty;Throws(()=>reloaded.Submit(bad,1000),"invalid quantity rejected: "+qty);}
   var unsupported=New("Alice",27);unsupported.Cargo[0].Resource="EnrichedUranium";
   Throws(()=>reloaded.Submit(unsupported,1000),"unapproved resource rejected");
   var remote=New("Bob",99);remote.Body="Duna";Check(reloaded.Submit(remote,1000).Arrival==1630,"other-body transit uses configured ETA");
   reloaded.Cancel(remote.Id,"Bob");Check(reloaded.Get(remote.Id).Status=="Cancelled","pending order can be cancelled");
   var packet=new Packet{Kind="State",Orders=reloaded.State.Orders,Settings=options,ServerNow=1000};var bytes=Wire.Encode(packet);
   var copy=Wire.Decode(bytes,bytes.Length);
   Check(copy.Orders.Count==2 && copy.Orders[0].Cargo[0].Units==500,"client/server wire round-trip preserves ledger");
   Throws(()=>Wire.Decode(bytes,262145),"oversized message rejected");
   var entity=System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY ext SYSTEM 'file:///C:/Windows/win.ini'>]><Packet><Kind>&ext;</Kind></Packet>");
   Throws(()=>Wire.Decode(entity,entity.Length),"XML external entities blocked");
   var broken=new Depot(new DepotState(),options,s=>throw new IOException("disk full"));
   Throws(()=>broken.Submit(New(),1000),"storage failure prevents grant");
   Throws(()=>broken.Claim(broken.State.Orders[0].Id,"Alice",Guid.NewGuid().ToString(),25,1200),"failed storage instance stops all future mutations");
   Check(File.Exists(path+".previous"),"atomic ledger updates retain previous revision");
   Console.WriteLine("RESULT: "+count+" checks passed; temp evidence "+dir);
 }
}
