using System;
using System.Globalization;
using System.IO;
using System.Linq;
using LmpCommon.Message.Data;
using LmpCommon.Message.Server;
using LmpCommon.Time;
using RailgunLogistics;
using RailgunSandbox;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Plugin;
using Server.Server;
using Server.System;

namespace RailgunLogisticsServer
{
    public sealed class ReusableFleetService
    {
        readonly object gate = new object();
        readonly System.Collections.Generic.Dictionary<string, long> missingSince = new System.Collections.Generic.Dictionary<string, long>();
        FleetAuthority authority;
        CoastWarpService coastWarp;
        public void Start()
        {
            string directory = Path.Combine(ServerContext.UniverseDirectory, "RailgunReusable"); Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "Fleet.xml");
            var state = File.Exists(file) ? XmlFile.Read<SharedFleet>(file) : SharedFleet.New();
            state.Validate(); state.RestartSession();
            authority = new FleetAuthority(state, next => XmlFile.Save(file, next));
            XmlFile.Save(file, authority.Snapshot());
            if (!LmpModInterface.RegisterModHandler(FleetWire.Channel, Receive)) throw new IOException("Duplicate reusable-fleet handler.");
            coastWarp=new CoastWarpService(gate,()=>authority.Snapshot(),(client,number)=> {
                var current=authority.Snapshot();var slot=current.Slots.SingleOrDefault(s=>s.Pod.Number==number);
                if(slot==null||slot.Pilot!=client.PlayerName||slot.LeaseUntil<=Rules.Now+15||!CoastWarpRules.PhaseAllowed(slot.Pod.Phase)||client.Subspace!=current.Subspace)return false;
                var facts=Facts(client,new FleetMessage {Number=number},Rules.Now);
                return facts.OwnsPodUpdate&&facts.PodSnapshotVerified;
            });coastWarp.Start();
            LunaLog.Normal("[RailgunReusable] Shared two-pod authority ready; native locks, receipts and return checks required.");
        }
        public void Stop() {coastWarp?.Stop();LmpModInterface.UnregisterModHandler(FleetWire.Channel);}
        static string Field(Server.System.Vessel.Classes.Part part, string name) => part?.Fields.GetSingle(name)?.Value;
        static string Module(Server.System.Vessel.Classes.Part part, string module, string key) =>
            part?.Modules.GetAll().FirstOrDefault(m => m.Key == module)?.Value?.GetValue(key)?.Value;
        static Guid PodVessel(string fleetId)
        {
            foreach (var vessel in VesselStoreSystem.CurrentVessels)
                if (vessel.Value.Parts.GetAllValues().Any(p => Module(p, "RailgunFlightPod", "fleetId") == fleetId)) return vessel.Key;
            return Guid.Empty;
        }
        static bool True(string text) => string.Equals(text, "True", StringComparison.OrdinalIgnoreCase);
        static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && FlightRules.Finite(value) ? value : double.NaN;
        FleetFacts Facts(ClientStructure client, FleetMessage request, long now)
        {
            var state = authority.Snapshot(); var slot = state.Slots.SingleOrDefault(s => s.Pod.Number == request.Number);
            var proposed = request.Pod ?? slot?.Pod;
            var expected = FleetWire.NativeRecord(slot, request);
            var facts = new FleetFacts { Subspace = client.Subspace };
            if (expected == null) return facts;
            if (WarpContext.Subspaces.TryGetValue(client.Subspace, out var current)) {
                double gameTime = (LunaNetworkTime.UtcNow - TimeContext.StartTime).TotalSeconds + current.Time;
                facts.GameTime = gameTime;
                facts.AbandonedTimeline = !ClientRetriever.GetAuthenticatedClients().Any(c => c.Subspace == state.Subspace);
                // Subspace offsets can differ slightly due to normal clock
                // correction. The monotonic boundary is the last committed
                // flight time, not equality or ordering of those offsets.
                facts.ForwardTimeline = FlightRules.Finite(gameTime);
                facts.OtherFlightPilotsAligned = state.Slots.Where(s => FlightRules.Busy(s.Pod) && s.Pilot != "" && s.Pilot != client.PlayerName && s.LeaseUntil > now)
                    .All(s => ClientRetriever.GetClientByName(s.Pilot)?.Subspace == client.Subspace);
            }
            var podId = PodVessel(expected.VesselId);
            if (podId != Guid.Empty && VesselStoreSystem.CurrentVessels.TryGetValue(podId, out var native)) {
                var parts = native.Parts.GetAllValues().ToList();
                facts.PodSnapshotVerified = expected.PartIds.Count > 0 && expected.PartIds.All(id => parts.Any(p => Field(p, "uid") == id.ToString(CultureInfo.InvariantCulture))) &&
                    parts.Any(p => Module(p, "RailgunFlightPod", "fleetId") == expected.VesselId);
                if (slot?.Pod.Created == true && !facts.PodSnapshotVerified) {
                    if (!missingSince.ContainsKey(proposed.VesselId)) missingSince[proposed.VesselId] = now;
                    facts.PodMissingConfirmed = now - missingSince[proposed.VesselId] >= 20;
                } else missingSince.Remove(proposed.VesselId);
                facts.OwnsPodUpdate = (!LockSystem.LockQuery.ControlLockExists(podId) || LockSystem.LockQuery.ControlLockBelongsToPlayer(podId, client.PlayerName)) &&
                    (LockSystem.LockQuery.UpdateLockBelongsToPlayer(podId, client.PlayerName) ||
                    LockSystem.LockQuery.UnloadedUpdateLockBelongsToPlayer(podId, client.PlayerName) || LockSystem.LockQuery.ControlLockBelongsToPlayer(podId, client.PlayerName));
                string situation = native.Fields.GetSingle("sit")?.Value;
                double lat = Number(native.Fields.GetSingle("lat")?.Value), lon = Number(native.Fields.GetSingle("lon")?.Value);
                var home = new FleetState();
                double padLat = home.HomeLatitude + 6.0 / 600000 * 180 / Math.PI;
                double padLon = home.HomeLongitude + (proposed.Number == 1 ? -36 : 36) / (600000 * Math.Cos(padLat * Math.PI / 180)) * 180 / Math.PI;
                double distance = Math.Sqrt(Math.Pow((lat - padLat) * Math.PI / 180 * 600000, 2) + Math.Pow((lon - padLon) * Math.PI / 180 * 600000 * Math.Cos(padLat * Math.PI / 180), 2));
                facts.PodOnPad = facts.PodSnapshotVerified && situation == "LANDED" && native.GetOrbitingBodyName() == "Kerbin" && distance <= 25;
                facts.LandedAtDepot = facts.PodOnPad;
                var own = parts.FirstOrDefault(p => Field(p, "uid") == proposed.OwnPort.ToString());
                var target = parts.FirstOrDefault(p => Field(p, "uid") == proposed.TargetPort.ToString());
                facts.Docked = own != null && target != null && parts.Any(p => Field(p, "uid") == proposed.TargetAnchor.ToString()) &&
                    (Module(own, "ModuleDockingNode", "state") ?? "").StartsWith("Docked", StringComparison.Ordinal) &&
                    (Module(target, "ModuleDockingNode", "state") ?? "").StartsWith("Docked", StringComparison.Ordinal) &&
                    Module(own, "ModuleDockingNode", "dockUId") == proposed.TargetPort.ToString() && Module(target, "ModuleDockingNode", "dockUId") == proposed.OwnPort.ToString();
                var marker = parts.First(p => Module(p, "RailgunFlightPod", "fleetId") == proposed.VesselId);
                facts.PlacementSettled = True(Module(marker, "RailgunFlightPod", "placementSettled"));
                if (Module(marker, "RailgunFlightPod", "completedOrder") == proposed.OrderId && !string.IsNullOrEmpty(proposed.OrderId) && facts.Docked) {
                    VesselStoreSystem.BackupVessels();
                    string path = Path.Combine(VesselStoreSystem.VesselsPath, podId + VesselStoreSystem.VesselFileFormat);
                    facts.ReceiptDurable = File.Exists(path) && File.ReadAllText(path).Contains(proposed.OrderId);
                }
                // Native identity and original IDs must exist on disk before initial construction is acknowledged.
                if (slot != null && !slot.Pod.Created && proposed.Created && facts.PodSnapshotVerified) {
                    VesselStoreSystem.BackupVessels();
                    string path = Path.Combine(VesselStoreSystem.VesselsPath, podId + VesselStoreSystem.VesselFileFormat);
                    facts.PodSnapshotVerified = File.Exists(path) && File.ReadAllText(path).Contains(proposed.VesselId);
                }
            } else if (slot?.Pod.Created == true) {
                if (!missingSince.ContainsKey(proposed.VesselId)) missingSince[proposed.VesselId] = now;
                facts.PodMissingConfirmed = now - missingSince[proposed.VesselId] >= 20;
            }
            if (request.Kind == "Reserve") foreach (var vessel in VesselStoreSystem.CurrentVessels) {
                var parts = vessel.Value.Parts.GetAllValues().ToList();
                if (!parts.Any(p => Field(p, "uid") == proposed.TargetAnchor.ToString())) continue;
                var port = parts.FirstOrDefault(p => Field(p, "uid") == proposed.TargetPort.ToString());
                facts.ReceiverOwned = LockSystem.LockQuery.ControlLockBelongsToPlayer(vessel.Key, client.PlayerName);
                facts.ReceiverReady = port != null && Module(port, "ModuleDockingNode", "state") == "Ready" &&
                    vessel.Value.GetOrbitingBodyName() == "Kerbin" && vessel.Value.Fields.GetSingle("sit")?.Value == "ORBITING" &&
                    !parts.Any(p => Module(p, "RailgunFlightPod", "fleetId") != null);
                if (port != null) try { DockingPortPolicy.ValidateHardware(Field(port, "name")); } catch { facts.ReceiverReady = false; }
                break;
            }
            return facts;
        }
        void Send(ClientStructure client, FleetMessage message)
        {
            byte[] bytes = FleetWire.Encode(message);
            var data = ServerContext.ServerMessageFactory.CreateNewMessageData<ModMsgData>();
            data.ModName = FleetWire.Channel; data.Data = bytes; data.NumBytes = bytes.Length; data.Reliable = true; data.Relay = false;
            MessageQueuer.SendToClient<ModSrvMsg>(client, data);
        }
        void Receive(ClientStructure client, byte[] bytes, int count)
        {
            if (client == null || !client.Authenticated) return;
            lock (gate) {
                FleetMessage request = null;
                try {
                    request = FleetWire.Decode(bytes, count);
                    long now = Rules.Now;
                    if(request.Kind=="LoadingView") {
                        var current=authority.Snapshot();var facts=Facts(client,request,now);
                        if(!LoadingViewRules.Accept(current,client.PlayerName,request,facts,now))return;
                        var view=new FleetMessage {Kind="LoadingView",Epoch=current.Epoch,Number=request.Number,
                            LoadingOrder=request.LoadingOrder,LoadingSeconds=request.LoadingSeconds,LoadingSampleUt=request.LoadingSampleUt};
                        foreach(var peer in ClientRetriever.GetAuthenticatedClients().Where(c=>c.Subspace==client.Subspace))Send(peer,view);
                        return;
                    }
                    var snapshot = authority.Apply(client.PlayerName, request, Facts(client, request, now), now);
                    Send(client, new FleetMessage { Kind = "State", Request = request.Request, Fleet = snapshot, ServerNow = now });
                } catch (Exception ex) {
                    LunaLog.Debug("[RailgunReusable] " + ex.Message);
                    Send(client, new FleetMessage { Kind = "Error", Request = request?.Request ?? "", Error = ex.Message, Fleet = authority?.Snapshot(), ServerNow = Rules.Now });
                }
            }
        }
    }
}
