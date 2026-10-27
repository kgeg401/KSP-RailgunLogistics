using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace RailgunLogistics
{
    public class Cargo
    {
        public string Resource = "LiquidFuel";
        public double Units;
    }
    public class Order
    {
        public string Id, Owner, VesselId, VesselName, Body;
        public uint Anchor, Port;
        public string PodId;
        public bool PodSpawned;
        public long Created, Launch, Arrival;
        public string Status = "InFlight";
        public List<Cargo> Cargo = new List<Cargo>();
        public List<Cargo> Delivered = new List<Cargo>();
    }
    public class DepotState
    {
        public int Version = 1;
        public List<Order> Orders = new List<Order>();
    }
    public class Options
    {
        public int ChargeSeconds = 30;
        public int KerbinTransitSeconds = 120;
        public int OtherTransitSeconds = 600;
        public double MaxUnitsPerResource = 1000000;
        public string[] AllowedResources = { "LiquidFuel", "Oxidizer", "MonoPropellant", "XenonGas", "LqdHydrogen", "LqdMethane", "ArgonGas", "Lithium", "ElectricCharge" };
        public void Validate()
        {
            if (ChargeSeconds<1 || ChargeSeconds>86400 || KerbinTransitSeconds<1 || OtherTransitSeconds<1 ||
                KerbinTransitSeconds>604800 || OtherTransitSeconds>604800 || !Rules.Positive(MaxUnitsPerResource) ||
                AllowedResources==null || AllowedResources.Length==0) throw new InvalidDataException("Invalid logistics settings");
        }
    }
    public static class Rules
    {
        public const string Channel = "Kgeg4.RailgunLogistics.v2";
        public static string PodIdentity(string orderId)
        {
            var bytes=Guid.ParseExact(orderId,"N").ToByteArray();
            bytes[0]^=0xA7;bytes[7]^=0x4C;bytes[15]^=0xD2;
            return new Guid(bytes).ToString();
        }
        public static bool Positive(double x) => !double.IsNaN(x) && !double.IsInfinity(x) && x>0;
        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public static List<Cargo> Copy(IEnumerable<Cargo> items) => items.Select(c=>new Cargo { Resource=c.Resource, Units=c.Units }).ToList();
        public static List<double> Allocate(double requested, IList<double> amounts, IList<double> capacities)
        {
            if (!Positive(requested) || amounts.Count!=capacities.Count) throw new ArgumentException("Invalid allocation");
            var result=new List<double>();
            for(int i=0;i<amounts.Count;i++)
            {
                if (double.IsNaN(amounts[i]) || double.IsInfinity(amounts[i]) || amounts[i]<0 ||
                    double.IsNaN(capacities[i]) || double.IsInfinity(capacities[i]) || capacities[i]<amounts[i])
                    throw new InvalidDataException("Invalid tank state");
                double add=Math.Min(requested,capacities[i]-amounts[i]);
                result.Add(add);requested-=add;
            }
            return result;
        }
        public static bool HasReceipt(string receipts,string id) => (receipts??"").Split(';').Contains(id);
        public static string AddReceipt(string receipts,string id) => HasReceipt(receipts,id)?receipts:(receipts??"")+id+";";
    }
    public static class XmlFile
    {
        public static T Read<T>(string path) where T:new()
        {
            if (!File.Exists(path)) return new T();
            using(var stream=File.OpenRead(path)) return (T)new XmlSerializer(typeof(T)).Deserialize(stream);
        }
        public static void Save<T>(string path,T obj)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+".tmp";
            using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
            {
                new XmlSerializer(typeof(T)).Serialize(stream,obj);stream.Flush(true);
            }
            if(File.Exists(path)) File.Replace(temp,path,path+".previous",true);
            else File.Move(temp,path);
        }
    }
    // All mutations are serialized by the caller. A failed durable write stops this instance.
    public sealed class Depot
    {
        public DepotState State {get; private set;}
        public readonly Options Settings;
        readonly Action<DepotState> save;
        bool faulted;
        public Depot(DepotState state,Options settings,Action<DepotState> persist)
        {
            State=state;Settings=settings;save=persist;settings.Validate();
            if(state.Version!=1 || state.Orders==null || state.Orders.Select(x=>x.Id).Distinct().Count()!=state.Orders.Count)
                throw new InvalidDataException("Invalid logistics ledger; restore its backup, do not reset it");
        }
        void Check(){if(faulted)throw new IOException("Logistics ledger is unavailable; restart after repairing storage");}
        void Persist(){try{save(State);}catch{faulted=true;throw;}}
        public Order Get(string id)=>State.Orders.FirstOrDefault(x=>x.Id==id);
        public Order Submit(Order order,long now)
        {
            Check();
            if(!Guid.TryParseExact(order.Id,"N",out _) || !Guid.TryParse(order.VesselId,out _) || order.Anchor==0 ||
                order.Port==0 || string.IsNullOrWhiteSpace(order.Owner) || order.Owner.Length>100 || order.VesselName==null || order.VesselName.Length>160 ||
                order.Cargo==null || order.Cargo.Count<1 || order.Cargo.Count>12 || order.Cargo.Select(c=>c.Resource).Distinct().Count()!=order.Cargo.Count)
                throw new InvalidDataException("Invalid dispatch request");
            foreach(var c in order.Cargo)
                if(!Settings.AllowedResources.Contains(c.Resource) || !Rules.Positive(c.Units) || c.Units>Settings.MaxUnitsPerResource)
                    throw new InvalidDataException("Unsupported cargo or amount");
            var existing=Get(order.Id);
            if(existing!=null)
            {
                if(existing.Owner!=order.Owner || existing.Anchor!=order.Anchor || existing.Port!=order.Port || existing.Cargo.Count!=order.Cargo.Count ||
                    existing.Cargo.Where((c,i)=>c.Resource!=order.Cargo[i].Resource || c.Units!=order.Cargo[i].Units).Any())
                    throw new InvalidDataException("Dispatch ID conflict");
                return existing;
            }
            if(State.Orders.Count>=10000 || State.Orders.Count(o=>o.Owner==order.Owner && o.Status!="Delivered" && o.Status!="Cancelled")>=8)
                throw new InvalidDataException("Dispatch queue is full");
            if(State.Orders.Any(o=>o.Anchor==order.Anchor && o.Status!="Delivered" && o.Status!="Cancelled"))
                throw new InvalidDataException("This receiver already has a delivery pending");
            order.Created=now;order.Launch=checked(now+Settings.ChargeSeconds);
            order.Arrival=checked(order.Launch+(order.Body=="Kerbin"?Settings.KerbinTransitSeconds:Settings.OtherTransitSeconds));
            order.Status="InFlight";order.Delivered=new List<Cargo>();
            order.PodId=Rules.PodIdentity(order.Id);order.PodSpawned=false;
            State.Orders.Add(order);Persist();return order;
        }
        public Order Claim(string id,string owner,string vesselId,uint anchor,long now)
        {
            Check();var o=Get(id);
            if(o==null || o.Owner!=owner || o.Anchor!=anchor)throw new InvalidDataException("Not your dispatch");
            if(now<o.Arrival || o.Status=="Delivered" || o.Status=="Cancelled")return null;
            if(!Guid.TryParse(vesselId,out _))throw new InvalidDataException("Invalid receiving ship");
            if(o.VesselId!=vesselId || o.Status!="Applying")
            {o.VesselId=vesselId;o.Status="Applying";Persist();}
            return o;
        }
        public void ConfirmPod(string id,bool durablePod)
        {
            Check();var o=Get(id);
            if(o==null || o.Status!="Applying" || !durablePod || o.PodSpawned)return;
            o.PodSpawned=true;Persist();
        }
        public void Confirm(string id,bool durableVesselReceipt,IEnumerable<Cargo> actual)
        {
            Check();var o=Get(id);
            if(o==null || o.Status!="Applying" || !durableVesselReceipt || !o.PodSpawned)return;
            var result=Rules.Copy(actual);
            if(result.Count!=o.Cargo.Count || result.Where((c,i)=>c.Resource!=o.Cargo[i].Resource || c.Units<0 ||
                double.IsNaN(c.Units) || double.IsInfinity(c.Units) || Math.Abs(c.Units-o.Cargo[i].Units)>1e-6).Any())
                throw new InvalidDataException("Invalid delivery receipt");
            o.Delivered=result;o.Status="Delivered";Persist();
        }
        public void Cancel(string id,string owner)
        {
            Check();var o=Get(id);
            if(o==null || o.Owner!=owner)throw new InvalidDataException("Not your dispatch");
            if(o.Status!="InFlight")throw new InvalidDataException("Transfer already started");
            o.Status="Cancelled";Persist();
        }
    }
    public class Packet
    {
        public string Kind, Error;
        public long ServerNow;
        public Options Settings;
        public List<Order> Orders=new List<Order>();
        public Order Order;
    }
    public static class Wire
    {
        public static byte[] Encode(Packet packet)
        {
            using(var s=new MemoryStream()) {new XmlSerializer(typeof(Packet)).Serialize(s,packet);return s.ToArray();}
        }
        public static Packet Decode(byte[] data,int count)
        {
            if(count<1 || count>262144 || count>data.Length)throw new InvalidDataException("Invalid packet size");
            using(var s=new MemoryStream(data,0,count))
            using(var r=System.Xml.XmlReader.Create(s,new System.Xml.XmlReaderSettings { DtdProcessing=System.Xml.DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=262144 }))
                return (Packet)new XmlSerializer(typeof(Packet)).Deserialize(r);
        }
    }
}
