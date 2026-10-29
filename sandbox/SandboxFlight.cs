using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using MuMech;
using UnityEngine;

namespace RailgunSandbox
{
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public sealed class SandboxAutoStart : MonoBehaviour
    {
        static bool started;
        System.Collections.IEnumerator Start()
        {
            if (started || !Environment.GetCommandLineArgs().Any(arg => arg == "-railgun-sandbox" || arg == "-railgun-demo" || arg == "-railgun-reentry" || arg == "-railgun-railonly" || arg == "-railgun-shell" || arg == "-railgun-porttest")) yield break;
            started = true;
            yield return new WaitForSeconds(2);
            try {
                if (NetworkFleet.Connected) throw new InvalidOperationException("Do not start an isolated fixture while connected to Luna.");
                // Luna may initialize SaveFolder to its new blank save at the
                // main menu. An explicit test flag must not load that save.
                string testSave = FlightRules.DevelopmentSave(Environment.GetCommandLineArgs());
                HighLogic.SaveFolder = testSave;
                var game = GamePersistence.LoadGame("persistent", testSave, true, false);
                if (game == null || game.Mode != Game.Modes.SANDBOX) throw new InvalidOperationException("Missing isolated railgun sandbox save.");
                // Game.Load reads CurrentGame.Mode before assigning itself; reproduce the native resume flow.
                HighLogic.CurrentGame = game;
                int focus = 0;
                if (testSave == "Railgun-Shell-Test") {
                    int shellPod = game.flightState.protoVessels.FindIndex(v => v.vesselName == "KRL Reusable Pod 1");
                    if (shellPod >= 0) focus = shellPod;
                }
                if (testSave == "Railgun-Reentry-Test" || testSave == "Railgun-RailOnly-Test") {
                    focus = game.flightState.protoVessels.FindIndex(v => v.vesselName == "KRL Reusable Pod 1");
                    if (focus < 0) throw new InvalidOperationException("The isolated flight fixture is missing its original first pod.");
                    // Focusing a distant receiver first packs an airborne
                    // cradle pod below atmosphere; native KSP can destroy it
                    // before the observer gets a chance to select Watch pod.
                }
                FlightDriver.StartAndFocusVessel(game, focus);
            } catch (Exception ex) { Debug.LogError("[RailgunSandbox] Automatic local test load failed: " + ex); }
        }
    }
    // Standalone test save, or the server-owned fleet when joined through Luna.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class SandboxFlight : MonoBehaviour
    {
        public static SandboxFlight Instance { get; private set; }
        public void OpenMenu() { menuCollapsed = false;if(showcase)ShowcaseShot("ui"); }
        public static bool ReentryTestSave => !NetworkFleet.IsMultiplayer && Environment.GetCommandLineArgs().Contains("-railgun-reentry");
        public static bool ShellTestSave => !NetworkFleet.IsMultiplayer && Environment.GetCommandLineArgs().Contains("-railgun-shell");
        public static bool RailLaunchTestSave => !NetworkFleet.IsMultiplayer && (ShellTestSave || Environment.GetCommandLineArgs().Contains("-railgun-railonly"));
        public static bool PortSelectionTestSave => !NetworkFleet.IsMultiplayer && Environment.GetCommandLineArgs().Contains("-railgun-porttest");
        public static string SaveName => NetworkFleet.IsMultiplayer ? "LunaMultiplayer" : ShellTestSave ? "Railgun-Shell-Test" : PortSelectionTestSave ? "Railgun-Port-Selection-Test" : RailLaunchTestSave ? "Railgun-RailOnly-Test" : ReentryTestSave ? "Railgun-Reentry-Test" : Environment.GetCommandLineArgs().Contains("-railgun-demo") ? "Railgun-Reusable-Demo" : "Railgun-Reusable-Sandbox";
        FleetState state;
        NetworkFleet network;
        DepotModel model;
        CelestialBody home;
        string folder, error = "", detail = "Prepare the depot to create its two persistent stock-part pods.";
        string quantity = "100", resource = "MonoPropellant";
        uint selectedPort;
        bool enabledHere, faulted, follow, backgroundBefore;
        double ground, nextTick, nextStatus, nextLaunchSample;
        Rect window = new Rect(28, 32, 520, 710);
        AeroMenu menu;
        int selectedPod = 1;
        bool menuCollapsed, chooseDestination, choosePort, showFlightDetails, compactMenuPreview;
        Vector2 scroll;
        readonly Dictionary<string, string> armed = new Dictionary<string, string>();
        readonly HashSet<string> guidedByUs = new HashSet<string>();
        readonly Dictionary<int, DeliveryEstimate> deliveryEstimates = new Dictionary<int, DeliveryEstimate>();
        readonly Dictionary<int, string> deliveryEstimateKeys = new Dictionary<int, string>();
        readonly Dictionary<int, LoadingSequence> loadingSequences = new Dictionary<int, LoadingSequence>();
        readonly Dictionary<int, ShellLoadingRoute> shellRoutes = new Dictionary<int, ShellLoadingRoute>();
        ShellLoadingRoute ShellRoute(PodRecord p) {
            if (!shellRoutes.TryGetValue(p.Number, out var route)) shellRoutes[p.Number] = route = new ShellLoadingRoute();
            return route;
        }
        bool holdLoadingDemo;
        bool demoDaylight;
        double nextEtaSample;
        MonoBehaviour legacyService;
        bool legacyWasEnabled;
        bool showcase;
        GameObject showcasePivot;
        Transform showcaseReference;
        int recoverySamples;
        const string LockName = "RailgunSandbox.Window";
        double Now => Planetarium.GetUniversalTime();
        string Ledger => Path.Combine(folder, "RailgunReusableFleet.xml");
        System.Collections.IEnumerator Start()
        {
            if (HighLogic.SaveFolder != SaveName) yield break;
            while (!FlightGlobals.ready || HighLogic.CurrentGame == null) yield return null;
            enabledHere = HighLogic.SaveFolder == SaveName && HighLogic.CurrentGame != null && (NetworkFleet.IsMultiplayer || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX);
            if (!enabledHere) yield break;
            Instance = this;
            backgroundBefore = Application.runInBackground; Application.runInBackground = true;
            folder = Path.Combine(KSPUtil.ApplicationRootPath, "saves", SaveName);
            if (NetworkFleet.IsMultiplayer) {
                network = NetworkFleet.Shared; state = network.Local;
                while (enabledHere && !network.Ready) { network.Pump(ref state); yield return null; }
                follow = network.FollowFlight;
            }
            try {
                if (network == null) state = File.Exists(Ledger) ? Read<FleetState>(Ledger) : FleetState.New();
                state.Validate();
                home = FlightGlobals.Bodies.First(b => b.bodyName == state.HomeBody);
                var normal = home.GetRelSurfaceNVector(state.HomeLatitude, state.HomeLongitude);
                ground = Math.Max(0, home.pqsController.GetSurfaceHeight(normal) - home.pqsController.radius);
                model = new DepotModel(RailLaunchTestSave, ShellTestSave); SaveLedger();
                selectedPod = state.Pods.FirstOrDefault(p => p.Created && p.Phase == FlightPhase.Ready)?.Number ?? 1;
                // Prevent the earlier unlimited countdown service being invoked in this two-pod test save.
                legacyService = FindObjectsOfType<MonoBehaviour>().FirstOrDefault(c => c.GetType().FullName == "RailgunLogistics.Logistics");
                if (legacyService != null) { legacyWasEnabled = legacyService.enabled; legacyService.enabled = false; }
                Debug.Log("[RailgunSandbox] " + (network == null ? "Local" : "Server-owned shared") + " reusable depot enabled in " + SaveName + "; two slots; real 1x flight.");
            } catch (Exception ex) { Fail(ex); }
        }
        static T Read<T>(string path) { using (var file = File.OpenRead(path)) return (T)new XmlSerializer(typeof(T)).Deserialize(file); }
        void SaveLedger()
        {
            if (network != null) { network.Save(state); return; }
            if (faulted) throw new IOException("Fleet storage failed. Restart after restoring the matching sandbox save and fleet ledger.");
            try {
                state.Validate(); string temp = Ledger + ".tmp";
                using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    new XmlSerializer(typeof(FleetState)).Serialize(file, state); file.Flush(true);
                }
                if (File.Exists(Ledger)) File.Replace(temp, Ledger, Ledger + ".previous", true); else File.Move(temp, Ledger);
            } catch { faulted = true; throw; }
        }
        void PersistGame()
        {
            if (network != null) { network.PublishNative(state); network.Save(state); return; }
            string result = GamePersistence.SaveGame("persistent", SaveName, SaveMode.OVERWRITE);
            if (string.IsNullOrEmpty(result)) throw new IOException("KSP could not persist the physical pods.");
            SaveLedger();
        }
        void Fail(Exception ex) { if (error != ex.Message) Debug.LogError("[RailgunSandbox] " + ex); error = ex.Message; }
        void Change(PodRecord p, FlightPhase next)
        {
            FlightRules.Advance(p, next, Now); armed.Remove(p.VesselId); SaveLedger();
            Debug.Log("[RailgunSandbox] Pod " + p.Number + " -> " + next + " at UT " + Now.ToString("R", CultureInfo.InvariantCulture));
        }
        void Pad(PodRecord p, out double lat, out double lon)
        {
            lat = state.HomeLatitude + 6 / home.Radius * 180 / Math.PI;
            lon = state.HomeLongitude + DepotModel.PadX(p.Number) / (home.Radius * Math.Cos(lat * Math.PI / 180)) * 180 / Math.PI;
        }
        double PadDistance(PodRecord p, Vessel v)
        {
            Pad(p, out double lat, out double lon);
            return (home.GetWorldSurfacePosition(lat, lon, 0) - home.GetWorldSurfacePosition(v.latitude, v.longitude, 0)).magnitude;
        }
        void Prepare()
        {
            if (state.Pods.All(p => p.Created)) throw new InvalidOperationException("The depot already owns its two pods. Missing or lost pods are not replaced.");
            string missing = StockPods.Missing(); if (missing.Length > 0) throw new InvalidOperationException("KSP did not load required parts: " + missing);
            if (network != null) {
                network.SetupRequested = true;
                foreach (var pod in state.Pods.Where(p => !p.Created)) network.Acquire(pod);
            }
            foreach (var pod in state.Pods.Where(p => !p.Created)) {
                if (network != null && !network.CanAct(pod)) continue;
                // Recover the same UUID after a crash between the KSP save and sidecar commit.
                var existing = StockPods.Vessel(pod);
                if (existing != null) {
                    var snapshots = existing.protoVessel.protoPartSnapshots;
                    if (!snapshots.Any(part => part.modules.Any(m => m.moduleName == "RailgunFlightPod" && m.moduleValues.GetValue("fleetId") == pod.VesselId)))
                        throw new IOException("Fleet identity conflicts with an unrelated vessel.");
                    var shell = snapshots.SingleOrDefault(part => part.partName == LaunchShell.PartName);
                    var original = snapshots.Where(part => part != shell).ToArray();
                    pod.Created = true; pod.OriginalParts = original.Length; pod.PartIds = original.Select(part => part.flightID).ToList();
                    if (shell != null) {
                        var fairing = shell.modules.Single(m => m.moduleName == "ModuleProceduralFairing");
                        if (fairing.moduleValues.GetValue("fsm") != "st_idle") throw new IOException("Cannot recover an unused pod with an opened launch shell.");
                        pod.LaunchShellRequired = true; pod.LaunchShellPartId = shell.flightID;
                    }
                    pod.OwnPort = snapshots.Single(part => part.partInfo.name == "dockingPort2").flightID;
                } else { Pad(pod, out double lat, out double lon); StockPods.Spawn(pod, home, lat, lon, ground, ShellTestSave); }
                PersistGame();
            }
            detail = network == null ? "Two real stock-part pods created and saved. Let them settle, then select a receiver." : "Shared pod setup submitted; waiting for native synchronization and depot placement.";
            var created = state.Pods.FirstOrDefault(p => p.Created && (network == null || network.LeaseValid(p.Number)));
            if (created != null && network == null) Watch(created);
        }
        static string ConnectorType(ModuleDockingNode port)
        {
            // The actual receiving node is authoritative. TweakScale can change
            // its interface after prefab loading (for example size0 -> size1p5).
            var types = port.nodeTypes ?? new HashSet<string>((port.nodeType ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return DockingPortPolicy.SelectType(string.Join(",", types), port.gendered, port.snapRotation, types);
        }
        static bool SupportedPort(ModuleDockingNode port) { try { ConnectorType(port); DockingPortPolicy.ValidateHardware(port.part.partInfo.name); return true; } catch (InvalidOperationException) { return false; } }
        IEnumerable<ModuleDockingNode> FreePorts(Vessel v) => v == null || !v.loaded || v.parts.Any(part => part.FindModuleImplementing<RailgunFlightPod>() != null) ? Enumerable.Empty<ModuleDockingNode>() :
            v.parts.Where(p => p.FindModuleImplementing<RailgunFlightPod>() == null && !StockPods.CargoParts(v).Contains(p))
                .SelectMany(p => p.FindModulesImplementing<ModuleDockingNode>()).Where(n => SupportedPort(n) && PortState(n) == "Ready");
        static string PortState(ModuleDockingNode node) => node?.fsm?.CurrentState?.name ?? node?.state ?? "";
        Vessel SelectedReceiver()
        {
            if (state.SelectedReceiverAnchor == 0) return FlightGlobals.ActiveVessel;
            return FlightGlobals.Vessels.FirstOrDefault(v => v != null && v.state != Vessel.State.DEAD &&
                (v.loaded ? v.parts.Any(p => p.flightID == state.SelectedReceiverAnchor) :
                v.protoVessel?.protoPartSnapshots.Any(p => p.flightID == state.SelectedReceiverAnchor) == true));
        }
        public void SelectDeliveryPort(Part part)
        {
            if (!enabledHere || state == null || faulted) throw new InvalidOperationException("The local reusable-pod sandbox is not ready.");
            if (part == null || part.vessel == null || !part.vessel.loaded) throw new InvalidOperationException("The receiving port must be loaded.");
            var nodes = part.FindModulesImplementing<ModuleDockingNode>();
            if (nodes.Count != 1) throw new InvalidOperationException("This part has multiple docking nodes; exact-node selection is not supported yet.");
            string type = ConnectorType(nodes[0]);
            DockingPortPolicy.ValidateHardware(part.partInfo.name);
            if (!FreePorts(part.vessel).Any(n => n == nodes[0])) throw new InvalidOperationException("That exact port is occupied, closed, or belongs to a delivery pod.");
            state.SelectedReceiverAnchor = part.vessel.rootPart.flightID;
            state.SelectedReceiverPort = selectedPort = part.flightID;
            menuCollapsed = chooseDestination = choosePort = false;
            showcase = false;
            detail = "Delivery target: " + part.vessel.vesselName + " / " + part.partInfo.title + " #" + part.flightID + " / " + DockingPortPolicy.Name(type) + ". Choose cargo and an available pod.";
            error = ""; SaveLedger();
            Debug.Log("[RailgunSandbox] Exact receiving port selected: " + part.flightID + " / " + type);
        }
        Vessel Receiver(PodRecord p) => FlightGlobals.Vessels.FirstOrDefault(v => v != null && v.state != Vessel.State.DEAD &&
            (v.loaded ? v.parts.Any(part => part.flightID == p.TargetAnchor) : v.protoVessel?.protoPartSnapshots.Any(part => part.flightID == p.TargetAnchor) == true));
        ModuleDockingNode TargetPort(PodRecord p, Vessel receiver) => receiver?.parts.FirstOrDefault(part => part.flightID == p.TargetPort)?.FindModuleImplementing<ModuleDockingNode>();
        ModuleDockingNode OwnPort(PodRecord p) {
            var marker = StockPods.Marker(p); if (marker == null) return null;
            // A stock docking operation can reroot the tree. Follow the persisted part ID.
            return marker.vessel.parts.FirstOrDefault(part => part.flightID == p.OwnPort)?.FindModuleImplementing<ModuleDockingNode>();
        }
        void Dispatch(PodRecord p, Vessel target, uint portId, double units)
        {
            if (ShellTestSave && !p.LaunchShellRequired) throw new InvalidOperationException("The shell experiment requires a physically fitted launch shell; a bare pod cannot substitute for this test.");
            if (network != null && !network.CanReserve(p)) throw new InvalidOperationException("Waiting for this pod's server reservation slot.");
            if (faulted) throw new IOException("Fleet storage is unavailable.");
            if (target == null || target.mainBody != home || target.orbit.PeA < home.atmosphereDepth + 3000 ||
                target.situation != Vessel.Situations.ORBITING || target.parts.Any(part => part.FindModuleImplementing<RailgunFlightPod>() != null))
                throw new InvalidOperationException("First flight slice supports independent receivers in stable Kerbin orbit. Interplanetary routes are not implemented yet.");
            var selected = FreePorts(target).FirstOrDefault(n => n.part.flightID == portId) ?? throw new InvalidOperationException("The selected receiving port is no longer free. No replacement port was selected.");
            double room = target.parts.SelectMany(part => part.Resources.Cast<PartResource>()).Where(r => r.resourceName == resource && r.flowState).Sum(r => r.maxAmount - r.amount);
            if (units > room + 1e-6) throw new InvalidOperationException("The receiving ship does not have enough unlocked tank space.");
            var pod = StockPods.Vessel(p);
            if (pod == null || !pod.Landed) throw new InvalidOperationException("The physical pod must be parked at the depot.");
            if (PadDistance(p, pod) > 25) throw new InvalidOperationException("Return the pod to its depot pad before fitting a delivery connector.");
            if (!StockPods.OriginalPartsIntact(p, pod)) throw new InvalidOperationException("Original pod parts are missing; this damaged pod cannot dispatch.");
            // An unloaded client proto can retain the pre-settling construction
            // flag across Luna scene changes. The multiplayer host checks its
            // current native snapshot when admitting Reserve; do not let that
            // stale local flag overrule the server's physical readiness check.
            if (network == null && !StockPods.PlacementSettled(p)) throw new InvalidOperationException("Watch the pod once and let its initial construction placement settle before dispatch.");
            string hardware = DockingHardware.Capture(selected);
            FlightRules.Reserve(state, p, target.rootPart.flightID, selected.part.flightID, target.vesselName, resource, units, StockPods.Capacity(resource), Now, RailLaunchTestSave);
            p.DockingType = ConnectorType(selected);
            p.DockingHardwareConfig = hardware;
            double latitude = state.HomeLatitude * Math.PI / 180;
            p.RailHeading = Math.Asin(Math.Max(-1, Math.Min(1, Math.Cos(target.orbit.inclination * Math.PI / 180) / Math.Cos(latitude)))) * 180 / Math.PI;
            SaveLedger(); follow = true; error = "";
            detail = "Flight assigned. No arrival timer or proximity insertion; native propulsion and orbits determine travel time.";
        }
        void Watch(PodRecord p)
        {
            selectedPod = p.Number;
            var v = StockPods.Vessel(p) ?? throw new InvalidOperationException("The saved physical pod is missing. No replacement was generated.");
            network?.RequestControl(p);
            follow = true;
            if (network != null) network.FollowFlight = true;
            FlightGlobals.SetActiveVessel(v);
        }
        void Update()
        {
            if (!enabledHere || state == null || home == null || faulted) return;
            if (demoDaylight) {
                var surfaceUp = (home.GetWorldSurfacePosition(state.HomeLatitude,state.HomeLongitude,ground)-home.position).normalized;
                double sun = Vector3d.Dot(surfaceUp,(home.orbit.referenceBody.position-home.position).normalized);
                if (network != null || state.Pods.Any(FlightRules.Busy) || sun > .65) {
                    TimeWarp.SetRate(0,true); demoDaylight=false;
                    Debug.Log("[RailgunSandbox] Isolated daylight setup ended at native UT "+Now.ToString("R",CultureInfo.InvariantCulture));
                }
            }
            if (network != null) {
                network.Pump(ref state);
                foreach (var pod in state.Pods.Where(p => !network.LeaseValid(p.Number))) {
                    if (!guidedByUs.Remove(pod.VesselId)) continue;
                    var core = Core(pod);
                    if (core != null) {
                        if (network.NativeOwner(pod, false)) StopFlight(core);
                        else {
                            RecoveryGuidance.Remove(core.landing);
                            core.GetComputerModule<MechJebModuleAscentAutopilot>().users.Remove(this);
                            core.GetComputerModule<MechJebModuleRendezvousAutopilot>().users.Remove(this);
                            core.GetComputerModule<MechJebModuleDockingAutopilot>().users.Remove(this);
                            core.landing.users.Remove(this); core.node.users.Remove(this); core.attitude.users.Remove(this); core.rcs.users.Remove(this);
                        }
                    }
                    armed.Remove(pod.VesselId);
                }
                if (!network.Ready) {
                    detail = network.Status;
                    if (Time.realtimeSinceStartup >= nextStatus) { nextStatus = Time.realtimeSinceStartup + 2; try { Status(); } catch (Exception ex) { Fail(ex); } }
                    return;
                }
            }
            if (!state.Pods.Any(FlightRules.Busy) && FlightGlobals.ActiveVessel?.mainBody == home) ResolveGround();
            model?.Place(home, state.HomeLatitude, state.HomeLongitude, ground);
            var guided = state.Pods.FirstOrDefault(p => p.Phase == FlightPhase.Reserved || p.Phase == FlightPhase.RailLaunch);
            if (guided != null && guided.CradleHeight > 0) model.ConfigureRail(guided.CradleHeight, guided.RailHeading);
            if (guided != null && guided.CradleHeight > 0) {
                var pose = guided.Phase == FlightPhase.RailLaunch ? LoadingPose.At(guided.LaunchShellRequired ? LoadingSequence.ShellDuration : LoadingSequence.Duration, guided.LaunchShellRequired) : DisplayLoading(guided);
                model.Loader.Update(StockPods.Vessel(guided), pose, guided.CradleHeight, guided.Phase == FlightPhase.Reserved, Time.deltaTime, guided.LaunchShellRequired ? ShellRoute(guided) : null);
                model.RailArt.SetPower(pose,guided.Phase==FlightPhase.RailLaunch);
            } else {
                model?.RailArt.SetPower(null,false);
                var parked=state.Pods.Where(p=>p.Phase==FlightPhase.Ready).Select(StockPods.Vessel).FirstOrDefault(v=>v!=null && v.loaded && !v.packed && v.Landed);
                if(parked!=null) {
                    double idleHeight=Math.Max(RootHeight(parked),parked.altitude-ground+2);
                    model.ConfigureRail(idleHeight,90);
                    model.Loader.Update(parked,LoadingPose.At(-1,ShellTestSave),idleHeight,false,Time.deltaTime);
                } else model?.Loader.SetIdle();
            }
            if (state.Pods.Any(FlightRules.Busy) && TimeWarp.CurrentRate != 1) TimeWarp.SetRate(0, true);
            if (Time.realtimeSinceStartup >= nextStatus) {
                nextStatus = Time.realtimeSinceStartup + 2;
                try { Command(); if (!enabledHere) return; Status(); } catch (Exception ex) { Fail(ex); }
            }
            if (Time.realtimeSinceStartup < nextTick || !FlightGlobals.ready || FlightDriver.Pause) return;
            nextTick = Time.realtimeSinceStartup + .25;
            try {
                if (network != null && network.SetupRequested) {
                    if (network.ConstructionAccepted) {
                        network.SetupRequested = false;
                        var unsettled = state.Pods.FirstOrDefault(p => !StockPods.PlacementSettled(p));
                        if (unsettled != null) Watch(unsettled);
                    }
                    else if (state.Pods.Any(p => !p.Created)) Prepare();
                }
                foreach (var p in state.Pods) {
                    if (network != null && !network.CanAct(p)) continue;
                    if (!p.Created) continue;
                    var v = StockPods.Vessel(p);
                    if (v == null || v.state == Vessel.State.DEAD) {
                        if (p.Phase != FlightPhase.Lost) { p.Detail = "Physical pod missing/destroyed; slot remains unavailable"; Change(p, FlightPhase.Lost); }
                    }
                    else if ((!v.loaded || !v.packed) && p.Phase != FlightPhase.Lost && p.OriginalParts > 0 && !StockPods.OriginalPartsIntact(p, v)) {
                        StopFlight(Core(p)); p.Detail = "Original structural parts destroyed; slot remains unavailable";
                        Change(p, FlightPhase.Lost); PersistGame();
                    }
                    else if (p.Phase == FlightPhase.Ready && string.IsNullOrEmpty(p.OrderId) && v.loaded && v.Landed && PadDistance(p, v) < 25) {
                        SettleInitialConstruction(p, v);
                        bool repaired = false;
                        foreach (var cargo in StockPods.CargoParts(v).SelectMany(part => part.Resources.Cast<PartResource>())) {
                            if (cargo.amount != 0) { cargo.amount = 0; repaired = true; }
                            cargo.flowState = false;
                        }
                        if (repaired) PersistGame();
                    }
                }
                // One foreground autopilot at a time. A second reservation stays occupied on its pad.
                var current = state.Pods.FirstOrDefault(p => FlightRules.Busy(p) && p.Phase != FlightPhase.Reserved) ?? state.Pods.FirstOrDefault(p => p.Phase == FlightPhase.Reserved);
                if (current != null && network != null && current.Phase == FlightPhase.Returning && network.Dispatcher(current.Number) == network.Player &&
                    network.CanTakeControl(current.Number) && !network.LeaseValid(current.Number)) {
                    if (StockPods.Vessel(current) != FlightGlobals.ActiveVessel) Watch(current); else network.RequestControl(current);
                    return;
                }
                if (current != null && network != null && !network.CanAct(current)) return;
                if (current != null && network != null) guidedByUs.Add(current.VesselId);
                if (current != null && (!ReentryTestSave || current.RecoveryTest || current.Phase != FlightPhase.Rendezvous) &&
                    (!RailLaunchTestSave || current.Phase != FlightPhase.Rendezvous)) Tick(current);
            } catch (Exception ex) { Fail(ex); }
        }
        MechJebCore Core(PodRecord p) => StockPods.Marker(p)?.part.FindModuleImplementing<MechJebCore>();
        void StopFlight(MechJebCore core)
        {
            if (core == null) return;
            RecoveryGuidance.Remove(core.landing);
            core.GetComputerModule<MechJebModuleAscentAutopilot>().users.Remove(this);
            core.GetComputerModule<MechJebModuleRendezvousAutopilot>().users.Remove(this);
            core.GetComputerModule<MechJebModuleDockingAutopilot>().users.Remove(this);
            core.landing.StopLanding(); core.node.Abort(); core.thrust.ThrustOff();
            core.attitude.users.Remove(this);
        }
        void RetractLegs(PodRecord record, Vessel vessel)
        {
            vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, false);
            foreach (var gear in vessel.parts.Where(part => record.PartIds.Contains(part.flightID)).SelectMany(part => part.FindModulesImplementing<ModuleWheels.ModuleWheelDeployment>())) {
                if (gear.fsm == null) throw new InvalidOperationException("Landing-leg state machine is not ready.");
                if (gear.fsm.CurrentState == gear.st_deployed || gear.fsm.CurrentState == gear.st_deploying) gear.EventToggle();
            }
        }
        int ApplyLandingEngineCompatibility(PodRecord record, Vessel vessel)
        {
            int changed = 0;
            foreach (var module in vessel.parts.Where(part => record.PartIds.Contains(part.flightID)).SelectMany(part => part.Modules.Cast<PartModule>())
                .Where(module => module.moduleName == "PersistentEngine")) {
                // PersistentThrust 1x gating follows maneuver nodes, while landing guidance steers dynamically.
                // Keep its native resource/engine simulation, but let this owned pod's guidance aim its burns.
                var field = module.GetType().GetField("maneuverToleranceInDegree");
                if (field == null || field.FieldType != typeof(float)) throw new InvalidOperationException("Unsupported PersistentEngine landing compatibility field.");
                field.SetValue(module, 180f); changed++;
            }
            Debug.Log("[RailgunSandbox] Landing compatibility configured on " + changed + " owned engine module(s); no resource or force override.");
            return changed;
        }
        bool Once(PodRecord p) {
            string phase = p.Phase.ToString(); if (armed.TryGetValue(p.VesselId, out string previous) && previous == phase) return false;
            armed[p.VesselId] = phase; return true;
        }
        void Tick(PodRecord p)
        {
            var v = StockPods.Vessel(p); if (v == null) return;
            if (p.Phase == FlightPhase.Reserved && DockingHardware.NeedsRefit(p, v)) {
                if (v.loaded) {
                    StopFlight(Core(p));
                    var receiver = Receiver(p) ?? throw new InvalidOperationException("Receiver missing; connector fitting held.");
                    p.Detail = "Fitting the matching physical docking connector at the depot";
                    if (FlightGlobals.ActiveVessel != receiver) FlightGlobals.SetActiveVessel(receiver);
                    return; // Let native vessel unloading complete before changing its connector snapshot.
                }
                DockingHardware.Refit(p, v); PersistGame(); follow = true;
            }
            if (follow && v != FlightGlobals.ActiveVessel && p.Phase != FlightPhase.Unloading && p.Phase != FlightPhase.Undocking) { FlightGlobals.SetActiveVessel(v); return; }
            if (!v.loaded || v.packed || v.rootPart?.rb == null) { p.Detail = "Waiting for native pod physics; travel is not credited as delivery"; return; }
            var marker = StockPods.Marker(p); var core = Core(p);
            if (marker == null || core == null) throw new InvalidOperationException("Pod guidance/MechJeb is not ready.");
            core.node.autowarp = false;
            if (FlightRules.RequiresMainPropellant(p.Phase) && (p.Phase != FlightPhase.Ascent || !p.RailOnlyAscent || p.CircularizationStarted) && !v.Landed) {
                var propellant = v.parts.Where(part => p.PartIds.Contains(part.flightID) && !StockPods.CargoParts(v).Contains(part))
                    .SelectMany(part => part.Resources.Cast<PartResource>()).Where(r => r.flowState).ToArray();
                if (!FlightRules.HasMainPropellant(propellant.Where(r => r.resourceName == "LiquidFuel").Sum(r => r.amount),
                    propellant.Where(r => r.resourceName == "Oxidizer").Sum(r => r.amount))) {
                    StopFlight(core); v.ctrlState.mainThrottle = 0;
                    if (p.Phase == FlightPhase.Landing && FlightRules.CanSettleWithoutFuel(v.mainBody == home, v.Splashed,
                        Math.Min(core.vesselState.altitudeTrue, core.vesselState.altitudeBottom), PadDistance(p, v), v.srfSpeed, UprightAngle(v))) {
                        p.Detail = "Fuel exhausted at home pad; waiting for actual touchdown and stability";
                        return; // No landing/refit credit until native contact and the full stability interval.
                    }
                    p.Detail = "Propulsion exhausted; pod unavailable. Delivery cargo remains isolated.";
                    Change(p, FlightPhase.Stranded); PersistGame(); return;
                }
            }
            if (v != FlightGlobals.ActiveVessel && p.Phase != FlightPhase.Unloading && p.Phase != FlightPhase.Undocking) { p.Detail = "Select Watch pod for native flight control"; return; }
            var target = Receiver(p); var own = OwnPort(p);
            if (target == null && p.Phase <= FlightPhase.Docking && p.Phase >= FlightPhase.Reserved) {
                core.node.Abort(); core.thrust.ThrustOff(); p.Detail = "Receiver missing; holding actual trajectory"; return;
            }
            switch (p.Phase) {
                case FlightPhase.Reserved:
                    DockingHardware.ValidateLoaded(p, v);
                    if (Once(p)) {
                        StopFlight(core);
                        v.SetReferenceTransform(marker.part); var cargo = StockPods.CargoTank(v, p.Resource);
                        if (cargo == null || cargo.maxAmount < p.CargoUnits) throw new InvalidOperationException("Stock cargo capacity changed.");
                        foreach (var tank in StockPods.CargoParts(v).SelectMany(part => part.Resources.Cast<PartResource>())) { tank.amount = 0; tank.flowState = false; }
                        cargo.amount = p.CargoUnits; cargo.flowState = false; p.CargoPart = cargo.part.flightID;
                        foreach (var engine in v.parts.SelectMany(part => part.FindModulesImplementing<ModuleEngines>())) engine.Shutdown();
                        if (!PortSelectionTestSave) RetractLegs(p, v);
                        v.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                        // Native ground placement accounts for the loaded landing-strut geometry.
                        // Lift above that measured clearance instead of forcing the rocket down to guessed bounds.
                        p.CradleHeight = Math.Max(RootHeight(v), v.altitude - ground + 2);
                        PersistGame();
                    }
                    if (PortSelectionTestSave) { p.Detail = "Port-selection fixture: physical connector fitted; launch intentionally disabled"; return; }
                    RailFrame(p, out var loadingOrigin, out var loadingAxis, out var loadingUp);
                    var loadingOffset = v.GetWorldPos3D() - loadingOrigin;
                    double loadingAlong = Vector3d.Dot(loadingOffset, loadingAxis);
                    double loadingLateral = (loadingOffset - loadingAxis * loadingAlong).magnitude;
                    double loadingAngle = Vector3d.Angle(v.transform.up, loadingAxis);
                    bool seated = !p.LaunchShellRequired || ShellRoute(p).Seated;
                    var loadingPose = LoadingFor(p).Update(p.OrderId, Now, seated && RailLaunch.Captured(loadingAlong, loadingLateral, v.srfSpeed, loadingAngle));
                    network?.PublishLoadingView(p,loadingPose,Now);
                    p.Detail = FormattableString.Invariant($"{loadingPose.Description}: along {loadingAlong:F2} m / lateral {loadingLateral:F2} m / speed {v.srfSpeed:F2} m/s / angle {loadingAngle:F2} deg");
                    if (!seated) p.Detail = ShellRoute(p).Description;
                    if (holdLoadingDemo && loadingPose.LaunchClear) p.Detail = "Isolated demo hold: loading hardware clear; launch not released";
                    LoadingSample(p, v, loadingPose, loadingAlong, loadingLateral, loadingAngle);
                    if (loadingPose.LaunchClear && !holdLoadingDemo) {
                        LaunchShell.ValidateClosed(p, v);
                        model.Loader.Update(v, loadingPose, p.CradleHeight, true, 0, p.LaunchShellRequired ? ShellRoute(p) : null);
                        Change(p, FlightPhase.RailLaunch);
                    }
                    break;
                case FlightPhase.RailLaunch:
                    RailFrame(p, out var railOrigin, out var railAxis, out var railUp);
                    double alongRail = Vector3d.Dot(v.GetWorldPos3D() - railOrigin, railAxis);
                    p.Detail = p.RailOnlyAscent ? "Electromagnetic rail acceleration; pod engines and RCS OFF" : "Rail launch assist; powered ascent follows muzzle release";
                    if (alongRail >= (p.RailOnlyAscent ? RailLaunch.Length : 108)) {
                        ShutdownLaunchPropulsion(v, core); Change(p, FlightPhase.Ascent); PersistGame();
                    } else if (p.RailOnlyAscent && !RailLaunch.OnGuide(alongRail, (v.GetWorldPos3D() - railOrigin - railAxis * alongRail).magnitude)) {
                        LaunchFailure(p, core, "Pod departed the physical launch guide before the muzzle");
                    }
                    break;
                case FlightPhase.Ascent:
                    if (p.RailOnlyAscent) CoastAndCircularize(p, v, core); else PoweredAscent(p, v, marker, target, core);
                    break;
                case FlightPhase.Rendezvous:
                    if (target.mainBody != v.mainBody) { p.Detail = "Receiver changed body; interplanetary replanning is unsupported"; return; }
                    var rendezvous = core.GetComputerModule<MechJebModuleRendezvousAutopilot>();
                    if (Once(p)) { core.target.Set(target); rendezvous.desiredDistance.val = 100; rendezvous.maxClosingSpeed.val = 10; rendezvous.users.Add(this); }
                    p.Detail = "Actual orbit rendezvous: " + rendezvous.status;
                    if (target.loaded && (v.GetWorldPos3D() - target.GetWorldPos3D()).magnitude < 160 && (v.obt_velocity - target.obt_velocity).magnitude < 1.2) {
                        rendezvous.users.Remove(this); core.node.Abort(); Change(p, FlightPhase.Docking);
                    }
                    break;
                case FlightPhase.Docking:
                    var port = TargetPort(p, target);
                    if (own == null || port == null) { p.Detail = "Waiting for matching docking ports to load"; return; }
                    if (Once(p)) { own.MakeReferenceTransform(); core.target.Set(port); core.GetComputerModule<MechJebModuleDockingAutopilot>().users.Add(this); }
                    var other = own.otherNode;
                    if (FlightRules.DockingEvidence(PortState(own).StartsWith("Docked"), PortState(other).StartsWith("Docked"),
                        other?.part.flightID ?? 0, p.TargetPort, other?.vessel == v, v.parts.Any(part => part.flightID == p.TargetAnchor))) {
                        core.GetComputerModule<MechJebModuleDockingAutopilot>().users.Remove(this); core.thrust.ThrustOff();
                        Change(p, FlightPhase.Unloading); PersistGame();
                    } else p.Detail = "Native docking: " + core.GetComputerModule<MechJebModuleDockingAutopilot>().status;
                    break;
                case FlightPhase.Unloading:
                    Transfer(p, v, own);
                    if (p.CargoTransferred && Now - p.PhaseStarted >= 3) { own.Undock(); Change(p, FlightPhase.Undocking); PersistGame(); }
                    break;
                case FlightPhase.Undocking:
                    p.Detail = "Waiting for the stock docking joint to separate";
                    if (own != null && own.otherNode == null && !v.parts.Any(part => part.flightID == p.TargetAnchor)) {
                        v.SetReferenceTransform(marker.part); Change(p, FlightPhase.Returning); PersistGame();
                    }
                    break;
                case FlightPhase.Returning:
                    if (v.mainBody != home) { p.Detail = "Return route from this body is not implemented; pod remains occupied"; return; }
                    if (target != null && (v.GetWorldPos3D() - target.GetWorldPos3D()).magnitude < 50) {
                        v.ActionGroups.SetGroup(KSPActionGroup.RCS, true); core.rcs.users.Add(this);
                        Vector3d away = (v.GetWorldPos3D() - target.GetWorldPos3D()).normalized;
                        core.rcs.SetTargetWorldVelocity(target.obt_velocity + away * .8);
                        p.Detail = "RCS departure to clear the receiving ship"; return;
                    }
                    core.rcs.users.Remove(this); Change(p, FlightPhase.Landing);
                    break;
                case FlightPhase.Landing:
                    if (v.Splashed) {
                        StopFlight(core); p.Detail = "Splashdown away from recovery pad; pod unavailable and no refit credited";
                        Change(p, FlightPhase.Stranded); PersistGame(); break;
                    }
                    if (Once(p)) {
                        p.StableSince = -1; // A fresh scene/control handoff must establish a new continuous touchdown interval.
                        Pad(p, out double lat, out double lon); v.SetReferenceTransform(marker.part);
                        ApplyLandingEngineCompatibility(p, v);
                        if (v.altitude - ground > 1500) RetractLegs(p, v);
                        RecoveryGuidance.Register(core, home, this, lat, lon, ReentryTestSave && Environment.GetCommandLineArgs().Contains("-railgun-pad-guidance"));
                        foreach (var engine in v.parts.Where(part => p.PartIds.Contains(part.flightID)).SelectMany(part => part.FindModulesImplementing<ModuleEngines>())) engine.Activate();
                        core.target.SetPositionTarget(home, lat, lon); core.landing.deployChutes = false;
                        core.landing.deployGears = true; core.landing.limitGearsStage.val = -1; core.landing.touchdownSpeed.val = .4;
                        core.landing.LandAtPositionTarget(this);
                    }
                    string terminalStatus = RecoveryGuidance.TerminalStatus(core.landing);
                    p.Detail = terminalStatus.StartsWith("centering /", StringComparison.Ordinal) ? "Home-pad approach: " + terminalStatus : "Powered return to home pad: " + core.landing.status;
                    if (FlightRules.StableLanding(p, FlightRules.LandingEvidence(v.Landed, v.mainBody == home, 0, v.srfSpeed,
                        v.parts.Count(part => p.PartIds.Contains(part.flightID)), p.OriginalParts), Now)) {
                        int intact = v.parts.Count(part => p.PartIds.Contains(part.flightID));
                        double distance = PadDistance(p, v), tilt = UprightAngle(v), stable = Now - p.StableSince;
                        bool accepted = FlightRules.LandingEvidence(v.Landed, v.mainBody == home, distance, v.srfSpeed, intact, p.OriginalParts, tilt);
                        string evidencePath = Path.Combine(folder, "LandingEvidence.tsv");
                        if (!File.Exists(evidencePath)) File.WriteAllText(evidencePath, "ut\tpod\trunId\trecoveryTest\taccepted\tstableSeconds\tpadMetres\tuprightDegrees\tsurfaceSpeed\tintact\toriginal\tnativeLanded\twarp\tphysicsDt\n");
                        File.AppendAllText(evidencePath, FormattableString.Invariant($"{Now:R}\t{p.VesselId}\t{p.RecoveryTestRunId}\t{p.RecoveryTest}\t{accepted}\t{stable:R}\t{distance:R}\t{tilt:R}\t{v.srfSpeed:R}\t{intact}\t{p.OriginalParts}\t{v.Landed}\t{TimeWarp.CurrentRate:R}\t{TimeWarp.fixedDeltaTime:R}\n"));
                        core.landing.StopLanding(); v.ctrlState.mainThrottle = 0;
                        RecoveryGuidance.Remove(core.landing);
                        if (!accepted) {
                            p.Detail = FormattableString.Invariant($"Landing outside recovery limits: {PadDistance(p,v):F1} m from pad, {UprightAngle(v):F1} degrees tilt; no refit credited");
                            Change(p, FlightPhase.Stranded);
                        } else if (p.RecoveryTest) {
                            p.Detail = "Return-only test landed intact; no cargo delivery or refit credited";
                            Change(p, FlightPhase.RecoveryTestComplete);
                        } else Change(p, FlightPhase.Refitting);
                        PersistGame();
                    }
                    break;
                case FlightPhase.Refitting:
                    if (!FlightRules.LandingEvidence(v.Landed, v.mainBody == home, PadDistance(p, v), v.srfSpeed,
                        v.parts.Count(part => p.PartIds.Contains(part.flightID)), p.OriginalParts, UprightAngle(v))) {
                        p.Detail = "Refit stopped: craft must remain intact and settled on its home pad"; return;
                    }
                    if (Now - p.PhaseStarted < 10) { p.Detail = "Home depot inspection and refuelling"; return; }
                    foreach (var part in v.parts.Where(part => p.PartIds.Contains(part.flightID) && !StockPods.CargoParts(v).Contains(part)))
                        foreach (PartResource tank in part.Resources) if (tank.resourceName == "LiquidFuel" || tank.resourceName == "Oxidizer" || tank.resourceName == "MonoPropellant" || tank.resourceName == "ElectricCharge") tank.amount = tank.maxAmount;
                    foreach (var cargo in StockPods.CargoParts(v).SelectMany(part => part.Resources.Cast<PartResource>())) { cargo.amount = 0; cargo.flowState = false; }
                    Change(p, FlightPhase.Ready); PersistGame();
                    break;
            }
        }
        static double RootHeight(Vessel v) => -v.parts.Min(part => v.transform.InverseTransformPoint(part.transform.position).y) + 2;
        LoadingSequence LoadingFor(PodRecord p) {
            if (!loadingSequences.TryGetValue(p.Number, out var sequence)) loadingSequences[p.Number] = sequence = new LoadingSequence(p.LaunchShellRequired);
            return sequence;
        }
        LoadingPose DisplayLoading(PodRecord p)=>network!=null&&!network.CanAct(p)?network.ObservedLoading(p,Now):LoadingFor(p).Pose;
        void LoadingSample(PodRecord p, Vessel v, LoadingPose pose, double along, double lateral, double angle) {
            if (p.LaunchShellRequired) {
                string shellPath=Path.Combine(folder,"ShellLoading-v2.tsv");
                if(!File.Exists(shellPath))File.WriteAllText(shellPath,"ut\ttransfer\tstage\telapsed\tgrip\tcarrierGrip\tretraction\tpositioned\tseated\tx\ty\tz\ttargetX\ttargetY\ttargetZ\tspeed\tangle\tclamps\tpins\tcharge\tlaunchClear\tparts\n");
                var route=ShellRoute(p);var local=model.Root.transform.InverseTransformPoint((Vector3)v.GetWorldPos3D());
                File.AppendAllText(shellPath,FormattableString.Invariant($"{Now:R}\t{route.Stage}\t{pose.Stage}\t{pose.Elapsed:R}\t{route.Grip:R}\t{pose.CarrierGrip:R}\t{pose.CarrierRetraction:R}\t{model.Loader.CarrierPositioned}\t{route.Seated}\t{local.x:R}\t{local.y:R}\t{local.z:R}\t{route.X:R}\t{route.Y:R}\t{route.Z:R}\t{v.srfSpeed:R}\t{angle:R}\t{pose.Clamps:R}\t{pose.Pins:R}\t{pose.Charge:R}\t{pose.LaunchClear}\t{v.parts.Count}\n"));
            }
            string path = Path.Combine(folder, "LoadingMechanism-v1.tsv");
            if (!File.Exists(path)) File.WriteAllText(path, "ut\tpod\tphase\tstage\telapsed\talong\tlateral\tspeed\tangle\tclamps\tpins\tcharge\tlaunchClear\tparts\tthrottle\tliquidFuel\toxidizer\n");
            var resources = v.parts.SelectMany(part => part.Resources.Cast<PartResource>()).ToArray();
            File.AppendAllText(path, FormattableString.Invariant($"{Now:R}\t{p.Number}\t{p.Phase}\t{pose.Stage}\t{pose.Elapsed:R}\t{along:R}\t{lateral:R}\t{v.srfSpeed:R}\t{angle:R}\t{pose.Clamps:R}\t{pose.Pins:R}\t{pose.Charge:R}\t{pose.LaunchClear}\t{v.parts.Count}\t{v.ctrlState.mainThrottle:R}\t{resources.Where(r=>r.resourceName=="LiquidFuel").Sum(r=>r.amount):R}\t{resources.Where(r=>r.resourceName=="Oxidizer").Sum(r=>r.amount):R}\n"));
        }
        static double UprightAngle(Vessel v) => Vector3d.Angle(v.rootPart.transform.up, (v.GetWorldPos3D() - v.mainBody.position).normalized);
        void ResolveGround()
        {
            // KSC's runway collider is above raw PQS terrain. Place the facility on the actual surface.
            var start = home.GetWorldSurfacePosition(state.HomeLatitude, state.HomeLongitude, ground + 250);
            var up = (start - home.position).normalized;
            var hits = Physics.RaycastAll((Vector3)start, (Vector3)(-up), 500, 32768, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.collider.GetComponentInParent<Part>() == null && !hit.collider.transform.IsChildOf(model.Root.transform))
                .OrderBy(hit => hit.distance).ToArray();
            if (hits.Length > 0) ground = ((Vector3d)hits[0].point - home.position).magnitude - home.Radius;
        }
        void SettleInitialConstruction(PodRecord record, Vessel vessel)
        {
            var marker = StockPods.Marker(record);
            if (marker == null || marker.placementSettled || record.FlightsCompleted != 0 || !string.IsNullOrEmpty(record.OrderId) || vessel.packed || vessel.rootPart?.rb == null) return;
            var centre = vessel.GetWorldPos3D(); var up = (centre - home.position).normalized;
            if (!Physics.Raycast((Vector3)(centre + up * 100), (Vector3)(-up), out RaycastHit hit, 300, 32768, QueryTriggerInteraction.Ignore)) return;
            double lowest = double.PositiveInfinity;
            foreach (var part in vessel.parts)
                foreach (var collider in part.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.gameObject.layer != 21)) {
                    var bounds = collider.bounds;
                    for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2) {
                        var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                        lowest = Math.Min(lowest, Vector3d.Dot((Vector3d)corner - centre, up));
                    }
                }
            if (double.IsInfinity(lowest)) return;
            double correction = Vector3d.Dot((Vector3d)hit.point - centre, up) + .08 - lowest;
            if (Math.Abs(correction) > 30) throw new InvalidOperationException("Initial pod placement needs inspection; ground offset exceeds construction limits.");
            // One-time scene authoring for never-launched prototypes. Never called on a mission or return.
            // GetWorldPos3D returns CoMD; SetPosition expects vesselTransform.position.
            // Native Translate applies a delta from that root and avoids a centre-of-mass-sized jump.
            var velocity = vessel.rootPart.rb.velocity;
            vessel.Translate(up * correction); vessel.SetWorldVelocity(velocity);
            vessel.Landed = false; vessel.situation = Vessel.Situations.FLYING;
            marker.placementSettled = true; PersistGame();
            Debug.Log("[RailgunSandbox] Initial construction placement corrected " + record.Number + " by " + correction.ToString("F3") + " m; awaiting native contact.");
        }
        void RailFrame(PodRecord p, out Vector3d origin, out Vector3d axis, out Vector3d up)
        {
            var surface = home.GetWorldSurfacePosition(state.HomeLatitude, state.HomeLongitude, ground);
            up = (surface - home.position).normalized;
            var north = Vector3d.Exclude(up, home.GetWorldSurfacePosition(state.HomeLatitude + .01, state.HomeLongitude, ground) - surface).normalized;
            var east = Vector3d.Cross(up, north).normalized;
            double heading = p.RailHeading * Math.PI / 180, pitch = (p.RailOnlyAscent ? RailLaunch.ElevationDegrees : 90) * Math.PI / 180;
            axis = up * Math.Sin(pitch) + (north * Math.Cos(heading) + east * Math.Sin(heading)) * Math.Cos(pitch);
            origin = surface + up * p.CradleHeight;
        }
        void ShutdownLaunchPropulsion(Vessel vessel, MechJebCore core)
        {
            core.GetComputerModule<MechJebModuleAscentAutopilot>().users.Remove(this);
            core.thrust.ThrustOff(); vessel.ctrlState.mainThrottle = 0;
            vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, false);
            foreach (var engine in vessel.parts.SelectMany(part => part.FindModulesImplementing<ModuleEngines>()))
                if (engine.EngineIgnited) engine.Shutdown();
        }
        void LaunchFailure(PodRecord p, MechJebCore core, string reason)
        {
            StopFlight(core); p.Detail = reason + "; no rocket-ascent fallback, pod remains unavailable";
            Change(p, FlightPhase.Stranded); PersistGame();
        }
        void PoweredAscent(PodRecord p, Vessel v, RailgunFlightPod marker, Vessel target, MechJebCore core)
        {
            var ap = core.GetComputerModule<MechJebModuleAscentAutopilot>();
            // Guidance can lose its Luna lease after warp or reconnect even
            // though the native circularization already succeeded. Resume from
            // the actual orbit, rather than starting a second ascent program.
            if (!ap.enabled && FlightRules.SafeOrbit(v.orbit.PeA, v.orbit.eccentricity, home.atmosphereDepth)) {
                core.node.Abort(); core.thrust.ThrustOff();
                Change(p, FlightPhase.Rendezvous); PersistGame(); return;
            }
            if (Once(p)) {
                v.SetReferenceTransform(marker.part);
                foreach (var engine in v.parts.SelectMany(part => part.FindModulesImplementing<ModuleEngines>())) engine.Activate();
                var ascent = core.GetComputerModule<MechJebModuleAscentAutopilot>();
                ascent.autostage = false; ascent.timedLaunch = false; ascent.skipCircularization = false;
                ascent.desiredOrbitAltitude.val = Math.Max(home.atmosphereDepth + 15000, Math.Min(target.orbit.PeA, 120000));
                ascent.desiredInclination = target.orbit.inclination; ascent.ascentPathIdxPublic = ascentType.CLASSIC;
                var path = core.GetComputerModule<MechJebModuleAscentClassic>();
                path.autoPath = false; path.turnStartAltitude.val = 500; path.turnStartVelocity.val = 100;
                path.turnEndAltitude.val = 40000; path.turnEndAngle.val = 0; path.turnShapeExponent.val = .4;
                ascent.doWiring(); ascent.users.Add(this);
            }
            p.Detail = "Native powered ascent: " + ap.status;
            if (v.orbit.PeA > home.atmosphereDepth + 3000 && !ap.enabled) Change(p, FlightPhase.Rendezvous);
        }
        void CoastAndCircularize(PodRecord p, Vessel v, MechJebCore core)
        {
            if (p.LaunchShellRequired) {
                RailFrame(p, out var shellOrigin, out var shellAxis, out var shellUp);
                string shellState = $"{p.LaunchShellOpenedAt:R}/{p.LaunchShellClearAt:R}/{p.LaunchShellPanelsReleased}/{p.LaunchShellBaseReleased}";
                bool shellClear = LaunchShell.Update(p, v, Vector3d.Dot(v.GetWorldPos3D() - shellOrigin, shellAxis), Now);
                if (shellState != $"{p.LaunchShellOpenedAt:R}/{p.LaunchShellClearAt:R}/{p.LaunchShellPanelsReleased}/{p.LaunchShellBaseReleased}") SaveLedger();
                if (!shellClear) {
                    ShutdownLaunchPropulsion(v, core);
                    core.attitude.attitudeTo(v.obt_velocity.normalized, AttitudeReference.INERTIAL, this);
                    return;
                }
            }
            bool entering = Once(p);
            if (!core.node.enabled && FlightRules.SafeOrbit(v.orbit.PeA, v.orbit.eccentricity, home.atmosphereDepth)) {
                core.thrust.ThrustOff(); Change(p, FlightPhase.Rendezvous); PersistGame(); return;
            }
            if (v.patchedConicSolver == null) { p.Detail = "Waiting for the native orbital plan"; return; }
            if (p.CircularizationPlanned && !p.CircularizationStarted && v.patchedConicSolver.maneuverNodes.Count == 0) {
                // A scene reload can retain the ledger but lose an unexecuted node.
                // Re-plan from the real current orbit; never insert a velocity.
                p.CircularizationPlanned = false; SaveLedger();
            }
            if (p.CircularizationStarted && !core.node.enabled && v.patchedConicSolver.maneuverNodes.Count == 0) {
                LaunchFailure(p, core, "Circularization stopped without a maneuver or a safe orbit"); return;
            }
            if (entering && p.CircularizationStarted && !core.node.enabled &&
                v.patchedConicSolver.maneuverNodes.Count == 1 && v.altitude > home.atmosphereDepth + 1000) {
                foreach (var engine in v.parts.SelectMany(part => part.FindModulesImplementing<ModuleEngines>())) engine.Activate();
                core.node.ExecuteOneNode(this);
            }
            if (!p.CircularizationStarted) {
                ShutdownLaunchPropulsion(v, core);
                core.attitude.attitudeTo(v.obt_velocity.normalized, AttitudeReference.INERTIAL, this);
            }
            p.Detail = FormattableString.Invariant($"Rail-only coast / engines OFF / Ap {v.orbit.ApA / 1000:F1} km / Pe {v.orbit.PeA / 1000:F1} km");
            if (!p.CircularizationPlanned) {
                if (v.orbit.eccentricity >= 1) { LaunchFailure(p, core, "Rail trajectory is unbound"); return; }
                if (v.altitude <= home.atmosphereDepth + 1000) {
                    if (v.verticalSpeed < -5) LaunchFailure(p, core, "Rail launch failed to clear the atmosphere");
                    return;
                }
                double burnAt = Now + v.orbit.timeToAp;
                if (v.verticalSpeed < 0 || v.orbit.timeToAp <= 0 || v.orbit.ApA < home.atmosphereDepth + 5000) { LaunchFailure(p, core, "Insufficient or missed rail apoapsis"); return; }
                Vector3d dv = OrbitalManeuverCalculator.DeltaVToCircularize(v.orbit, burnAt);
                if (!FlightRules.Finite(dv.magnitude) || dv.magnitude > RailLaunch.MaximumCircularizationDeltaV) {
                    LaunchFailure(p, core, "Required circularization exceeds the 800 m/s launch budget: " + dv.magnitude.ToString("F1")); return;
                }
                if (v.patchedConicSolver == null || v.patchedConicSolver.maneuverNodes.Count != 0)
                    throw new InvalidOperationException("Circularization needs an empty native maneuver plan.");
                v.PlaceManeuverNode(v.orbit, dv, burnAt);
                double thrust = v.parts.SelectMany(part => part.FindModulesImplementing<ModuleEngines>()).Sum(e => e.maxThrust * e.thrustPercentage / 100);
                if (thrust <= 0) { LaunchFailure(p, core, "No usable native circularization engine"); return; }
                // Conservative half-duration estimate; the actual burn remains MJ's native finite burn.
                p.CircularizationDeltaV = dv.magnitude;
                p.CircularizationHalfBurnTime = dv.magnitude * v.GetTotalMass() / thrust / 2;
                p.CircularizationPlanned = true; SaveLedger();
            }
            if (!p.CircularizationStarted && v.patchedConicSolver.maneuverNodes.Count != 1) {
                p.Detail = "Circularization held: expected one native maneuver"; return;
            }
            double untilBurn = !p.CircularizationStarted ? v.patchedConicSolver.maneuverNodes[0].UT - Now : 0;
            if (!p.CircularizationStarted && untilBurn < -10) { LaunchFailure(p, core, "Circularization maneuver window missed"); return; }
            if (!p.CircularizationStarted && RailLaunch.BurnWindow(v.altitude, home.atmosphereDepth, untilBurn, p.CircularizationHalfBurnTime)) {
                core.attitude.users.Remove(this);
                foreach (var engine in v.parts.SelectMany(part => part.FindModulesImplementing<ModuleEngines>())) engine.Activate();
                p.CircularizationStarted = true; core.node.autowarp = false; core.node.ExecuteOneNode(this); SaveLedger();
            }
            if (p.CircularizationStarted) {
                p.Detail = FormattableString.Invariant($"Native apoapsis circularization / planned {p.CircularizationDeltaV:F1} m/s / burnTriggered={core.node.burnTriggered}");
                if (!core.node.enabled && FlightRules.SafeOrbit(v.orbit.PeA, v.orbit.eccentricity, home.atmosphereDepth)) {
                    core.thrust.ThrustOff(); Change(p, FlightPhase.Rendezvous); PersistGame();
                } else if (!core.node.enabled && v.orbit.timeToAp > 60) LaunchFailure(p, core, "Native circularization ended without a safe periapsis");
            }
        }
        void LaunchSample(PodRecord p, Vessel v, double along, double lateral, double guideAcceleration = 0)
        {
            if (Now < nextLaunchSample) return; nextLaunchSample = Now + .1;
            LaunchShell.Sample(p, v, folder, Now);
            string path = Path.Combine(folder, "RailLaunchEvidence-v2.tsv");
            if (!File.Exists(path)) File.WriteAllText(path, "ut\tpod\tphase\taltitude\tsurfaceSpeed\talongRail\tlateral\tcircularizationStarted\tthrustKN\tliquidFuel\toxidizer\trcs\tfixedDt\tparts\theatRatio\thottestPart\tg\tqKPa\tguideAcceleration\n");
            var resources = v.parts.Where(part => !StockPods.CargoParts(v).Contains(part)).SelectMany(part => part.Resources.Cast<PartResource>()).ToArray();
            double thrust = v.parts.SelectMany(part => part.FindModulesImplementing<ModuleEngines>()).Sum(e => e.finalThrust);
            var hottest = v.parts.Select(part => new { Part = part, Heat = Math.Max(part.temperature / part.maxTemp, part.skinTemperature / part.skinMaxTemp) }).OrderByDescending(x => x.Heat).First();
            File.AppendAllText(path, FormattableString.Invariant($"{Now:R}\t{p.Number}\t{p.Phase}\t{v.altitude:R}\t{v.srfSpeed:R}\t{along:R}\t{lateral:R}\t{p.CircularizationStarted}\t{thrust:R}\t{resources.Where(r => r.resourceName == "LiquidFuel").Sum(r => r.amount):R}\t{resources.Where(r => r.resourceName == "Oxidizer").Sum(r => r.amount):R}\t{v.ActionGroups[KSPActionGroup.RCS]}\t{TimeWarp.fixedDeltaTime:R}\t{v.parts.Count}\t{hottest.Heat:R}\t{hottest.Part.partInfo.name}\t{v.geeForce_immediate:R}\t{v.dynamicPressurekPa:R}\t{guideAcceleration:R}\n"));
        }
        void FixedUpdate()
        {
            if (!enabledHere || faulted || state == null || home == null || !FlightGlobals.ready || TimeWarp.CurrentRate != 1 || PortSelectionTestSave) return;
            var current = state.Pods.FirstOrDefault(p => FlightRules.Busy(p) && p.Phase != FlightPhase.Reserved) ?? state.Pods.FirstOrDefault(p => p.Phase == FlightPhase.Reserved);
            if (current == null || current.Phase != FlightPhase.Reserved && current.Phase != FlightPhase.RailLaunch && current.Phase != FlightPhase.Ascent) return;
            if (network != null && !network.CanAct(current)) return;
            var v = StockPods.Vessel(current); if (v == null || !v.loaded || v.packed || v != FlightGlobals.ActiveVessel || v.rootPart?.rb == null) return;
            if (RailLaunch.EnginesLocked(current)) ShutdownLaunchPropulsion(v, Core(current));
            RailFrame(current, out var origin, out var axis, out var up);
            var position = v.GetWorldPos3D(); var relative = position - origin;
            double along = Vector3d.Dot(relative, axis);
            var railLateral = relative - axis * along;
            if (current.Phase == FlightPhase.Ascent) { LaunchSample(current, v, along, railLateral.magnitude); return; }
            if (current.CradleHeight <= 0) { LaunchSample(current, v, along, railLateral.magnitude); return; }
            Vector3d velocity = v.srf_velocity;
            Vector3d acceleration;
            if (current.Phase == FlightPhase.Reserved) {
                // A powered depot crane loads the rocket; continuously applied force, no SetPosition shortcut.
                Vector3d craneError = -relative;
                if (current.LaunchShellRequired) {
                    var local = model.Root.transform.InverseTransformPoint((Vector3)position);
                    var route = ShellRoute(current);
                    route.Update(current.OrderId, Now, local.x, local.y, local.z, current.CradleHeight, v.srfSpeed, Vector3d.Angle(v.transform.up, axis), model.Loader.CarrierPositioned);
                    craneError = (Vector3d)model.Root.transform.TransformPoint(new Vector3((float)route.X, (float)route.Y, (float)route.Z)) - position;
                }
                Vector3d desiredVelocity = Clamp(craneError * .35, 2);
                acceleration = Clamp((desiredVelocity - velocity) * 2, 12) + up * home.gravParameter / Math.Pow(home.Radius + v.altitude, 2);
            } else {
                if (along >= (current.RailOnlyAscent ? RailLaunch.Length : 108)) { LaunchSample(current, v, along, railLateral.magnitude); return; }
                if (!(current.RailOnlyAscent ? RailLaunch.OnGuide(along, railLateral.magnitude) : along >= -2 && railLateral.magnitude <= 2)) { current.Detail = "Rail departure outside guide; no launch force applied"; LaunchSample(current, v, along, railLateral.magnitude); return; }
                double gravity = home.gravParameter / Math.Pow(home.Radius + v.altitude, 2);
                double gravityAlong = gravity * Vector3d.Dot(up, axis);
                acceleration = axis * (current.RailOnlyAscent ? RailLaunch.DriveAcceleration(Vector3d.Dot(velocity, axis), Time.fixedDeltaTime, gravityAlong) : 50) +
                    up * gravity - axis * gravityAlong - Clamp(railLateral * 8 + Vector3d.Exclude(axis, velocity) * 4, 80);
            }
            Vector3 angularAcceleration = Vector3.zero;
            Vector3 angularVelocity = v.rootPart.rb.angularVelocity;
            bool shellCrane = current.Phase == FlightPhase.Reserved && current.LaunchShellRequired;
            if (shellCrane) {
                Vector3 desiredAngularVelocity=Vector3.zero;
                if(ShellRoute(current).AlignToRail) {
                    var turn=Vector3.Cross(v.transform.up,(Vector3)axis);
                    float radians=Vector3.Angle(v.transform.up,(Vector3)axis)*Mathf.Deg2Rad;
                    if(turn.sqrMagnitude>.000001f) desiredAngularVelocity=turn.normalized*Math.Min(.18f,radians*.8f);
                }
                angularAcceleration=Vector3.ClampMagnitude((desiredAngularVelocity-angularVelocity)*2,.12f);
            } else {
                var orientation = Quaternion.FromToRotation(v.transform.up, (Vector3)axis) * v.transform.rotation;
                v.SetRotation(Quaternion.RotateTowards(v.transform.rotation, orientation, 60 * Time.fixedDeltaTime));
            }
            foreach (var body in v.parts.Select(part => part.rb).Where(body => body != null).Distinct()) {
                body.AddForce((Vector3)acceleration, ForceMode.Acceleration);
                if(shellCrane) {
                    // A distributed crane force couple rotates the assembly around
                    // its measured CoM. SetRotation rotates about the upper probe
                    // and displaced the casing by five metres in the first take.
                    var radius=body.worldCenterOfMass-(Vector3)position;
                    body.AddForce(Vector3.Cross(angularAcceleration,radius)+Vector3.Cross(angularVelocity,Vector3.Cross(angularVelocity,radius)),ForceMode.Acceleration);
                    body.AddTorque(angularAcceleration,ForceMode.Acceleration);
                }
            }
            LaunchSample(current, v, along, railLateral.magnitude, acceleration.magnitude);
        }
        void Transfer(PodRecord p, Vessel v, ModuleDockingNode own)
        {
            var marker = StockPods.Marker(p);
            if (p.CargoTransferred) {
                if (marker.completedOrder != p.OrderId) throw new IOException("Fleet receipt and vessel save disagree. Restore a matching checkpoint before departure.");
                return;
            }
            // A receipt in the same KSP snapshot as the drained tank recovers a crash before sidecar commit.
            if (marker.completedOrder == p.OrderId) { p.CargoTransferred = true; SaveLedger(); return; }
            var other = own?.otherNode;
            if (!FlightRules.DockingEvidence(PortState(own).StartsWith("Docked"), PortState(other).StartsWith("Docked"), other?.part.flightID ?? 0,
                p.TargetPort, other?.vessel == v, v.parts.Any(part => part.flightID == p.TargetAnchor))) throw new InvalidOperationException("Transfer requires a confirmed physical dock to the assigned port.");
            var source = v.parts.FirstOrDefault(part => part.flightID == p.CargoPart)?.Resources[p.Resource];
            if (source == null || source.amount + 1e-6 < p.CargoUnits) throw new InvalidOperationException("Cargo tank no longer contains the full manifest.");
            var podParts = new HashSet<uint>(p.PartIds);
            var tanks = v.parts.Where(part => !podParts.Contains(part.flightID)).SelectMany(part => part.Resources.Cast<PartResource>()).Where(r => r.resourceName == p.Resource && r.flowState).ToList();
            var allocation = FlightRules.CargoAllocation(p.CargoUnits, source.amount, tanks.Select(r => r.amount).ToArray(), tanks.Select(r => r.maxAmount).ToArray());
            if (allocation == null) { p.Detail = "Docked; waiting for enough unlocked receiver capacity"; return; }
            for (int i = 0; i < tanks.Count; i++) tanks[i].amount += allocation[i];
            source.amount -= p.CargoUnits; p.CargoTransferred = true; p.Detail = "Docked, cargo transferred from its real locked tank";
            // Vessel resources and a matching receipt must reach the same save before autonomous undocking.
            marker.completedOrder = p.OrderId;
            PersistGame(); Debug.Log("[RailgunSandbox] Cargo conserved: " + p.CargoUnits + " " + p.Resource + " pod " + p.Number + " / " + p.OrderId);
        }
        static Vector3d Clamp(Vector3d vector, double magnitude) => vector.magnitude > magnitude ? vector.normalized * magnitude : vector;
        void InspectionTarget(Transform reference,Vector3 anchor)
        {
            showcaseReference=reference;
            // KSP reparents its persistent camera pivot to a Transform target.
            // Keep this object outside the depot's distance-culling hierarchy.
            if(showcasePivot==null){showcasePivot=new GameObject("KRL independent inspection target");showcasePivot.AddComponent<InspectionTargetFollower>();UnityEngine.Object.DontDestroyOnLoad(showcasePivot);}
            var follower=showcasePivot.GetComponent<InspectionTargetFollower>();follower.Reference=reference;follower.LocalPoint=anchor;
            showcasePivot.transform.position=reference.TransformPoint(anchor);
            FlightCamera.SetTarget(showcasePivot.transform);
        }
        void ReleaseInspectionTarget()
        {
            showcaseReference=null;
            if(showcasePivot!=null)showcasePivot.GetComponent<InspectionTargetFollower>().Reference=null;
            var camera=FlightCamera.fetch;
            if(showcasePivot!=null && camera!=null && camera.GetPivot()!=null && camera.GetPivot().IsChildOf(showcasePivot.transform)) {
                if(FlightGlobals.ActiveVessel!=null&&FlightGlobals.ActiveVessel.vesselTransform!=null)FlightCamera.SetTarget(FlightGlobals.ActiveVessel);else camera.SetTargetNone();
            }
        }
        void ShowcaseShot(string shot)
        {
            showcase = shot != "ui";
            ReleaseInspectionTarget();
            if (!showcase) {
                KSP.UI.UIMasterController.Instance.ShowUI(); GameEvents.onShowUI.Fire();
                FlightCamera.SetTarget(FlightGlobals.ActiveVessel); return;
            }
            KSP.UI.UIMasterController.Instance.HideUI(); GameEvents.onHideUI.Fire();
            FlightCamera.SetModeImmediate(shot == "orbit" ? FlightCamera.Modes.ORBITAL : FlightCamera.Modes.FREE);
            if (shot == "wide" || shot == "loading" || shot == "locks") {
                var loadingPod = state.Pods.FirstOrDefault(p => p.Phase == FlightPhase.Reserved || p.Phase == FlightPhase.RailLaunch);
                float height = loadingPod?.CradleHeight > 0 ? (float)loadingPod.CradleHeight : 28;
                var visiblePod = loadingPod == null ? null : StockPods.Vessel(loadingPod);
                if (visiblePod != null && visiblePod.loaded) height += Vector3.Dot(visiblePod.rootPart.transform.position-(Vector3)visiblePod.GetWorldPos3D(),model.Root.transform.up);
                InspectionTarget(model.Root.transform,shot == "locks" ? new Vector3(0,height-12,0) : new Vector3(0,shot=="loading"?32:45,0));
            } else if (shot == "coupler" && PortSelectionTestSave) {
                var own = OwnPort(state.Pods[0]) ?? throw new InvalidOperationException("Load the test pod connector first.");
                FlightCamera.SetTarget(own.part.transform);
            } else FlightCamera.SetTarget(FlightGlobals.ActiveVessel);
            var camera = FlightCamera.fetch;
            camera.camHdg = shot == "orbit" ? 1.3f : shot=="locks"?2.5f:2.2f;
            camera.camPitch = shot == "wide" ? .2f : shot == "orbit" ? .35f : shot=="locks"?.04f:.12f;
            camera.SetDistanceImmediate(shot == "coupler" && PortSelectionTestSave ? 6 : shot=="locks"?24:shot=="loading"?108:shot == "wide" ? 180 : shot == "pod" ? 45 : 65);
            camera.FieldOfView = 60;
            Debug.Log("[RailgunSandbox] Showcase camera " + shot + "; native simulation unchanged.");
        }
        void Command()
        {
            string path = Path.Combine(folder, "RailgunSandbox.command"); if (!File.Exists(path)) return;
            string command = File.ReadAllText(path).Trim(); File.Delete(path);
            if (command.StartsWith("ui-preview:", StringComparison.Ordinal)) {
                if (!PortSelectionTestSave) throw new InvalidOperationException("UI previews require the isolated port-selection test save.");
                string view = command.Substring("ui-preview:".Length);
                showcase = false; menuCollapsed = view == "collapsed"; chooseDestination = view == "destinations";
                choosePort = view == "ports"; showFlightDetails = view == "details";
                compactMenuPreview = view == "compact";
                if (view == "invalid") quantity = "oops"; else quantity = "100";
                scroll = Vector2.zero; return;
            }
            if (command.StartsWith("load-case:")) {
                LoadRecoveryCase(command.Substring("load-case:".Length)); return;
            }
            if (command.StartsWith("showcase-")) { ShowcaseShot(command.Substring(9)); return; }
            if(command.StartsWith("rail-look:",StringComparison.Ordinal)||command.StartsWith("cradle-look:",StringComparison.Ordinal)||command.StartsWith("pod-look:",StringComparison.Ordinal)) {
                if(!ShellTestSave || network!=null)throw new InvalidOperationException("Asset camera controls require the isolated shell save.");
                var values=command.Substring(command.IndexOf(':')+1).Split(',').Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();
                if(values.Length!=6 || values.Any(n=>!FlightRules.Finite(n)||Math.Abs(n)>512))throw new InvalidOperationException("Invalid asset camera coordinates.");
                var eye=new Vector3(values[0],values[1],values[2]);var target=new Vector3(values[3],values[4],values[5]);
                float distance=Vector3.Distance(eye,target);if(distance<3||distance>250)throw new InvalidOperationException("Invalid asset camera distance.");
                var reference=command.StartsWith("cradle",StringComparison.Ordinal)?model.Loader.CradleFrame:command.StartsWith("pod",StringComparison.Ordinal)?FlightGlobals.ActiveVessel.rootPart.transform:model.RailArt.Frame;
                showcase=true;KSP.UI.UIMasterController.Instance.HideUI();GameEvents.onHideUI.Fire();FlightCamera.SetModeImmediate(FlightCamera.Modes.FREE);
                InspectionTarget(reference,target);FlightCamera.fetch.FieldOfView=55;
                StartCoroutine(RailInspection.Aim(reference,eye,target,folder));return;
            }
            if(command.StartsWith("rail-view:",StringComparison.Ordinal)) {
                if(!ShellTestSave || network!=null)throw new InvalidOperationException("Rail camera controls require the isolated shell save.");
                var values=command.Substring(10).Split(',').Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();
                if(values.Length!=6 || values.Any(n=>!FlightRules.Finite(n)) || values[0]<-24 || values[0]>512 || Math.Abs(values[1])>20 || Math.Abs(values[2])>20 || values[3]<3 || values[3]>200 || Math.Abs(values[4])>6.4 || Math.Abs(values[5])>1.4)throw new InvalidOperationException("Invalid rail inspection camera coordinates.");
                showcase=true;KSP.UI.UIMasterController.Instance.HideUI();GameEvents.onHideUI.Fire();FlightCamera.SetModeImmediate(FlightCamera.Modes.FREE);
                InspectionTarget(model.RailArt.Frame,new Vector3(values[1],values[0],values[2]));
                var camera=FlightCamera.fetch;camera.camHdg=values[4];camera.camPitch=values[5];camera.SetDistanceImmediate(values[3]);camera.FieldOfView=55;return;
            }
            if (command == "demo-daylight") {
                if (network != null || SaveName != "Railgun-Reusable-Demo" || state.Pods.Any(FlightRules.Busy)) throw new InvalidOperationException("Daylight setup requires the isolated demo with no active flight.");
                demoDaylight=true;TimeWarp.SetRate(3,true);return;
            }
            if (command == "loading-hold" || command == "loading-cycle" || command == "loading-release") {
                if (NetworkFleet.IsMultiplayer || SaveName != "Railgun-Reusable-Demo" && !ShellTestSave) throw new InvalidOperationException("Loading capture controls require an isolated demo save.");
                if (ShellTestSave && command == "loading-cycle") throw new InvalidOperationException("A complete shell transfer replay requires a fresh isolated fixture; do not snap a withdrawn carrier back around the shell.");
                var pod = state.Pods.FirstOrDefault(p => p.Phase == FlightPhase.Reserved);
                if (pod == null) throw new InvalidOperationException("The demo needs an unlaunched reserved pod.");
                holdLoadingDemo = command != "loading-release";
                if (command == "loading-cycle") LoadingFor(pod).Reset();
                return;
            }
            if (command == "engine-compat1") {
                var pod = state.Pods[0]; var vessel = StockPods.Vessel(pod);
                if (!ReentryTestSave || !pod.RecoveryTest || pod.Phase != FlightPhase.Landing || vessel == null || !vessel.loaded)
                    throw new InvalidOperationException("The compatibility A/B command is restricted to the isolated active recovery test.");
                ApplyLandingEngineCompatibility(pod, vessel); PersistGame(); return;
            }
            if (command == "reentry-test1") {
                var pod = state.Pods[0]; var vessel = StockPods.Vessel(pod);
                if (!ReentryTestSave || vessel == null || vessel.mainBody != home || !vessel.loaded || vessel.packed ||
                    vessel.situation != Vessel.Situations.ORBITING || !StockPods.OriginalPartsIntact(pod, vessel))
                    throw new InvalidOperationException("Load the intact orbital pod in the isolated re-entry save first.");
                StopFlight(Core(pod)); FlightRules.BeginRecoveryTest(pod, true, Now); armed.Remove(pod.VesselId);
                follow = true; PersistGame(); detail = "RETURN-ONLY TEST: real remaining fuel and cargo; no delivery/refit credit.";
                return;
            }
            if(command=="rail-clearance") {
                if(!ShellTestSave || network!=null)throw new InvalidOperationException("Loader clearance inspection requires the isolated shell save.");
                var pod=state.Pods.FirstOrDefault(p=>p.Phase==FlightPhase.Reserved);
                if(pod==null||!LoadingFor(pod).Pose.LaunchClear)throw new InvalidOperationException("Measure clearance at the verified launch-clear pose.");
                RailInspection.LoadingClearance(model,folder);
            }
            else if(command=="rail-payload") {
                if(!ShellTestSave || network!=null)throw new InvalidOperationException("Payload geometry inspection requires the isolated shell save.");
                StartCoroutine(RailInspection.PayloadWhenReady(FlightGlobals.ActiveVessel,folder));
            }
            else if(command=="rail-profile") {
                if(!ShellTestSave || network!=null)throw new InvalidOperationException("Rail profiling requires the isolated shell save.");
                StartCoroutine(RailInspection.Profile(model,folder));
            }
            else if(command=="rail-audit") {
                if(!ShellTestSave || network!=null || state.Pods.Any(FlightRules.Busy))throw new InvalidOperationException("Rail asset audit requires the idle isolated shell save.");
                StartCoroutine(RailInspection.Audit(model,folder));
            }
            else if(command=="rail-photo") {
                if(!ShellTestSave || network!=null)throw new InvalidOperationException("Rail asset capture requires the isolated shell save.");
                StartCoroutine(RailInspection.GameFrame(Path.Combine(folder,"RailGameplay.png")));
            }
            else if(command=="rail-inspect") {
                if(!ShellTestSave || network!=null)throw new InvalidOperationException("Rail asset inspection requires the isolated shell save.");
                StartCoroutine(RailInspection.Capture(model,folder));
            }
            else if (command == "prepare") Prepare();
            else if (command == "port-hardware-check" || command == "port-test-release") {
                if (!PortSelectionTestSave) throw new InvalidOperationException("Hardware probes require the isolated port-selection save.");
                var pod = state.Pods[0]; var vessel = StockPods.Vessel(pod);
                if (pod.Phase != FlightPhase.Reserved || pod.CargoTransferred || pod.LaunchedAt != 0 ||
                    vessel == null || !vessel.loaded || !vessel.Landed || vessel.srfSpeed > .5)
                    throw new InvalidOperationException("Hardware probe requires the parked, unlaunched test pod.");
                DockingHardware.ValidateLoaded(pod, vessel);
                var own = OwnPort(pod); var bottom = own.part.attachNodes.Single(n => n.id == "bottom");
                var parentNode = own.part.parent.attachNodes.Single(n => n.attachedPart == own.part);
                double gap = Vector3.Distance(own.part.transform.TransformPoint(bottom.position), own.part.parent.transform.TransformPoint(parentNode.position));
                int renderers = own.part.GetComponentsInChildren<MeshRenderer>().Length;
                int colliders = own.part.GetComponentsInChildren<Collider>().Length;
                if (gap > .02 || renderers == 0 || colliders == 0 || !StockPods.OriginalPartsIntact(pod, vessel))
                    throw new InvalidOperationException("Actual connector rendering, collider, mounting or identity check failed: gap=" + gap);
                File.AppendAllText(Path.Combine(folder, "port-hardware-check.tsv"),
                    $"{pod.TargetPort}\t{own.part.partInfo.name}\t{own.nodeType}\t{own.part.flightID}\t{vessel.parts.Count}\t{renderers}\t{colliders}\t{gap:R}\tTrue\n");
                if (command == "port-test-release") {
                    // Fixture only: no flight happened, no delivery/refuel/return credit.
                    pod.Phase = FlightPhase.Ready; pod.OrderId = ""; pod.Detail = "Isolated connector test order released without launch";
                    armed.Remove(pod.VesselId); follow = false;
                }
                PersistGame();
            }
            else if (command == "port-menu-selftest" || command == "port-coupler-selftest") {
                if (!PortSelectionTestSave) throw new InvalidOperationException("Port probes require the isolated port-selection test save.");
                var rows = new List<string>();
                if (command == "port-menu-selftest") {
                    var target = FlightGlobals.ActiveVessel;
                    foreach (var port in FreePorts(target).GroupBy(n => ConnectorType(n)).Select(g => g.First())) {
                        string type = ConnectorType(port);
                        var menu = port.part.FindModuleImplementing<RailgunDeliveryPort>();
                        if (menu == null || !menu.Events[nameof(RailgunDeliveryPort.RequestDelivery)].guiActive) throw new InvalidOperationException("Native port menu not available.");
                        menu.Events[nameof(RailgunDeliveryPort.RequestDelivery)].Invoke();
                        if (state.SelectedReceiverPort != port.part.flightID || state.SelectedReceiverAnchor != target.rootPart.flightID)
                            throw new InvalidOperationException("Native menu selected another receiver port.");
                        rows.Add(type + "\t" + port.part.flightID + "\t" + port.nodeType + "\tTrue");
                    }
                } else {
                    var pod = state.Pods[1]; var vessel = StockPods.Vessel(pod);
                    if (pod.Phase != FlightPhase.Ready || vessel == null || !vessel.loaded) throw new InvalidOperationException("Load the unreserved second pod for coupling probes.");
                    string original = pod.DockingType;
                    try {
                        foreach (string type in new[] { "size0", "size1", "size2", "size1p5" }) {
                            pod.DockingType = type; StockPods.ConfigureCoupler(pod, vessel);
                            var own = OwnPort(pod);
                            if (own.nodeTypes.Count != 1 || !own.nodeTypes.Contains(type)) throw new InvalidOperationException("Native coupler failed to acknowledge selected interface.");
                            rows.Add(type + "\t" + own.part.flightID + "\t" + own.nodeType + "\tTrue");
                        }
                    } finally { pod.DockingType = original; StockPods.ConfigureCoupler(pod, vessel); }
                }
                File.WriteAllLines(Path.Combine(folder, command + ".tsv"), new[] { "requestedType\tpartId\tnativeType\tpassed" }.Concat(rows));
                PersistGame();
            }
            else if (command.StartsWith("select-port:", StringComparison.Ordinal)) {
                uint id = uint.Parse(command.Substring("select-port:".Length), CultureInfo.InvariantCulture);
                var part = FlightGlobals.Vessels.Where(v => v != null && v.loaded).SelectMany(v => v.parts).FirstOrDefault(p => p.flightID == id);
                var menu = part?.FindModuleImplementing<RailgunDeliveryPort>();
                if (menu == null) throw new InvalidOperationException("The exact receiving part has no native delivery menu.");
                menu.Events[nameof(RailgunDeliveryPort.RequestDelivery)].Invoke();
            }
            else if (command.StartsWith("open-port:", StringComparison.Ordinal)) {
                uint id = uint.Parse(command.Substring("open-port:".Length), CultureInfo.InvariantCulture);
                var part = FlightGlobals.Vessels.Where(v => v != null && v.loaded).SelectMany(v => v.parts).FirstOrDefault(p => p.flightID == id);
                if (part == null) throw new InvalidOperationException("The requested port is not loaded.");
                UIPartActionController.Instance.SpawnPartActionWindow(part, false);
            }
            else if (command == "watch1" || command == "watch2") Watch(state.Pods[command == "watch1" ? 0 : 1]);
            else if (command == "loading-preview1") {
                if(!ShellTestSave || NetworkFleet.IsMultiplayer) throw new InvalidOperationException("Shell loading previews require the isolated shell save.");
                var target=SelectedReceiver();var port=FreePorts(target).FirstOrDefault();
                if(port==null)throw new InvalidOperationException("Load the preview receiver first.");
                Dispatch(state.Pods[0],target,port.part.flightID,double.Parse(quantity,CultureInfo.InvariantCulture));
                holdLoadingDemo=true;Watch(state.Pods[0]);ShowcaseShot("pod");
            }
            else if (command == "dispatch1" || command == "dispatch2") {
                var target = SelectedReceiver();
                var port = state.SelectedReceiverAnchor != 0 ? FreePorts(target).FirstOrDefault(n => n.part.flightID == state.SelectedReceiverPort) : FreePorts(target).FirstOrDefault();
                if (port == null) throw new InvalidOperationException("The exact selected receiving port is unavailable; no other port was selected.");
                Dispatch(state.Pods[command == "dispatch1" ? 0 : 1], target, port.part.flightID, double.Parse(quantity, CultureInfo.InvariantCulture));
            }
            else if (command == "receiver") {
                var candidate = FlightGlobals.Vessels.FirstOrDefault(v => v != null && !state.Pods.Any(p => StockPods.Vessel(p) == v) &&
                    v.protoVessel?.protoPartSnapshots.Any(part => part.modules.Any(m => m.moduleName == "ModuleDockingNode")) == true);
                if (candidate == null) throw new InvalidOperationException("No receiving ship was found in this sandbox.");
                follow = false; FlightGlobals.SetActiveVessel(candidate);
            }
            else if (command == "pause-follow") follow = false;
            else if (command == "inspect") {
                if (!state.Pods.All(p => StockPods.Vessel(p)?.loaded == true)) throw new InvalidOperationException("Load the parked pods with Watch pod before inspection.");
                var inspectionPod = StockPods.Vessel(state.Pods[0]);
                StartCoroutine(DepotInspection.Capture(model, Path.Combine(folder, "DepotInspection.png"), (Vector3)inspectionPod.GetWorldPos3D(), inspectionPod));
            }
            else if (command == "snapshot") PersistGame();
            else if (command == "save-and-exit") { PersistGame(); Application.Quit(); }
            else throw new InvalidOperationException("Unknown sandbox command");
        }
        void LoadRecoveryCase(string name)
        {
            // Native scene reload for explicitly authored isolated engineering/acceptance cases.
            // Never reset a mission or mutate the position/fuel of a running vessel.
            if (!ReentryTestSave || !name.StartsWith("case-") || name.Length > 48 ||
                name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidOperationException("Recovery cases are restricted to named fixtures in the isolated test save.");
            if (state.Pods.Any(p => FlightRules.Busy(p) && !(p.Phase == FlightPhase.Rendezvous && !p.RecoveryTest)))
                throw new InvalidOperationException("Finish the current native recovery before loading another case.");
            var fixture = Read<FleetState>(Path.Combine(folder, name + ".xml")); fixture.Validate();
            var game = GamePersistence.LoadGame(name, SaveName, true, false);
            if (game == null || game.Mode != Game.Modes.SANDBOX)
                throw new InvalidOperationException("Recovery fixture is not a native sandbox save.");
            foreach (var pod in fixture.Pods) {
                var vessel = game.flightState.protoVessels.SingleOrDefault(v => v.vesselID.ToString("N") == pod.VesselId);
                if (vessel == null || !pod.Created || pod.OriginalParts <= 0 ||
                    !vessel.protoPartSnapshots.Select(p => p.flightID).OrderBy(id => id).SequenceEqual(pod.PartIds.OrderBy(id => id)) ||
                    vessel.protoPartSnapshots.Count != pod.OriginalParts || pod.CargoTransferred || pod.RecoveryTest)
                    throw new InvalidOperationException("Recovery fixture/ledger identity, intact parts or mission state disagree.");
            }
            if (fixture.Pods[0].Phase != FlightPhase.Rendezvous || fixture.Pods[1].Phase != FlightPhase.Ready)
                throw new InvalidOperationException("Recovery case must start with one intact orbital pod and one parked pod.");
            foreach (var pod in state.Pods) StopFlight(Core(pod));
            state = fixture; SaveLedger(); enabledHere = false;
            HighLogic.SaveFolder = SaveName; HighLogic.CurrentGame = game;
            int focus = game.flightState.protoVessels.FindIndex(v => v.vesselID.ToString("N") == fixture.Pods[0].VesselId);
            Debug.Log("[RailgunSandbox] Native isolated recovery scene reload: " + name);
            FlightDriver.StartAndFocusVessel(game, focus);
        }
        void Status()
        {
            RefreshDeliveryEtas();
            var lines = new StringBuilder(network == null ? "Local reusable fleet / native 1x flight\n" : "Luna shared reusable fleet / native 1x flight\n");
            lines.AppendLine("UT=" + Now.ToString("R", CultureInfo.InvariantCulture)); lines.AppendLine("Error=" + error);
            if (network != null) lines.AppendLine("SharedAuthority: ready=" + network.Ready + " player=" + network.Player + " status=" + network.Status);
            lines.AppendLine($"NativeChecks: warp={TimeWarp.CurrentRate:R} fixedDt={TimeWarp.fixedDeltaTime:R} infinitePropellant={CheatOptions.InfinitePropellant} infiniteElectricity={CheatOptions.InfiniteElectricity} ignoreTemperature={CheatOptions.IgnoreMaxTemperature} unbreakableJoints={CheatOptions.UnbreakableJoints} noCrashDamage={CheatOptions.NoCrashDamage}");
            lines.AppendLine("Active=" + FlightGlobals.ActiveVessel?.vesselName);
            if(showcasePivot!=null&&showcaseReference!=null)lines.AppendLine($"InspectionCamera: depotActive={model.Root.activeSelf} targetIndependent={!showcasePivot.transform.IsChildOf(model.Root.transform)} cameraActive={FlightCamera.fetch.mainCamera.gameObject.activeInHierarchy}");
            lines.AppendLine($"Menu: collapsed={menuCollapsed} selectedPod={selectedPod} resource={resource} quantity={quantity} compact={compactMenuPreview}");
            lines.AppendLine($"Selection: anchor={state.SelectedReceiverAnchor} port={state.SelectedReceiverPort}");
            var selectedReceiver = SelectedReceiver();
            if (selectedReceiver != null && selectedReceiver.loaded) foreach (var port in selectedReceiver.parts.SelectMany(part => part.FindModulesImplementing<ModuleDockingNode>())) {
                var menu = port.part.FindModuleImplementing<RailgunDeliveryPort>();
                lines.AppendLine($"  receiverPort: id={port.part.flightID} title={port.part.partInfo.title} types={string.Join(",", port.nodeTypes ?? new HashSet<string>())} state={PortState(port)} deliveryMenu={menu?.Events[nameof(RailgunDeliveryPort.RequestDelivery)].guiActive} supported={SupportedPort(port)}");
            }
            foreach (var p in state.Pods) {
                var v = StockPods.Vessel(p);
                lines.AppendLine($"Pod {p.Number}: {p.Phase} / {p.Detail} / id={p.VesselId} / created={p.Created} / cargoTransferred={p.CargoTransferred}");
                lines.AppendLine($"  placement: localSnapshotSettled={StockPods.PlacementSettled(p)} admission={(network==null?"local native snapshot":"server native snapshot")}");
                if (p.Phase == FlightPhase.Reserved) { var loading = DisplayLoading(p); lines.AppendLine(FormattableString.Invariant($"  loading: stage={loading.Stage} elapsed={loading.Elapsed:F2} clamps={loading.Clamps:F3} pins={loading.Pins:F3} charge={loading.Charge:F3} launchClear={loading.LaunchClear} demoHold={holdLoadingDemo}")); }
                if (p.Phase == FlightPhase.Reserved && p.LaunchShellRequired) {
                    var route=ShellRoute(p);var pose=LoadingFor(p).Pose;
                    lines.AppendLine(FormattableString.Invariant($"  shellTransfer: stage={route.Stage} grip={route.Grip:F3} positioned={model.Loader.CarrierPositioned} target={route.X:F2},{route.Y:F2},{route.Z:F2} seated={route.Seated} carrierGrip={pose.CarrierGrip:F3} retraction={pose.CarrierRetraction:F3}"));
                }
                var eta = PodEta(p);
                lines.AppendLine("  " + eta.Display(Now) + " / basis=" + eta.Basis);
                lines.AppendLine(FormattableString.Invariant($"  eta: available={eta.Available} sampleUT={eta.SampleUt:R} earliestUT={eta.EarliestUt:R} latestUT={eta.LatestUt:R}"));
                if (network != null) lines.AppendLine("  authority: " + network.Diagnostics(p));
                lines.AppendLine($"  orderPort={p.TargetPort} couplerType={p.DockingType} railOnlyAscent={p.RailOnlyAscent}");
                if (p.LaunchShellRequired && v?.loaded == true) {
                    var shell = v?.parts?.FirstOrDefault(part => part.flightID == p.LaunchShellPartId);
                    var pf = shell?.FindModuleImplementing<ModuleProceduralFairing>();
                    lines.AppendLine($"  launchShell: part={p.LaunchShellPartId} attached={shell != null} scalar={pf?.GetScalar} openedAt={p.LaunchShellOpenedAt:R} clearAt={p.LaunchShellClearAt:R} shielded={v?.parts?.Count(part => p.PartIds.Contains(part.flightID) && part.ShieldedFromAirstream)}/{p.OriginalParts}");
                }
                if (v != null && v.loaded) {
                    var coupler = OwnPort(p);
                    if (coupler != null) lines.AppendLine("  nativeCoupler: id=" + coupler.part.flightID + " part=" + coupler.part.partInfo.name + " types=" + string.Join(",", coupler.nodeTypes ?? new HashSet<string>()) + " bottom=" + KSPUtil.WriteVector(coupler.part.attachNodes.Single(n => n.id == "bottom").position));
                }
                if (v != null) {
                    lines.AppendLine($"  vessel={v.vesselName} loaded={v.loaded} packed={v.packed} parts={v.parts?.Count} alt={v.altitude:F2} speed={v.srfSpeed:F2} sit={v.situation} pos={v.latitude:R},{v.longitude:R} padDistance={PadDistance(p,v):F2} Pe={v.orbit.PeA:F2} Ap={v.orbit.ApA:F2} holdPhysics={v.HoldPhysics} kinematic={v.rootPart?.rb?.isKinematic}");
                    if (v.loaded) {
                        var gearStates = v.parts.Where(part => p.PartIds.Contains(part.flightID)).SelectMany(part => part.FindModulesImplementing<ModuleWheels.ModuleWheelDeployment>())
                            .Select(gear => gear.fsm?.CurrentState?.name ?? "not-ready");
                        lines.AppendLine("  landingLegs=" + string.Join(",", gearStates));
                        foreach (var engine in v.parts.Where(part => p.PartIds.Contains(part.flightID)).SelectMany(part => part.FindModulesImplementing<ModuleEngines>())) {
                            var persistent = engine.part.Modules.Cast<PartModule>().FirstOrDefault(module => module.moduleName == "PersistentEngine");
                            object angle = persistent?.GetType().GetField("vesselHeadingVersusManeuverInDegree")?.GetValue(persistent);
                            object tolerance = persistent?.GetType().GetField("maneuverToleranceInDegree")?.GetValue(persistent);
                            lines.AppendLine(FormattableString.Invariant($"  engine: name={engine.part.partInfo.name} enabled={engine.enabled} ignited={engine.EngineIgnited} flameout={engine.flameout} actualThrustKN={engine.finalThrust:F3} currentThrottle={engine.currentThrottle:F3} flowMul={engine.multFlow:F4} requestMet={engine.propellantReqMet:F2} persistentAngle={angle} persistentTolerance={tolerance}"));
                        }
                        var hottest = v.parts.Select(part => new { Part = part, Ratio = Math.Max(part.temperature / Math.Max(1, part.maxTemp), part.skinTemperature / Math.Max(1, part.skinMaxTemp)) })
                            .OrderByDescending(value => value.Ratio).FirstOrDefault();
                        if (hottest != null) {
                            double retroError = v.srf_velocity.sqrMagnitude > 1 ? Vector3d.Angle(v.ReferenceTransform.up, -v.srf_velocity) : 0;
                            var guidance = Core(p);
                            if (guidance != null && p.Phase == FlightPhase.Landing)
                                lines.AppendLine(FormattableString.Invariant($"  thrustControl: mode={guidance.thrust.tmode} speedTarget={guidance.thrust.trans_spd_act:F2} requestedThrottle={guidance.thrust.targetThrottle:F3} actualLimiter={guidance.thrust.limiter} maxAccel={guidance.vesselState.maxThrustAccel:F2} limitedAccel={guidance.vesselState.limitedMaxThrustAccel:F2} groundClearance={Math.Min(guidance.vesselState.altitudeTrue, guidance.vesselState.altitudeBottom):F2} attitudeError={guidance.attitude.attitudeAngleFromTarget():F2}"));
                            if (guidance != null && p.Phase == FlightPhase.Landing) lines.AppendLine("  terminalPad: " + RecoveryGuidance.TerminalStatus(guidance.landing));
                            if (guidance != null && p.Phase == FlightPhase.Landing && guidance.landing.descentSpeedPolicy != null) {
                                var predictionModule = guidance.GetComputerModule<MechJebModuleLandingPredictions>();
                                var predictionPolicy = predictionModule.GetType().GetField("descentSpeedPolicy", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(predictionModule);
                                lines.AppendLine(FormattableString.Invariant($"  recoveryPolicy={guidance.landing.descentSpeedPolicy.GetType().Name} predictorPolicy={predictionPolicy?.GetType().Name ?? "unknown"} maxSpeed={guidance.landing.MaxAllowedSpeed():F2}"));
                            }
                            double predictedMiss = double.NaN;
                            if (guidance != null && p.Phase == FlightPhase.Landing && guidance.landing.PredictionReady) {
                                Pad(p, out double targetLat, out double targetLon);
                                var end = guidance.landing.Prediction.endPosition;
                                predictedMiss = (home.GetWorldSurfacePosition(end.latitude, end.longitude, 0) - home.GetWorldSurfacePosition(targetLat, targetLon, 0)).magnitude;
                            }
                            lines.AppendLine(FormattableString.Invariant($"  recovery: vz={v.verticalSpeed:F2} horizontal={v.horizontalSrfSpeed:F2} qkPa={v.dynamicPressurekPa:F3} mach={v.mach:F2} g={v.geeForce_immediate:F2} throttle={v.ctrlState.mainThrottle:F3} massTonnes={v.GetTotalMass():F3} retroErrorDeg={retroError:F2} uprightDeg={UprightAngle(v):F2} hottest={hottest.Part.partInfo.name} heatRatio={hottest.Ratio:F4} coreK={hottest.Part.temperature:F1} skinK={hottest.Part.skinTemperature:F1} predictedMissM={predictedMiss:F1}"));
                            if (p.Phase == FlightPhase.Landing && recoverySamples++ < 4000) {
                                string trace = Path.Combine(folder, "RecoveryTelemetry.tsv");
                                if (!File.Exists(trace)) File.WriteAllText(trace, "ut\tphysicsDt\tphase\taltASL\tvz\thorizontal\tqKPa\tmach\tg\tthrottle\tmassTonnes\tretroErrorDeg\theatRatio\tcoreK\tskinK\tparts\tpadDistance\tpredictedMiss\tguidance\n");
                                string row = FormattableString.Invariant($"{Now:R}\t{TimeWarp.fixedDeltaTime:R}\t{p.Phase}\t{v.altitude:R}\t{v.verticalSpeed:R}\t{v.horizontalSrfSpeed:R}\t{v.dynamicPressurekPa:R}\t{v.mach:R}\t{v.geeForce_immediate:R}\t{v.ctrlState.mainThrottle:R}\t{v.GetTotalMass():R}\t{retroError:R}\t{hottest.Ratio:R}\t{hottest.Part.temperature:R}\t{hottest.Part.skinTemperature:R}\t{v.parts.Count}\t{PadDistance(p,v):R}\t{predictedMiss:R}\t{p.Detail.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')}\n");
                                File.AppendAllText(trace, row);
                            }
                        }
                        foreach (string res in new[] { "LiquidFuel", "Oxidizer", "MonoPropellant" }) {
                            double propellant = v.parts.Where(part => p.PartIds.Contains(part.flightID) && !StockPods.CargoParts(v).Contains(part))
                                .SelectMany(part => part.Resources.Cast<PartResource>()).Where(r => r.resourceName == res).Sum(r => r.amount);
                            var cargo = StockPods.CargoTank(v, res); lines.AppendLine($"  {res}: propulsion={propellant:R} cargo={cargo?.amount:R} cargoLocked={cargo?.flowState == false}");
                        }
                    }
                }
            }
            File.WriteAllText(Path.Combine(folder, "RailgunSandbox.status.txt"), lines.ToString());
        }
        void OnGUI()
        {
            if (!enabledHere || state == null || showcase) return;
            if (FlightDriver.Pause) { InputLockManager.RemoveControlLock(LockName); return; }
            GUI.skin = HighLogic.Skin;
            if (menu == null) menu = new AeroMenu();
            window.height = menuCollapsed ? 90 : Mathf.Min(compactMenuPreview ? 680 : 710, Screen.height - 24);
            window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, Screen.width - 520));
            window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, Screen.height - window.height));
            window = GUILayout.Window(17269032, window, Draw, "", menu.Window, GUILayout.Width(520), GUILayout.Height(window.height));
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (window.Contains(mouse) || GUI.GetNameOfFocusedControl() == "RailgunCargoAmount") InputLockManager.SetControlLock(ControlTypes.ALL_SHIP_CONTROLS, LockName); else InputLockManager.RemoveControlLock(LockName);
        }
        void ChooseReceiver(Vessel candidate)
        {
            uint anchor = candidate.loaded ? candidate.rootPart.flightID : candidate.protoVessel.protoPartSnapshots[candidate.protoVessel.rootIndex].flightID;
            state.SelectedReceiverAnchor = anchor; state.SelectedReceiverPort = selectedPort = 0; SaveLedger();
            chooseDestination = false; choosePort = true; follow = false; FlightGlobals.SetActiveVessel(candidate);
        }
        string EtaKey(PodRecord pod) => pod.OrderId + ":" + pod.Phase;
        bool EtaReady(PodRecord pod) => network == null ? StockPods.Vessel(pod) == FlightGlobals.ActiveVessel : network.HasActivePilot(pod.Number);
        DeliveryEstimate PodEta(PodRecord pod)
        {
            var stateEstimate = DeliveryEta.State(pod, EtaReady(pod));
            if (pod.CargoTransferred || !EtaReady(pod) || pod.Phase != FlightPhase.Rendezvous && pod.Phase != FlightPhase.Docking) return stateEstimate;
            return deliveryEstimateKeys.TryGetValue(pod.Number, out var key) && key == EtaKey(pod) && deliveryEstimates.TryGetValue(pod.Number, out var estimate) ? estimate : stateEstimate;
        }
        void RefreshDeliveryEtas()
        {
            if (Time.realtimeSinceStartup < nextEtaSample && state.Pods.All(p => deliveryEstimateKeys.TryGetValue(p.Number, out var key) && key == EtaKey(p))) return;
            nextEtaSample = Time.realtimeSinceStartup + 5;
            foreach (var pod in state.Pods) {
                var vessel = StockPods.Vessel(pod);
                deliveryEstimates[pod.Number] = NativeDeliveryEta.Sample(pod, vessel, Receiver(pod), Core(pod), EtaReady(pod), Now);
                deliveryEstimateKeys[pod.Number] = EtaKey(pod);
            }
        }
        static string FriendlyPhase(PodRecord pod)
        {
            switch (pod.Phase) {
                case FlightPhase.Ready: return pod.Created ? "Ready to deliver" : "Awaiting setup";
                case FlightPhase.Reserved: return "Preparing delivery";
                case FlightPhase.RailLaunch: return "Launching";
                case FlightPhase.Ascent: return "Climbing to orbit";
                case FlightPhase.Rendezvous: return "Meeting receiver";
                case FlightPhase.Docking: return "Docking";
                case FlightPhase.Unloading: return "Delivering supplies";
                case FlightPhase.Undocking: return "Leaving receiver";
                case FlightPhase.Returning: return "Returning home";
                case FlightPhase.Landing: return "Landing at depot";
                case FlightPhase.Refitting: return "Preparing for reuse";
                case FlightPhase.Stranded: return "Recovery required";
                case FlightPhase.Lost: return "Unavailable";
                default: return "Test complete";
            }
        }
        void Draw(int id)
        {
            GUILayout.BeginHorizontal(menu.Header, GUILayout.Height(62));
            GUILayout.Label(menu.Orb, GUILayout.Width(48), GUILayout.Height(48)); GUILayout.Space(10);
            GUILayout.BeginVertical(); GUILayout.Label("Railgun Logistics", menu.Title);
            GUILayout.Label(network == null ? "Deliver. Return. Reuse." : "Shared Luna depot · Deliver. Return. Reuse.", menu.White); GUILayout.EndVertical();
            if (GUILayout.Button(menuCollapsed ? "+" : "−", menu.Quiet, GUILayout.Width(30), GUILayout.Height(28))) { menuCollapsed = !menuCollapsed; GUI.FocusControl(null); }
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, 460, 78));
            if (menuCollapsed) return;
            GUILayout.Space(9);
            var target = SelectedReceiver();
            var ports = FreePorts(target).ToList();
            if (state.SelectedReceiverAnchor != 0) selectedPort = state.SelectedReceiverPort;
            else if (!ports.Any(p => p.part.flightID == selectedPort)) selectedPort = ports.FirstOrDefault()?.part.flightID ?? 0;
            var chosen = ports.FirstOrDefault(p => p.part.flightID == selectedPort);
            bool receiverIsPod = target != null && state.Pods.Any(p => StockPods.Vessel(p) == target);
            bool routeSupported = target != null && !receiverIsPod && target.mainBody == home && target.situation == Vessel.Situations.ORBITING && target.orbit.PeA >= home.atmosphereDepth + 3000;
            scroll = GUILayout.BeginScrollView(scroll, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(window.height - 207));
            GUILayout.BeginVertical(menu.Card);
            GUILayout.BeginHorizontal(); GUILayout.Label("1", menu.Badge, GUILayout.Width(24), GUILayout.Height(22)); GUILayout.Space(8);
            GUILayout.Label("Destination", menu.Heading); GUILayout.FlexibleSpace();
            if (GUILayout.Button(chooseDestination ? "Done" : "Choose ship", menu.Quiet, GUILayout.Width(100), GUILayout.Height(25))) chooseDestination = !chooseDestination;
            GUILayout.EndHorizontal(); GUILayout.Space(8);
            if (chooseDestination) {
                var candidates = FlightGlobals.Vessels.Where(v => v != null && v.state != Vessel.State.DEAD && !state.Pods.Any(p => StockPods.Vessel(p) == v) &&
                    (v.loaded ? v.parts.Any(part => part.FindModuleImplementing<ModuleDockingNode>() != null) :
                    v.protoVessel?.protoPartSnapshots.Any(part => part.modules.Any(m => m.moduleName == "ModuleDockingNode")) == true)).OrderBy(v => v.vesselName).ToList();
                GUILayout.Label("Choose the ship that will receive supplies.", menu.Small); GUILayout.Space(6);
                foreach (var candidate in candidates) if (GUILayout.Button(candidate.vesselName + "  ·  " + candidate.mainBody.bodyName, menu.Button, GUILayout.MinHeight(32))) Run(() => ChooseReceiver(candidate));
                if (candidates.Count == 0) GUILayout.Label("No ships with docking ports were found.", menu.Small);
            } else {
                GUILayout.Label(target != null && !receiverIsPod ? target.vesselName : "Where should we deliver?", menu.Heading);
                GUILayout.Space(4);
                if (target != null && !receiverIsPod && !target.loaded) {
                    GUILayout.Label("Destination saved. View the ship to check its docking ports.", menu.Small);
                    if (GUILayout.Button("View destination", menu.Button, GUILayout.Height(29))) Run(() => { follow = false; FlightGlobals.SetActiveVessel(target); });
                } else if (chosen != null || ports.Count > 0) {
                    if (GUILayout.Button(chosen != null ? chosen.part.partInfo.title + "   ▾" : "Choose docking port   ▾", menu.Button, GUILayout.Height(31))) choosePort = !choosePort;
                    if (choosePort || chosen == null) foreach (var port in ports) {
                        if (GUILayout.Button(port.part.partInfo.title + "  ·  Port " + (ports.IndexOf(port) + 1), port == chosen ? menu.SelectedButton : menu.Button, GUILayout.Height(29)))
                            Run(() => { SelectDeliveryPort(port.part); choosePort = false; });
                    }
                    GUILayout.Space(4); GUILayout.Label("The pod is fitted with the matching connector before launch.", menu.Small);
                } else GUILayout.Label("Choose a ship above, or right-click its docking port and request delivery.", menu.Small);
                if (!routeSupported && target != null && !receiverIsPod) GUILayout.Label("This route is not available yet. Choose a ship in stable Kerbin orbit.", menu.Warning);
            }
            GUILayout.EndVertical(); GUILayout.Space(8);
            GUILayout.BeginVertical(menu.Card); menu.Step("2", "Supplies");
            GUILayout.BeginHorizontal();
            var resources = new[] { "MonoPropellant", "LiquidFuel", "Oxidizer" }; var names = new[] { "RCS fuel", "Liquid fuel", "Oxidizer" };
            for (int i = 0; i < resources.Length; i++) {
                if (GUILayout.Button(names[i], resource == resources[i] ? menu.SelectedButton : menu.Button, GUILayout.Height(33))) resource = resources[i];
            }
            GUILayout.EndHorizontal(); GUILayout.Space(8);
            double capacity = StockPods.Capacity(resource);
            double room = target != null && target.loaded && !receiverIsPod ? target.parts.SelectMany(part => part.Resources.Cast<PartResource>()).Where(r => r.resourceName == resource && r.flowState).Sum(r => r.maxAmount - r.amount) : capacity;
            double limit = Math.Max(0, Math.Min(capacity, room));
            GUILayout.BeginHorizontal(); GUILayout.Label("Amount", menu.Text, GUILayout.Width(62), GUILayout.Height(31));
            GUI.SetNextControlName("RailgunCargoAmount"); quantity = GUILayout.TextField(quantity, 12, menu.Input, GUILayout.Width(112), GUILayout.Height(32));
            GUILayout.Label("units", menu.Small, GUILayout.Width(40), GUILayout.Height(31)); GUILayout.FlexibleSpace();
            if (GUILayout.Button("Fill available", menu.Quiet, GUILayout.Width(112), GUILayout.Height(31))) quantity = limit.ToString("0.##", CultureInfo.InvariantCulture);
            GUILayout.EndHorizontal(); GUILayout.Space(4);
            GUILayout.Label("Up to " + limit.ToString("N0", CultureInfo.InvariantCulture) + " units for this delivery", menu.Small);
            GUILayout.EndVertical(); GUILayout.Space(8);
            GUILayout.BeginVertical(menu.Card); menu.Step("3", "Delivery pod");
            GUILayout.BeginHorizontal();
            foreach (var pod in state.Pods) {
                bool ready = pod.Created && pod.Phase == FlightPhase.Ready;
                GUILayout.BeginVertical(selectedPod == pod.Number ? menu.SelectedCard : menu.Card);
                if (GUILayout.Button((selectedPod == pod.Number ? "●  " : "○  ") + "Pod " + pod.Number, selectedPod == pod.Number ? menu.SelectedButton : menu.Button, GUILayout.Height(30))) selectedPod = pod.Number;
                GUILayout.Space(5); GUILayout.Label(FriendlyPhase(pod), ready ? menu.GoodBadge : menu.Badge, GUILayout.Height(23));
                if (FlightRules.Busy(pod)) GUILayout.Label(PodEta(pod).Display(Now), menu.Small);
                GUILayout.Space(4); GUILayout.Label(pod.FlightsCompleted + " completed deliveries", menu.Small);
                if (network != null) GUILayout.Label(network.Pilot(pod.Number) == "" ? "Shared pod" : "Pilot: " + network.Pilot(pod.Number), menu.Small);
                if (pod.Created && pod.Phase != FlightPhase.Lost && GUILayout.Button(network != null && network.CanTakeControl(pod.Number) && !network.LeaseValid(pod.Number) ? "Take control / watch" : "Watch pod", menu.Quiet, GUILayout.Height(24))) Run(() => Watch(pod));
                GUILayout.EndVertical();
                if (pod.Number == 1) GUILayout.Space(7);
            }
            GUILayout.EndHorizontal(); GUILayout.Space(6);
            GUILayout.Label("A pod becomes available again after it returns and refits.", menu.Small);
            GUILayout.EndVertical(); GUILayout.Space(6);
            if (GUILayout.Button(showFlightDetails ? "Hide flight details  ▴" : "Flight details  ▾", menu.Quiet, GUILayout.Height(25))) showFlightDetails = !showFlightDetails;
            if (showFlightDetails) {
                GUILayout.BeginVertical(menu.Card);
                foreach (var pod in state.Pods) {
                    GUILayout.Label("Pod " + pod.Number + " · " + FriendlyPhase(pod), menu.Heading);
                    GUILayout.Label(pod.Detail, menu.Small);
                    var eta = PodEta(pod);
                    GUILayout.Label(eta.Display(Now), menu.Small);
                    if (eta.Available) GUILayout.Label(eta.Basis, menu.Small);
                    if (pod.LaunchedAt > 0 && FlightRules.Busy(pod)) GUILayout.Label("Elapsed: " + FlightRules.Duration(Now - pod.LaunchedAt), menu.Small);
                    GUILayout.Space(6);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView(); GUILayout.Space(7);
            var selected = state.Pods.First(p => p.Number == selectedPod);
            bool portReserved = selectedPort != 0 && state.Pods.Any(p => FlightRules.Busy(p) && p.TargetPort == selectedPort);
            bool amountValid = double.TryParse(quantity, NumberStyles.Float, CultureInfo.InvariantCulture, out double units) && FlightRules.Finite(units) && units > 0 && units <= limit;
            bool setup = state.Pods.Any(p => !p.Created);
            string hint = faulted ? "Storage needs attention before a delivery can start." : !string.IsNullOrEmpty(error) ? error :
                network != null && !network.Ready ? network.Status :
                network != null && !string.IsNullOrEmpty(network.Error) ? network.Error : setup ? "Set up the depot to prepare your two reusable pods." :
                selected.Phase != FlightPhase.Ready ? "Pod " + selected.Number + " is " + FriendlyPhase(selected).ToLowerInvariant() + ". Choose a ready pod." :
                !routeSupported ? "Choose a destination in stable Kerbin orbit." : chosen == null ? "View the destination and select a free docking port." :
                portReserved ? "A pod is already assigned to this port. Choose another port." :
                limit <= 0 ? "There is no free space for this supply at the destination." :
                !amountValid ? "Enter a positive amount up to " + limit.ToString("N0", CultureInfo.InvariantCulture) + " units." :
                network != null && !network.CanReserve(selected) ? "Waiting for the pod's server reservation slot." :
                "Ready: " + quantity + " units · Pod " + selectedPod + " · matching connector";
            GUILayout.Label(hint, !string.IsNullOrEmpty(error) ? menu.Warning : menu.Text, GUILayout.Height(34));
            bool enabledBefore = GUI.enabled;
            GUI.enabled = enabledBefore && !faulted && (network == null || network.Ready) && (setup || selected.Created && selected.Phase == FlightPhase.Ready && chosen != null && routeSupported && amountValid && !portReserved && (network == null || network.CanReserve(selected)));
            if (GUILayout.Button(setup ? "Set up depot" : "Send pod " + selectedPod, menu.Primary, GUILayout.Height(42))) {
                if (setup) Run(Prepare); else Run(() => Dispatch(selected, target, selectedPort, units));
            }
            GUI.enabled = enabledBefore; GUILayout.Space(5);
            GUILayout.Label("Kerbin orbit deliveries  ·  Real flight time  ·  2 reusable pods", menu.Footer, GUILayout.Height(18));
        }
        void Run(Action action) { try { action(); error = ""; } catch (Exception ex) { Fail(ex); } }
        void OnDestroy() {
            if (Instance == this) Instance = null;
            if (network != null) network.FollowFlight = follow;
            // Luna synchronization and unacknowledged commits survive native scene switches.
            RecoveryGuidance.RemoveOwner(this);
            if (showcase && KSP.UI.UIMasterController.Instance != null) { KSP.UI.UIMasterController.Instance.ShowUI(); GameEvents.onShowUI.Fire(); }
            ReleaseInspectionTarget();if(showcasePivot!=null)UnityEngine.Object.Destroy(showcasePivot);
            model?.Dispose(); InputLockManager.RemoveControlLock(LockName);
            menu?.Dispose();
            if (enabledHere) Application.runInBackground = backgroundBefore;
            if (legacyService != null) legacyService.enabled = legacyWasEnabled;
        }
    }
}
