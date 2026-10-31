using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RailgunLogisticsServer;

int count=0;
void Check(bool value,string message){if(!value)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
List<RefitSupplyPolicy.Tank> Tanks()=>new List<RefitSupplyPolicy.Tank>{
    new RefitSupplyPolicy.Tank{Part=1,PartName="battery",Resource="ElectricCharge",Amount=100,Capacity=100,Flow=true},
    new RefitSupplyPolicy.Tank{Part=2,PartName="fuel",Resource="LiquidFuel",Amount=5760,Capacity=5760,Flow=true},
    new RefitSupplyPolicy.Tank{Part=2,PartName="fuel",Resource="Oxidizer",Amount=7040,Capacity=7040,Flow=true},
    new RefitSupplyPolicy.Tank{Part=2,PartName="fuel",Resource="MonoPropellant",Amount=120,Capacity=120,Flow=true},
    new RefitSupplyPolicy.Tank{Part=3,PartName="krlReusableMonoCargo",Resource="MonoPropellant",Amount=0,Capacity=120,Flow=false}};
var ids=new uint[]{1,2,3};
Check(RefitSupplyPolicy.Ready(Tanks(),ids,3),"Full propulsion supplies and empty locked cargo can become Ready");
foreach(var test in new (string name,Action<List<RefitSupplyPolicy.Tank>> change)[]{
    ("Earlier pre-refuel liquid fuel cannot acknowledge Ready",t=>t[1].Amount=170),
    ("Empty maneuvering supply cannot acknowledge Ready",t=>t[3].Amount=0),
    ("Unemptied cargo cannot be refit credit",t=>t[4].Amount=100),
    ("Cargo must remain locked after refill",t=>t[4].Flow=true),
    ("NaN native fuel fails closed",t=>t[1].Amount=double.NaN),
    ("Duplicate native resource fails closed",t=>t.Add(t[1])),
    ("Usable electrical charge is required",t=>t[0].Amount=0),
    ("A foreign part cannot satisfy propulsion supply",t=>t[1].Part=99)}){
    var values=Tanks();test.change(values);Check(!RefitSupplyPolicy.Ready(values,ids,3),test.name);
}
var generating=Tanks();generating[0].Amount=99.9;
Check(RefitSupplyPolicy.Ready(generating,ids,3),"Live battery generation does not require exact frame-level fullness");
if(args.Length==4){
    var fleet=XDocument.Load(args[1]);var pod=fleet.Descendants("Pod").First();
    var originals=pod.Element("PartIds").Elements().Select(e=>uint.Parse(e.Value)).ToArray();
    uint cargo=uint.Parse(pod.Element("CargoPart").Value);string identity=pod.Element("VesselId").Value;
    string full=File.ReadAllText(args[0]),early=File.ReadAllText(args[2]),order=args[3];
    Check(RefitSupplyEvidence.SavedReady(full,originals,cargo,identity,order),"Actual late native refuel snapshot passes installed Luna parsing");
    Check(!RefitSupplyEvidence.SavedReady(early,originals,cargo,identity,order),"Actual earlier Ready-labelled but unrefuelled native snapshot rejects");
    Check(!RefitSupplyEvidence.SavedReady(full,originals,cargo,identity,"another-order"),"A different native delivery receipt cannot acknowledge refit");
    Check(!RefitSupplyEvidence.SavedReady(full,originals,cargo,"another-pod",order),"A different pod marker cannot acknowledge refit");
    Check(!RefitSupplyEvidence.SavedReady(full.Substring(0,full.Length/2),originals,cargo,identity,order),"A truncated native snapshot is not durable readiness");
}
Console.WriteLine("PASS "+count+" refit supply checks; native network acknowledgement remains a separate test.");
