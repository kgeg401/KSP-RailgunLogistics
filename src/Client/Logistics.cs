using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using UnityEngine;
using KSP.UI.Screens;
using LmpClient;
using LmpClient.Events;
using LmpClient.Systems.ModApi;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.VesselProtoSys;
using LmpCommon.Enums;
using LmpCommon.Message.Data;
using LmpClient.Network;

namespace RailgunLogistics
{
    // Receipt travels in the same vessel snapshot as its resource amounts, including after docking.
    public sealed class RailgunDeliveryReceipt : PartModule
    {
        [KSPField(isPersistant=true)] public string receipts="";
        [KSPField(isPersistant=true)] public string lastId="";
        [KSPField(isPersistant=true)] public string lastCargo="";
        [KSPEvent(guiActive=true,guiName="Request railgun supplies",active=true)]
        public void OpenLogistics(){Logistics.Instance?.Open();}
    }

    [KSPAddon(KSPAddon.Startup.MainMenu,true)]
    public sealed class Logistics : MonoBehaviour
    {
        public static Logistics Instance;
        readonly ConcurrentQueue<Packet> incoming=new ConcurrentQueue<Packet>();
        readonly Dictionary<string,string> amounts=new Dictionary<string,string>();
        readonly HashSet<string> announced=new HashSet<string>();
        readonly HashSet<string> spawning=new HashSet<string>();
        List<Order> orders=new List<Order>();
        Options options=new Options();
        Depot offline;
        string offlineFolder,warning="",notice="Select cargo and a free Clamp-O-Tron port. Fuel stays aboard the pod until it docks.";
        uint selectedPort;
        bool visible,onlineLast,subscribed,serviceReady;
        float nextTick,serverStamp;
        long serverTime;
        Rect window=new Rect(60,100,490,560);
        Vector2 scroll;
        ApplicationLauncherButton button;
        Texture2D icon;
        string pendingId;
        Order pendingRequest;
        const string InputLock="RailgunLogistics.Window";
        public static bool Connected=>MainSystem.NetworkState==ClientState.Running;
        bool Multiplayer=>Connected || HighLogic.SaveFolder=="LunaMultiplayer";
        long Now=>Connected && serviceReady?serverTime+(long)(Time.realtimeSinceStartup-serverStamp):Rules.Now;
        string Player=>Connected?SettingsSystem.CurrentSettings.PlayerName:"Commander";
        public void Open(){visible=true;}
        void Awake()
        {
            if(Instance!=null){Destroy(this);return;}
            Instance=this;DontDestroyOnLoad(gameObject);
            Debug.Log("[RailgunLogistics] v0.2.0 loaded; physical docking pods ready.");
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            AddButton();
        }
        void AddButton()
        {
            if(button!=null || ApplicationLauncher.Instance==null)return;
            icon=new Texture2D(38,38,TextureFormat.RGBA32,false);
            var pixels=new Color[38*38];
            for(int y=0;y<38;y++)for(int x=0;x<38;x++)
                pixels[y*38+x]=(x>=10&&x<=13 || x>=25&&x<=28) && y>4 && y<33?new Color(.25f,.8f,1):new Color(.03f,.08f,.13f,1);
            for(int y=13;y<27;y++)for(int x=17;x<22;x++)pixels[y*38+x]=Color.white;
            icon.SetPixels(pixels);icon.Apply();
            button=ApplicationLauncher.Instance.AddModApplication(()=>visible=true,()=>visible=false,null,null,null,null,
                ApplicationLauncher.AppScenes.FLIGHT|ApplicationLauncher.AppScenes.SPACECENTER|ApplicationLauncher.AppScenes.TRACKSTATION,icon);
        }
        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            if(subscribed && ModApiEvent.onModMessageReceived!=null)ModApiEvent.onModMessageReceived.Remove(Receive);
            if(button!=null && ApplicationLauncher.Instance!=null)ApplicationLauncher.Instance.RemoveModApplication(button);
            if(icon!=null)Destroy(icon);
            InputLockManager.RemoveControlLock(InputLock);if(Instance==this)Instance=null;
        }
        void Receive(string name,byte[] bytes)
        {
            if(name!=Rules.Channel)return;
            try{incoming.Enqueue(Wire.Decode(bytes,bytes.Length));}catch(Exception ex){Debug.LogWarning("[RailgunLogistics] Ignored malformed reply: "+ex.Message);}
        }
        void Send(Packet packet)
        {
            byte[] bytes=Wire.Encode(packet);
            var data=NetworkMain.CliMsgFactory.CreateNewMessageData<ModMsgData>();
            data.ModName=Rules.Channel;data.Data=bytes;data.NumBytes=bytes.Length;data.Reliable=true;data.Relay=false;
            ModApiSystem.Singleton.MessageSender.SendMessage(data);
        }
        void Update()
        {
            if(!subscribed && ModApiEvent.onModMessageReceived!=null){ModApiEvent.onModMessageReceived.Add(Receive);subscribed=true;}
            if(Connected!=onlineLast)
            {
                onlineLast=Connected;serviceReady=false;orders.Clear();offline=null;offlineFolder=null;pendingRequest=null;pendingId=null;
                warning="";notice="Select cargo and a free Clamp-O-Tron port. Fuel stays aboard the pod until it docks.";
            }
            while(incoming.TryDequeue(out var reply))
            {
                if(!Connected)continue;
                serverTime=reply.ServerNow;serverStamp=Time.realtimeSinceStartup;
                if(reply.Kind=="State")
                {
                    serviceReady=true;orders=reply.Orders??new List<Order>();options=reply.Settings??options;warning="";
                    if(pendingId!=null && orders.Any(o=>o.Id==pendingId)){pendingId=null;pendingRequest=null;notice="Kerbin accepted your dispatch.";}
                }
                else if(reply.Kind=="Grant")Apply(reply.Order);
                else if(reply.Kind=="Error"){warning=reply.Error;pendingRequest=null;pendingId=null;}
            }
            if(Time.realtimeSinceStartup<nextTick)return;
            nextTick=Time.realtimeSinceStartup+2;
            try
            {
                if(Connected)
                {
                    Send(new Packet {Kind="List"});
                    if(pendingRequest!=null)Send(new Packet {Kind="Order",Order=pendingRequest});
                }
                else if(HighLogic.CurrentGame!=null && !Multiplayer && !string.IsNullOrEmpty(HighLogic.SaveFolder))
                {
                    if(offlineFolder!=HighLogic.SaveFolder)
                    {
                        string ledger=Path.Combine(KSPUtil.ApplicationRootPath,"saves",HighLogic.SaveFolder,"RailgunLogistics.xml");
                        options=XmlFile.Read<Options>(Path.Combine(KSPUtil.ApplicationRootPath,"GameData","RailgunLogistics","Settings.xml"));
                        offline=new Depot(XmlFile.Read<DepotState>(ledger),options,s=>XmlFile.Save(ledger,s));
                        offlineFolder=HighLogic.SaveFolder;
                    }
                    orders=offline.State.Orders;serviceReady=true;
                }
                else {orders.Clear();serviceReady=false;offline=null;offlineFolder=null;}
                foreach(var o in orders.Where(o=>o.Owner==Player && (o.Status=="InFlight" || o.Status=="Applying")).ToArray())
                {
                    if(Now>=o.Launch && announced.Add(o.Id))
                        ScreenMessages.PostScreenMessage("Kerbin railgun: cargo dispatched to "+o.VesselName,5,ScreenMessageStyle.UPPER_CENTER);
                    var v=ActiveReceiver(o.Anchor);
                    if(Now>=o.Arrival && v!=null)
                    {
                        var request=new Order {Id=o.Id,VesselId=v.id.ToString(),Anchor=o.Anchor};
                        if(Connected)Send(new Packet {Kind="Claim",Order=request});
                        else Apply(offline.Claim(o.Id,Player,v.id.ToString(),o.Anchor,Now));
                    }
                }
            }
            catch(Exception ex){warning=ex.Message;Debug.LogWarning("[RailgunLogistics] "+ex);}
        }
        static Vessel ActiveReceiver(uint anchor)
        {
            var v=FlightGlobals.ActiveVessel;
            if(!HighLogic.LoadedSceneIsFlight || v==null || !v.loaded || v.packed || v.state==Vessel.State.DEAD)return null;
            if(Connected && !LmpClient.Systems.Lock.LockSystem.LockQuery.ControlLockBelongsToPlayer(v.id,SettingsSystem.CurrentSettings.PlayerName))return null;
            return v.parts.Any(p=>p.flightID==anchor && p.FindModuleImplementing<RailgunDeliveryReceipt>()!=null)?v:null;
        }
        static RailgunDeliveryReceipt FindReceiver(Vessel v)=>v?.parts.Select(p=>p.FindModuleImplementing<RailgunDeliveryReceipt>()).FirstOrDefault(p=>p!=null);
        void Dispatch()
        {
            var v=FlightGlobals.ActiveVessel;var receiver=FindReceiver(v);
            if(v==null || receiver==null || ActiveReceiver(receiver.part.flightID)==null)throw new InvalidOperationException("Take control of a loaded ship with a command pod or probe core.");
            PhysicalPods.CheckOrbit(v);
            if(!PhysicalPods.Ports(v).Any(p=>p.part.flightID==selectedPort))throw new InvalidOperationException("Select a free standard Clamp-O-Tron port (size1). Junior and Sr. ports are not supported by this pod.");
            var cargo=new List<Cargo>();
            foreach(var pair in amounts)
            {
                if(string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Trim()=="0")continue;
                if(!double.TryParse(pair.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var qty) || !Rules.Positive(qty))
                    throw new InvalidOperationException("Enter a positive amount for "+pair.Key);
                double room=Tanks(v,pair.Key).Sum(r=>Math.Max(0,r.maxAmount-r.amount));
                if(!options.AllowedResources.Contains(pair.Key) || qty>options.MaxUnitsPerResource || qty>room+1e-6)
                    throw new InvalidOperationException("Amount exceeds tank space or dispatch limit for "+pair.Key);
                cargo.Add(new Cargo {Resource=pair.Key,Units=qty});
            }
            if(cargo.Count==0)throw new InvalidOperationException("Choose at least one cargo resource.");
            var o=new Order {Id=Guid.NewGuid().ToString("N"),Owner=Player,VesselId=v.id.ToString(),Anchor=receiver.part.flightID,Port=selectedPort,
                VesselName=v.vesselName,Body=v.mainBody.bodyName,Cargo=cargo};
            if(Connected){pendingId=o.Id;pendingRequest=o;Send(new Packet {Kind="Order",Order=o});}
            else {offline.Submit(o,Now);notice="Kerbin railgun charging. Your delivery is queued.";}
            amounts.Clear();warning="";
            Debug.Log("[RailgunLogistics] Requested "+o.Id+" for "+v.vesselName);
        }
        static List<PartResource> Tanks(Vessel v,string name)=>v.parts.Where(p=>p.FindModuleImplementing<RailgunCargoPod>()==null)
            .SelectMany(p=>p.Resources.Cast<PartResource>()).Where(r=>r.resourceName==name && r.flowState).ToList();
        static string CargoText(List<Cargo> cargo)=>string.Join(", ",cargo.Select(c=>c.Units.ToString("0.##",CultureInfo.InvariantCulture)+" "+c.Resource).ToArray());
        static string PackCargo(List<Cargo> cargo)=>string.Join(";",cargo.Select(c=>c.Resource+":"+c.Units.ToString("R",CultureInfo.InvariantCulture)).ToArray());
        static List<Cargo> UnpackCargo(string value)=>value.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Split(':')).Select(s=>new Cargo {Resource=s[0],Units=double.Parse(s[1],CultureInfo.InvariantCulture)}).ToList();
        void Apply(Order o)
        {
            if(o==null)return;
            var v=ActiveReceiver(o.Anchor);if(v==null || o.Owner!=Player || Now<o.Arrival)return;
            var receipt=v.parts.First(p=>p.flightID==o.Anchor).FindModuleImplementing<RailgunDeliveryReceipt>();
            try
            {
                if(!Rules.HasReceipt(receipt.receipts,o.Id))
                {
                    var podVessel=PhysicalPods.Find(o.Id);
                    if(podVessel==null)
                    {
                        if(o.PodSpawned){notice="Waiting for the existing cargo pod to sync. No replacement will be spawned.";return;}
                        if(spawning.Add(o.Id))StartCoroutine(PhysicalPods.Spawn(o,v,pod=>
                        {
                            spawning.Remove(o.Id);PublishPod(o,pod);notice="Physical cargo pod arriving. Keep the docking corridor clear and time at 1x.";
                        },error=>{spawning.Remove(o.Id);warning="Pod waiting: "+error.Split('\n')[0];Debug.LogError("[RailgunLogistics] "+error);}));
                        return;
                    }
                    if(!podVessel.loaded || podVessel.packed){notice="Cargo pod saved nearby — waiting for physics.";return;}
                    var pod=podVessel.parts.Select(p=>p.FindModuleImplementing<RailgunCargoPod>()).FirstOrDefault(p=>p?.orderId==o.Id);
                    if(pod==null)return;
                    if(spawning.Contains(o.Id))return;
                    if(!pod.inserted)pod.inserted=true; // Resume an already persisted pod without inserting another.
                    if(Connected && podVessel!=v)
                    {
                        LmpClient.Systems.Lock.LockSystem.Singleton.AcquireUpdateLock(podVessel.id);
                    }
                    if(!o.PodSpawned){PublishPod(o,podVessel);return;}
                    if(!pod.DockedToReceiver(out var docked) || docked!=v)
                    {notice="Cargo aboard real pod: "+pod.deliveryStatus+". No fuel transferred yet.";return;}
                    var manifest=PhysicalPods.Unpack(pod.cargoSpec);
                    if(pod.unloaded || manifest.Count!=o.Cargo.Count || manifest.Where((c,i)=>c.Resource!=o.Cargo[i].Resource || Math.Abs(c.Units-o.Cargo[i].Units)>1e-6).Any())
                        throw new InvalidOperationException("Pod manifest does not match the authorized dispatch.");
                    var changes=new List<Tuple<PartResource,double>>();var delivered=new List<Cargo>();
                    foreach(var cargo in o.Cargo)
                    {
                        var source=pod.part.Resources.Cast<PartResource>().FirstOrDefault(r=>r.resourceName==cargo.Resource);
                        if(source==null || source.amount<cargo.Units-1e-6)throw new InvalidOperationException("The physical pod no longer contains its full cargo.");
                        var tanks=Tanks(v,cargo.Resource);
                        var delta=Rules.Allocate(cargo.Units,tanks.Select(r=>r.amount).ToList(),tanks.Select(r=>r.maxAmount).ToList());
                        for(int i=0;i<tanks.Count;i++)changes.Add(Tuple.Create(tanks[i],delta[i]));
                        delivered.Add(new Cargo {Resource=cargo.Resource,Units=delta.Sum()});
                    }
                    // Do not consume the dispatch into locked/full tanks. Retry when there is enough room.
                    if(delivered.Where((c,i)=>c.Units<o.Cargo[i].Units-1e-6).Any())
                    {notice="Cargo holding nearby: unlock enough matching tank space for the full delivery.";return;}
                    foreach(var cargo in o.Cargo)pod.part.Resources[cargo.Resource].amount-=cargo.Units;
                    foreach(var change in changes)change.Item1.amount+=change.Item2;
                    pod.unloaded=true;pod.deliveryStatus="Delivered — empty pod attached";
                    receipt.lastCargo=PackCargo(delivered);receipt.lastId=o.Id;receipt.receipts=Rules.AddReceipt(receipt.receipts,o.Id);
                    notice="Pod docked. Cargo unloaded: "+CargoText(delivered);
                    ScreenMessages.PostScreenMessage(notice,7,ScreenMessageStyle.UPPER_CENTER);
                    Debug.Log("[RailgunLogistics] Physical pod docked and unloaded "+o.Id+" exactly once to "+v.id+": "+CargoText(delivered));
                }
                if(receipt.lastId!=o.Id)throw new InvalidOperationException("Historical receipt requires operator review; cargo was not applied twice.");
                var actual=UnpackCargo(receipt.lastCargo);
                if(Connected)
                {
                    VesselProtoSystem.Singleton.MessageSender.SendVesselMessage(v,false,"Railgun cargo receipt "+o.Id);
                    Send(new Packet {Kind="Receipt",Order=new Order {Id=o.Id,Anchor=o.Anchor,VesselId=v.id.ToString(),Delivered=actual}});
                }
                else
                {
                    // Save receipt and resources together before marking the sidecar ledger delivered.
                    string saved=GamePersistence.SaveGame("persistent",HighLogic.SaveFolder,SaveMode.OVERWRITE);
                    string path=Path.Combine(KSPUtil.ApplicationRootPath,"saves",HighLogic.SaveFolder,"persistent.sfs");
                    if(string.IsNullOrEmpty(saved) || !File.Exists(path) || !File.ReadAllText(path).Contains(o.Id))
                        throw new IOException("Waiting for the vessel receipt to reach the save file");
                    offline.Confirm(o.Id,true,actual);
                }
            }
            catch(Exception ex){warning="Delivery waiting: "+ex.Message;Debug.LogError("[RailgunLogistics] "+ex);}
        }
        void PublishPod(Order order,Vessel pod)
        {
            if(Connected)
            {
                LmpClient.Systems.Lock.LockSystem.Singleton.AcquireUpdateLock(pod.id);
                VesselProtoSystem.Singleton.MessageSender.SendVesselMessage(pod,false,"Railgun physical cargo pod "+order.Id);
                Send(new Packet {Kind="Pod",Order=new Order {Id=order.Id,VesselId=FlightGlobals.ActiveVessel.id.ToString(),Anchor=order.Anchor,PodId=pod.id.ToString()}});
            }
            else
            {
                string saved=GamePersistence.SaveGame("persistent",HighLogic.SaveFolder,SaveMode.OVERWRITE);
                string path=Path.Combine(KSPUtil.ApplicationRootPath,"saves",HighLogic.SaveFolder,"persistent.sfs");
                if(string.IsNullOrEmpty(saved) || !File.Exists(path) || !File.ReadAllText(path).Contains(order.Id))throw new IOException("Waiting for cargo pod persistence.");
                offline.ConfirmPod(order.Id,true);
            }
        }
        static string Remaining(long seconds)
        {
            seconds=Math.Max(0,seconds);return (seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
        }
        void OnGUI()
        {
            if(!visible || HighLogic.CurrentGame==null){InputLockManager.RemoveControlLock(InputLock);return;}
            GUI.skin=HighLogic.Skin;
            window.height=620;
            window=GUILayout.Window(17269031,window,Draw,"KERBIN RAILGUN  /  LOGISTICS",GUILayout.Width(490));
            var mouse=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
            if(window.Contains(mouse))InputLockManager.SetControlLock(ControlTypes.ALL_SHIP_CONTROLS,InputLock);
            else InputLockManager.RemoveControlLock(InputLock);
        }
        void Draw(int id)
        {
            GUILayout.BeginHorizontal();GUILayout.Label(Connected?"SHARED LUNA DEPOT":"KERBIN LOGISTICS DEPOT");
            if(GUILayout.Button("Close",GUILayout.Width(65)))visible=false;GUILayout.EndHorizontal();
            GUILayout.Label("Physical cargo pod • assisted approach • stock docking before unloading");
            if(Multiplayer && !Connected)GUILayout.Label("Connect to your Luna server to use its logistics depot.");
            else if(!serviceReady)GUILayout.Label("Waiting for logistics service… server plugin must be installed.");
            var v=FlightGlobals.ActiveVessel;
            scroll=GUILayout.BeginScrollView(scroll,GUILayout.Height(420));
            if(HighLogic.LoadedSceneIsFlight && v!=null && FindReceiver(v)!=null)
            {
                GUILayout.Label("RECEIVER: "+v.vesselName);
                var ports=PhysicalPods.Ports(v);
                if(!ports.Any(p=>p.part.flightID==selectedPort))selectedPort=ports.FirstOrDefault()?.part.flightID??0;
                GUILayout.BeginHorizontal();
                GUILayout.Label(ports.Count==0?"No free standard Clamp-O-Tron port (size1).":
                    "Port "+(ports.FindIndex(p=>p.part.flightID==selectedPort)+1)+" / "+ports.Count+": "+ports.First(p=>p.part.flightID==selectedPort).part.partInfo.title);
                if(ports.Count>1 && GUILayout.Button("Next port",GUILayout.Width(95)))selectedPort=ports[(ports.FindIndex(p=>p.part.flightID==selectedPort)+1)%ports.Count].part.flightID;
                GUILayout.EndHorizontal();
                GUILayout.Label("Cargo units   •   unlocked tank space");
                foreach(var res in options.AllowedResources)
                {
                    var tanks=Tanks(v,res);if(tanks.Count==0)continue;
                    double free=tanks.Sum(r=>Math.Max(0,r.maxAmount-r.amount));
                    if(!amounts.ContainsKey(res))amounts[res]="";
                    GUILayout.BeginHorizontal();GUILayout.Label(res,GUILayout.Width(120));
                    amounts[res]=GUILayout.TextField(amounts[res],14,GUILayout.Width(100));
                    GUILayout.Label("/ "+free.ToString("N0"),GUILayout.Width(95));
                    if(GUILayout.Button("Fill",GUILayout.Width(50)))amounts[res]=Math.Min(options.MaxUnitsPerResource,Math.Floor(free)).ToString("0",CultureInfo.InvariantCulture);
                    GUILayout.EndHorizontal();
                }
                bool prior=GUI.enabled;GUI.enabled=serviceReady && pendingRequest==null;
                if(GUILayout.Button(pendingRequest==null?"DISPATCH FROM KERBIN":"Awaiting dispatch confirmation…"))
                    try{Dispatch();}catch(Exception ex){warning=ex.Message;}
                GUI.enabled=prior;
                GUILayout.Label("Charge: "+options.ChargeSeconds+"s • Kerbin transit: "+options.KerbinTransitSeconds+"s • Other bodies: "+options.OtherTransitSeconds+"s");
            }
            else GUILayout.Label("Fly the ship that needs supplies. Its command pod or probe core has a Request railgun supplies menu.");
            GUILayout.Space(12);GUILayout.Label("DELIVERY BOARD");
            foreach(var o in orders.Where(o=>o.Status!="Cancelled").Reverse().Take(12))
            {
                var pod=PhysicalPods.Find(o.Id);
                var cargoPod=pod?.loaded==true?pod.parts.Select(p=>p.FindModuleImplementing<RailgunCargoPod>()).FirstOrDefault(p=>p?.orderId==o.Id):null;
                string phase=o.Status=="Delivered"?(o.Port==0?"Delivered (previous version)":"Docked and unloaded"):Now<o.Launch?"Charging "+Remaining(o.Launch-Now):Now<o.Arrival?"In transit "+Remaining(o.Arrival-Now):cargoPod?.deliveryStatus??"Waiting for receiver / physical pod";
                GUILayout.Label(o.VesselName+" • "+o.Owner+"\n"+phase+"\n"+CargoText(o.Cargo));
                if(o.Owner==Player && o.Status=="InFlight" && GUILayout.Button("Cancel delivery",GUILayout.Width(145)))
                {
                    try{if(Connected)Send(new Packet{Kind="Cancel",Order=new Order{Id=o.Id}});else offline.Cancel(o.Id,Player);}
                    catch(Exception ex){warning=ex.Message;}
                }
                GUILayout.Space(8);
            }
            GUILayout.EndScrollView();
            GUILayout.Label(string.IsNullOrEmpty(warning)?notice:warning);
            GUILayout.Label("Keep 1x time and the port clear. The empty pod stays attached; undock normally.");
            GUI.DragWindow(new Rect(0,0,420,24));
        }
    }
}
