using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LmpClient;
using LmpClient.Events;
using LmpClient.Network;
using LmpClient.Systems.Lock;
using LmpClient.Systems.ModApi;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.VesselProtoSys;
using LmpClient.Systems.Warp;
using LmpCommon.Enums;
using LmpCommon.Message.Data;
using UnityEngine;

namespace RailgunSandbox
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class SharedFleetConnection : MonoBehaviour
    {
        FleetState view;
        void Awake() { DontDestroyOnLoad(gameObject); }
        void Update() {
            if (NetworkFleet.IsMultiplayer || NetworkFleet.Connected) NetworkFleet.Shared.Pump(ref view);
            NetworkFleet.Shared.GuardFlightTime();
        }
        void OnApplicationQuit() { NetworkFleet.Shared.Dispose(); }
    }
    public sealed class NetworkFleet : IDisposable
    {
        public static readonly NetworkFleet Shared = new NetworkFleet();
        FleetState view;
        public FleetState Local => view;
        public bool SetupRequested, FollowFlight;
        public bool ConstructionAccepted => Ready && authority.Slots.All(s => s.Pod.Created);
        sealed class Pending { public FleetMessage Message; public float Sent; }
        readonly ConcurrentQueue<FleetMessage> incoming = new ConcurrentQueue<FleetMessage>();
        readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        readonly Dictionary<Guid, float> nextLockRequest = new Dictionary<Guid, float>();
        static readonly HashSet<int> requestedControl = new HashSet<int>();
        SharedFleet authority;
        bool subscribed;
        float lastSnapshot = -100, nextPoll, snapshotAt, nextResync;
        long serverTime;
        public string Error { get; private set; } = "";
        public static bool IsMultiplayer => HighLogic.SaveFolder == "LunaMultiplayer";
        public static bool Connected => MainSystem.NetworkState == ClientState.Running;
        public string Player => SettingsSystem.CurrentSettings.PlayerName;
        public bool HasSnapshot => Connected && authority != null && Time.realtimeSinceStartup - lastSnapshot < 4;
        public bool Ready => HasSnapshot &&
            WarpSystem.Singleton.CurrentSubspace >= 0 && (authority.Subspace == int.MinValue || WarpSystem.Singleton.CurrentSubspace == authority.Subspace);
        public string Status => !HasSnapshot ? "Waiting for the shared depot connection." : WarpSystem.Singleton.CurrentSubspace < 0 ?
            "Guidance paused during time warp. Return to 1x to resume." : !Ready ?
            "Guidance paused after a Luna time change; recovering shared flight synchronization. " + Error : Error;
        const string TimeLock = "RailgunReusable.GuidedFlightTime";
        public void GuardFlightTime()
        {
            bool guided = HasSnapshot && authority.Slots.Any(s => s.Pilot == Player && s.LeaseUntil > Now && FlightRules.Busy(s.Pod));
            if (guided) {
                InputLockManager.SetControlLock(ControlTypes.TIMEWARP, TimeLock);
                if (TimeWarp.CurrentRate != 1) TimeWarp.SetRate(0, true);
            } else InputLockManager.RemoveControlLock(TimeLock);
        }
        double Now => FleetClock.Estimate(serverTime, Time.realtimeSinceStartup, snapshotAt);
        FleetSlot Slot(int number) => authority?.Slots.SingleOrDefault(s => s.Pod.Number == number);
        public string Pilot(int number) => Slot(number)?.Pilot ?? "";
        public string Dispatcher(int number) => Slot(number)?.Dispatcher ?? "";
        public bool HasActivePilot(int number)
        {
            var slot = Slot(number);
            return Ready && slot != null && slot.Pilot != "" && slot.Lease != "" && slot.LeaseUntil > Now + 2;
        }
        bool OwnsDockedReceiver(PodRecord pod)
        {
            if (pod.Phase != FlightPhase.Docking && pod.Phase != FlightPhase.Unloading) return false;
            var vessel = StockPods.Vessel(pod);
            if (vessel == null || !vessel.loaded || !NativeOwner(pod, false)) return false;
            var own = vessel.parts.FirstOrDefault(p => p.flightID == pod.OwnPort)?.FindModuleImplementing<ModuleDockingNode>();
            return own?.otherNode?.part.flightID == pod.TargetPort && own.otherNode.vessel == vessel &&
                vessel.parts.Any(p => p.flightID == pod.TargetAnchor);
        }
        public string Diagnostics(PodRecord pod)
        {
            var slot = Slot(pod.Number);
            return "revision=" + slot?.Revision + " pilot=" + slot?.Pilot + " leaseSeconds=" + (slot == null ? 0 : slot.LeaseUntil - Now).ToString("F1") +
                " valid=" + LeaseValid(pod.Number) + " nativeOwner=" + NativeOwner(pod, false) +
                " pending=" + (pending.TryGetValue(pod.Number, out var queued) ? queued.Message.Kind + ":" + queued.Message.Request : "none");
        }
        public bool CanTakeControl(int number) => Ready && Slot(number) != null && (Slot(number).Pilot == Player || Slot(number).LeaseUntil <= Now);
        public void RequestControl(PodRecord pod) { if (CanTakeControl(pod.Number)) requestedControl.Add(pod.Number); }
        public bool LeaseValid(int number)
        {
            var slot = Slot(number);
            return Ready && slot != null && slot.Pilot == Player && slot.Lease != "" && slot.LeaseUntil > Now + 2;
        }
        public bool NativeOwner(PodRecord pod, bool acquire = true)
        {
            var vessel = StockPods.Vessel(pod);
            if (vessel == null) return !pod.Created;
            var query = LockSystem.LockQuery;
            if (query.ControlLockExists(vessel.id) && !query.ControlLockBelongsToPlayer(vessel.id, Player)) return false;
            bool owns = vessel.loaded ? query.ControlLockBelongsToPlayer(vessel.id, Player) || query.UpdateLockBelongsToPlayer(vessel.id, Player) :
                query.UnloadedUpdateLockBelongsToPlayer(vessel.id, Player);
            if (!owns && acquire && Connected && (!nextLockRequest.TryGetValue(vessel.id, out float next) || Time.realtimeSinceStartup >= next)) {
                nextLockRequest[vessel.id] = Time.realtimeSinceStartup + 1;
                if (vessel.loaded) LockSystem.Singleton.AcquireUpdateLock(vessel.id);
                else LockSystem.Singleton.AcquireUnloadedUpdateLock(vessel.id);
            }
            return owns;
        }
        public bool CanAct(PodRecord pod) => LeaseValid(pod.Number) && !pending.ContainsKey(pod.Number) && NativeOwner(pod);
        public bool CanReserve(PodRecord pod)
        {
            var slot = Slot(pod.Number);
            return Ready && slot != null && !pending.ContainsKey(pod.Number) && slot.Pod.Created && slot.Pod.Phase == FlightPhase.Ready &&
                (slot.Pilot == Player || slot.LeaseUntil <= Now);
        }
        void Receive(string name, byte[] bytes)
        {
            if (name != FleetWire.Channel) return;
            try { incoming.Enqueue(FleetWire.Decode(bytes, bytes.Length)); }
            catch (Exception ex) { Debug.LogWarning("[RailgunReusable] Invalid server message: " + ex.Message); }
        }
        void Send(FleetMessage message)
        {
            byte[] bytes = FleetWire.Encode(message);
            var data = NetworkMain.CliMsgFactory.CreateNewMessageData<ModMsgData>();
            data.ModName = FleetWire.Channel; data.Data = bytes; data.NumBytes = bytes.Length; data.Reliable = true; data.Relay = false;
            ModApiSystem.Singleton.MessageSender.SendMessage(data);
        }
        public void Pump(ref FleetState local)
        {
            if (view != null) local = view;
            if (!subscribed && ModApiEvent.onModMessageReceived != null) { ModApiEvent.onModMessageReceived.Add(Receive); subscribed = true; }
            if (!Connected) { authority = null; view = local = null; SetupRequested = FollowFlight = false; pending.Clear(); requestedControl.Clear(); Error = "Waiting for the Luna connection."; return; }
            while (incoming.TryDequeue(out var message)) {
                if (message.Fleet == null) { Error = message.Error; continue; }
                message.Fleet.Validate();
                var previousAuthority = authority;
                if (authority != null && authority.Epoch != message.Fleet.Epoch) { pending.Clear(); requestedControl.Clear(); }
                else if (authority != null) {
                    // Independent slot revisions prevent a delayed response from undoing
                    // either player's already acknowledged state.
                    foreach (var previous in authority.Slots) {
                        int index = message.Fleet.Slots.FindIndex(s => s.Pod.Number == previous.Pod.Number);
                        if (message.Fleet.Slots[index].Revision < previous.Revision) message.Fleet.Slots[index] = FleetWire.Copy(previous);
                    }
                    if (message.ServerNow < serverTime) continue;
                }
                foreach (var item in pending.ToArray()) {
                    var remote = message.Fleet.Slots.Single(s => s.Pod.Number == item.Key);
                    bool acknowledged = remote.LastRequest == item.Value.Message.Request;
                    bool stale = remote.Revision != item.Value.Message.Revision || remote.Pilot != Player && item.Value.Message.Kind == "Commit";
                    if (acknowledged || stale) pending.Remove(item.Key);
                }
                authority = message.Fleet; serverTime = message.ServerNow; snapshotAt = lastSnapshot = Time.realtimeSinceStartup;
                if (message.Kind == "Error") Error = message.Error; else Error = "";
                if (local == null) local = new FleetState { Pods = authority.Slots.Select(s => FleetWire.Copy(s.Pod)).ToList() };
                else foreach (var slot in authority.Slots) {
                    if (pending.ContainsKey(slot.Pod.Number)) continue;
                    if (LeaseValid(slot.Pod.Number) && FleetReconcile.KeepLocal(previousAuthority, authority, slot, Player)) continue;
                    int index = local.Pods.FindIndex(p => p.Number == slot.Pod.Number);
                    local.Pods[index] = FleetWire.Copy(slot.Pod);
                }
                view = local;
            }
            if (Time.realtimeSinceStartup < nextPoll) return; nextPoll = Time.realtimeSinceStartup + 1;
            Send(new FleetMessage { Kind = "List" });
            if (HasSnapshot && WarpSystem.Singleton.CurrentSubspace >= 0 && authority.Subspace != int.MinValue && WarpSystem.Singleton.CurrentSubspace != authority.Subspace) {
                foreach (var item in pending.Where(p => p.Value.Message.Kind != "Resynchronize").ToArray()) {
                    pending.Remove(item.Key);
                    if (local != null) local.Pods[local.Pods.FindIndex(p => p.Number == item.Key)] = FleetWire.Copy(Slot(item.Key).Pod);
                }
            }
            if (local != null && Ready && pending.Values.Any(p => p.Message.Kind == "Commit")) PublishNative(local);
            foreach (var queued in pending.Values) if (Time.realtimeSinceStartup - queued.Sent >= 1) {
                queued.Sent = Time.realtimeSinceStartup; Send(queued.Message);
            }
            if (local == null || !HasSnapshot) return;
            if (!Ready) {
                if (WarpSystem.Singleton.CurrentSubspace >= 0 && Time.realtimeSinceStartup >= nextResync) {
                    nextResync = Time.realtimeSinceStartup + 5;
                    var candidate = local.Pods.FirstOrDefault(p => p.Created && p.Phase != FlightPhase.Lost &&
                        (Pilot(p.Number) == Player || Dispatcher(p.Number) == Player || FlightGlobals.ActiveVessel == StockPods.Vessel(p)) && NativeOwner(p));
                    if (candidate != null && !pending.ContainsKey(candidate.Number)) Queue(Slot(candidate.Number), "Resynchronize", null);
                }
                return;
            }
            foreach (var pod in local.Pods.Where(p => FlightRules.Busy(p) && Dispatcher(p.Number) == Player && NativeOwner(p, false)))
                if (!LeaseValid(pod.Number) && CanTakeControl(pod.Number)) requestedControl.Add(pod.Number);
            // Luna assigns background orbit updates independently of gameplay
            // control. Cooperatively release only OUR background locks when the
            // server grants a pod to another pilot. Never displace a control lock.
            foreach (var slot in authority.Slots.Where(s => s.Pilot != "" && s.Pilot != Player && s.LeaseUntil > Now)) {
                var vessel = StockPods.Vessel(slot.Pod);
                if (vessel == null || LockSystem.LockQuery.ControlLockExists(vessel.id)) continue;
                if (LockSystem.LockQuery.UpdateLockBelongsToPlayer(vessel.id, Player) || LockSystem.LockQuery.UnloadedUpdateLockBelongsToPlayer(vessel.id, Player))
                    LockSystem.Singleton.ReleaseAllVesselLocks(null, vessel.id);
            }
            foreach (var pod in local.Pods) {
                var slot = Slot(pod.Number);
                if (slot.Pilot != Player && !pending.ContainsKey(pod.Number) && OwnsDockedReceiver(pod)) Queue(slot, "Acquire", null);
            }
            foreach (int number in requestedControl.ToArray()) {
                if (LeaseValid(number)) requestedControl.Remove(number);
                else Acquire(local.Pods.Single(p => p.Number == number));
            }
            foreach (var pod in local.Pods.Where(p => p.Created && p.Phase != FlightPhase.Lost)) {
                var vessel = StockPods.Vessel(pod);
                if (vessel == null || !StockPods.OriginalPartsIntact(pod, vessel)) {
                    if (!pending.ContainsKey(pod.Number)) Queue(Slot(pod.Number), "ObserveMissing", null);
                } else if (pending.TryGetValue(pod.Number, out var probe) && probe.Message.Kind == "ObserveMissing") pending.Remove(pod.Number);
            }
            foreach (var slot in authority.Slots) if (LeaseValid(slot.Pod.Number) && !pending.ContainsKey(slot.Pod.Number) && slot.LeaseUntil - Now < 12)
                Queue(slot, "Heartbeat", null);
        }
        void Queue(FleetSlot slot, string kind, PodRecord proposed)
        {
            if (pending.ContainsKey(slot.Pod.Number)) return;
            var request = new FleetMessage { Kind = kind, Epoch = authority.Epoch, Number = slot.Pod.Number, Revision = slot.Revision,
                Lease = slot.Lease, Request = Guid.NewGuid().ToString("N"), Pod = proposed == null ? null : FleetWire.Copy(proposed) };
            pending[slot.Pod.Number] = new Pending { Message = request, Sent = Time.realtimeSinceStartup }; Send(request);
        }
        public void Acquire(PodRecord pod)
        {
            if (!Ready || !VesselProtoSystem.Singleton.ProtoSystemReady) { Error = "Wait for the native Luna world to synchronize."; return; }
            var slot = Slot(pod.Number); if (slot == null || pending.ContainsKey(pod.Number) || LeaseValid(pod.Number)) return;
            if (slot.LeaseUntil > Now && slot.Pilot != Player) { Error = "Pod " + pod.Number + " is controlled by " + slot.Pilot + "."; return; }
            if (pod.Created && !NativeOwner(pod)) { Error = "Waiting for the pod's native update lock."; return; }
            Queue(slot, "Acquire", null);
        }
        public void PublishNative(FleetState local)
        {
            if (!Ready) throw new InvalidOperationException("Waiting for shared depot authority.");
            foreach (var pod in local.Pods) {
                if (!LeaseValid(pod.Number)) continue;
                var vessel = StockPods.Vessel(pod);
                if (vessel != null && NativeOwner(pod)) VesselProtoSystem.Singleton.MessageSender.SendVesselMessage(vessel, false, "Shared reusable pod " + pod.Number + " / " + pod.OrderId);
            }
        }
        public void Save(FleetState local)
        {
            if (!Ready) throw new InvalidOperationException("Waiting for shared depot authority.");
            foreach (var pod in local.Pods) {
                var slot = Slot(pod.Number); if (slot == null || pending.ContainsKey(pod.Number) || FleetWire.Digest(slot.Pod) == FleetWire.Digest(pod)) continue;
                bool reserve = slot.Pod.Phase == FlightPhase.Ready && pod.Phase == FlightPhase.Reserved;
                if (!reserve && !LeaseValid(pod.Number)) throw new InvalidOperationException("Acquire server control of this pod before changing it.");
                Queue(slot, reserve ? "Reserve" : "Commit", pod);
            }
        }
        public void Dispose()
        {
            if (subscribed && ModApiEvent.onModMessageReceived != null) ModApiEvent.onModMessageReceived.Remove(Receive);
            pending.Clear(); authority = null;
            InputLockManager.RemoveControlLock(TimeLock);
        }
    }
}
