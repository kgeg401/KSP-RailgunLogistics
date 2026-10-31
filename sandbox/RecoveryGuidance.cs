using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MuMech;
using UnityEngine;

namespace RailgunSandbox
{
    // Keep the predictor and real flight on the SAME immutable speed policy.
    // Only registered logistics-pod landing controllers are affected by the postfix.
    public static class RecoveryGuidance
    {
        sealed class Registration { public double Radius, Ground, Latitude, Longitude; public object Owner; public MechJebCore Core; public bool TerminalAssist; public string TerminalStatus = "inactive"; public double ApproachEnd=double.NaN; public bool PreviousAutoRcs,PreviousRcs,PreviousRcsAdjustment,RcsAllowed,NativeFinalCapture,EntryAimSet,HomeAimRestored,Replanned; }
        static readonly Dictionary<MechJebModuleLandingAutopilot, Registration> registered = new Dictionary<MechJebModuleLandingAutopilot, Registration>();
        static bool installed, terminalInstalled;
        static string installFailure;
        static double isolatedApproachSpeed=60;
        static bool isolatedNativeFinal;
        static bool isolatedContinuousLookahead;
        static double isolatedEntryBias;
        static double isolatedNativeExitSpeed=1;
        static double isolatedLateMonoReserve;
        static bool isolatedReplan;
        static bool isolatedVerticalCapture;
        static bool isolatedCaptureCorridor;
        static bool loopbackConfigured;
        public static bool PrivateLoopbackEligible {
            get {
                var args=Environment.GetCommandLineArgs();
                if(!NetworkFleet.Connected||!NetworkFleet.IsMultiplayer||!args.Contains("-railgun-guidance-lab")||
                    !(args.Contains("-railgun-mp-test")||args.Contains("-railgun-mp-test-b")))return false;
                var endpoint=LmpClient.Network.NetworkMain.ClientConnection?.ServerConnection?.RemoteEndPoint;
                return FlightRules.PrivateGuidanceExperiment(args,NetworkFleet.Connected,
                    NetworkFleet.IsMultiplayer,endpoint?.Address.ToString(),endpoint?.Port??0);
            }
        }
        static bool ExperimentActive => SandboxFlight.ReentryTestSave || loopbackConfigured && PrivateLoopbackEligible;
        // Registered logistics vessels use the completed native route profile.
        // Explicit engineering fixtures retain their separately selected controls.
        static double ApproachSpeed => ExperimentActive ? isolatedApproachSpeed : 120;
        static double EntryBias => ExperimentActive ? isolatedEntryBias : 2000;
        static bool NativeFinal => !ExperimentActive || isolatedNativeFinal;
        static bool VerticalCapture => !ExperimentActive || isolatedVerticalCapture;
        static bool CaptureCorridor => !ExperimentActive || isolatedCaptureCorridor;
        static double CaptureExitSpeed => ExperimentActive ? isolatedNativeExitSpeed : 1;
        static double LateMonoReserve => ExperimentActive ? isolatedLateMonoReserve : 0;
        static bool Replan => ExperimentActive && isolatedReplan;
        static bool ContinuousLookahead => ExperimentActive && isolatedContinuousLookahead;
        public static string ActiveProfile => ExperimentActive ? "explicit-engineering-profile" : "Kerbin-native-return-v1";
        public static void SetIsolatedApproachSpeed(double speed,bool nativeFinal=false,bool continuousLookahead=false,double entryBias=0,double nativeExitSpeed=1,double lateMonoReserve=0,bool replan=false,bool verticalCapture=false,bool captureCorridor=false)
        {
            bool localDescent=SandboxFlight.ReentryTestSave&&Environment.GetCommandLineArgs().Contains("-railgun-descent-fixture");
            bool loopback=PrivateLoopbackEligible;
            if(!(localDescent||loopback)||!FlightRules.Finite(speed)||speed<=0||speed>150||!FlightRules.Finite(entryBias)||entryBias<0||entryBias>5000||!FlightRules.Finite(nativeExitSpeed)||nativeExitSpeed<1||nativeExitSpeed>5||!FlightRules.Finite(lateMonoReserve)||lateMonoReserve<0||lateMonoReserve>5)
                throw new InvalidOperationException("Approach experiments require the explicit isolated native descent harness.");
            loopbackConfigured=loopback;
            isolatedApproachSpeed=speed;
            isolatedNativeFinal=nativeFinal;
            isolatedContinuousLookahead=continuousLookahead;
            isolatedEntryBias=entryBias;
            isolatedNativeExitSpeed=nativeExitSpeed;
            isolatedLateMonoReserve=lateMonoReserve;
            isolatedReplan=replan;
            isolatedVerticalCapture=verticalCapture;
            isolatedCaptureCorridor=captureCorridor;
        }
        static bool OwnsLanding(Vessel vessel)
        {
            lock(registered)return registered.Values.Any(r=>r.Core!=null&&r.Core.vessel==vessel);
        }
        public static void Register(MechJebCore core, CelestialBody body, object owner, double targetLatitude, double targetLongitude, bool terminalAssist = false)
        {
            if (body.bodyName != "Kerbin" || Math.Abs(body.atmosphereDepth - 70000) > 1)
                throw new InvalidOperationException("The entry envelope is currently calibrated only for the installed 70 km Kerbin atmosphere.");
            AirbrakeForecast.Install(OwnsLanding);
            if(installFailure!=null)throw new InvalidOperationException("Recovery patch installation failed; restart with a compatible build: "+installFailure);
            if (!installed) {
                var method = AccessTools.Method(typeof(MechJebModuleLandingAutopilot), "PickDescentSpeedPolicy");
                if (method == null || method.ReturnType != typeof(IDescentSpeedPolicy))
                    throw new InvalidOperationException("Installed MechJeb landing speed API is incompatible.");
                var pressure = AccessTools.Method(typeof(MechJebModuleThrustController), "MaximumDynamicPressureThrottle");
                var drive = AccessTools.Method(typeof(MechJebModuleThrustController), "Drive");
                if (pressure == null || drive == null) throw new InvalidOperationException("Installed MechJeb thrust API is incompatible.");
                var rcs=AccessTools.DeclaredMethod(typeof(ModuleRCS),nameof(ModuleRCS.FixedUpdate),Type.EmptyTypes);
                if(rcs==null||rcs.DeclaringType!=typeof(ModuleRCS)||rcs.GetMethodBody()==null)
                    throw new InvalidOperationException("Installed native RCS actuator update API is incompatible.");
                var harmony = new Harmony("local.railgun.recovery-speed");
                try {
                    harmony.Patch(method,postfix:new HarmonyMethod(typeof(RecoveryGuidance),nameof(PolicyPostfix)));
                    harmony.Patch(pressure, postfix: new HarmonyMethod(typeof(RecoveryGuidance), nameof(PressurePostfix)));
                    harmony.Patch(drive, prefix: new HarmonyMethod(typeof(RecoveryGuidance), nameof(ThrustPrefix)));
                    harmony.Patch(rcs,prefix:new HarmonyMethod(typeof(RecoveryGuidance),nameof(RcsPrefix)));
                    installed=true;
                } catch(Exception e) {
                    harmony.UnpatchAll("local.railgun.recovery-speed");installFailure=e.Message;throw;
                }
                Debug.Log("[RailgunSandbox] Native predictor/flight entry-speed policy patch connected.");
            }
            if (terminalAssist && !terminalInstalled) {
                var finalDescent = AccessTools.Method(typeof(MuMech.Landing.FinalDescent), "Drive");
                if (finalDescent == null || finalDescent.ReturnType != typeof(AutopilotStep)) throw new InvalidOperationException("Installed MechJeb final-descent API is incompatible.");
                new Harmony("local.railgun.terminal-pad").Patch(finalDescent, prefix: new HarmonyMethod(typeof(RecoveryGuidance), nameof(TerminalPrefix)));
                terminalInstalled = true;
            }
            double ground = body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(targetLatitude, targetLongitude)) - body.Radius;
            lock (registered) {
                if(registered.ContainsKey(core.landing))Remove(core.landing);
                registered[core.landing] = new Registration { Radius = body.Radius, Ground = ground, Latitude = targetLatitude, Longitude = targetLongitude, Owner = owner, Core = core, TerminalAssist = terminalAssist,
                    PreviousAutoRcs=core.attitude.RCS_auto,PreviousRcs=core.vessel.ActionGroups[KSPActionGroup.RCS],PreviousRcsAdjustment=core.landing.rcsAdjustment };
                core.attitude.RCS_auto=false;
            }
        }
        public static void UpdateRcs(MechJebCore core,double clearance,double usableMono)
        {
            lock(registered) {
                if(core==null||!registered.ContainsKey(core.landing))return;
                var vessel=core.vessel;
                if(vessel==null||vessel!=FlightGlobals.ActiveVessel||!vessel.loaded||vessel.packed)return;
                var registration=registered[core.landing];
                if(Replan&&!registration.Replanned&&clearance<12000&&clearance>10000&&vessel.verticalSpeed<0&&core.landing.enabled) {
                    // Reinitialize only the installed navigation state machine.
                    // Keep the continuous native scene, pose, resources and clock.
                    string previousStep=core.landing.CurrentStep?.GetType().FullName??"none";
                    registration.Replanned=true;
                    core.landing.LandAtPositionTarget(registration.Owner);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(KSPUtil.ApplicationRootPath,"saves",SandboxFlight.SaveName,"NativeReplanTransitions.tsv"),FormattableString.Invariant($"{Planetarium.GetUniversalTime():R}\t{vessel.id}\t{clearance:R}\t{usableMono:R}\t{previousStep}\t{core.landing.CurrentStep?.GetType().FullName}\n"));
                }
                if(EntryBias>0) {
                    bool entry=clearance>8000&&!registration.HomeAimRestored;
                    if(entry&&!registration.EntryAimSet||!entry&&!registration.HomeAimRestored) {
                        double latitude=registration.Latitude;
                        double longitude=registration.Longitude+(entry?EntryBias/(vessel.mainBody.Radius*Math.Cos(latitude*Math.PI/180))*180/Math.PI:0);
                        core.target.SetPositionTarget(vessel.mainBody,latitude,longitude);
                        registration.EntryAimSet=true;if(!entry)registration.HomeAimRestored=true;
                        string id=SandboxFlight.Instance?.WarpFleet?.Pods.FirstOrDefault(p=>StockPods.Vessel(p)==vessel)?.RecoveryTestRunId??"";
                        System.IO.File.AppendAllText(System.IO.Path.Combine(KSPUtil.ApplicationRootPath,"saves",SandboxFlight.SaveName,"EntryAimTransitions.tsv"),FormattableString.Invariant($"{Planetarium.GetUniversalTime():R}\t{id}\t{entry}\t{latitude:R}\t{longitude:R}\t{clearance:R}\n"));
                    }
                }
                bool coast=core.landing.CurrentStep is MuMech.Landing.CoastToDeceleration;
                // Native CoastToDeceleration intentionally uses RCS translation.
                // Retain that function with a bounded real propellant budget.
                core.landing.rcsAdjustment=registration.PreviousRcsAdjustment&&usableMono>10;
                bool courseCorrection=coast&&core.landing.rcsAdjustment;
                if(!courseCorrection&&core.rcs.enabled)core.rcs.enabled=false;
                bool terminal=core.landing.CurrentStep is MuMech.Landing.FinalDescent&&
                    (registration.TerminalStatus.StartsWith("centering /",StringComparison.Ordinal)||clearance<1000);
                // Private comparison: budget existing real propellant for the
                // final kilometre rather than exhausting it on approach.
                double steeringMono=usableMono-(clearance>1000?LateMonoReserve:0);
                bool enabled=RecoveryRcsPolicy.Enable(core.landing.enabled&&terminal,vessel.ActionGroups[KSPActionGroup.RCS],clearance,core.attitude.attitudeAngleFromTarget(),steeringMono);
                registration.RcsAllowed=enabled||courseCorrection;
                core.attitude.RCS_auto=false;
                if(!courseCorrection)vessel.ActionGroups.SetGroup(KSPActionGroup.RCS,enabled);
            }
        }
        static void RcsPrefix(ModuleRCS __instance)
        {
            // Other installed controllers can change the group between the
            // quarter-second planning tick and native actuator consumption.
            // Enforce only the owned reserve gate, then run ALL stock physics.
            var vessel=__instance.vessel;if(vessel==null||!vessel.ActionGroups[KSPActionGroup.RCS])return;
            lock(registered) {
                if(registered.Values.Any(r=>r.Core!=null&&r.Core.vessel==vessel&&!r.RcsAllowed))
                    vessel.ActionGroups.SetGroup(KSPActionGroup.RCS,false);
            }
        }
        public static void Remove(MechJebModuleLandingAutopilot controller)
        {
            if (controller == null) return;
            lock (registered) {
                if (registered.TryGetValue(controller, out var registration) && registration.Core != null) {
                    if(registration.TerminalAssist)registration.Core.attitude.users.Remove(registration.Owner);
                    registration.Core.attitude.RCS_auto=registration.PreviousAutoRcs;
                    registration.Core.landing.rcsAdjustment=registration.PreviousRcsAdjustment;
                    if(registration.EntryAimSet&&!registration.HomeAimRestored&&registration.Core.vessel!=null)
                        registration.Core.target.SetPositionTarget(registration.Core.vessel.mainBody,registration.Latitude,registration.Longitude);
                    if(registration.Core.vessel!=null)registration.Core.vessel.ActionGroups.SetGroup(KSPActionGroup.RCS,registration.PreviousRcs);
                }
                registered.Remove(controller);
            }
        }
        public static void RemoveOwner(object owner)
        {
            lock (registered) foreach (var key in registered.Where(pair => ReferenceEquals(pair.Value.Owner, owner)).Select(pair => pair.Key).ToArray()) Remove(key);
        }
        public static string TerminalStatus(MechJebModuleLandingAutopilot controller)
        {
            lock (registered) return controller != null && registered.TryGetValue(controller, out var registration) ? registration.TerminalStatus : "inactive";
        }
        public static bool PoweredApproachActive(MechJebModuleLandingAutopilot controller)
        {
            lock(registered)return controller!=null&&registered.TryGetValue(controller,out var r)&&FlightRules.Finite(r.ApproachEnd);
        }
        static bool TerminalPrefix(MuMech.Landing.FinalDescent __instance, ref AutopilotStep __result)
        {
            var core = __instance.core;
            Registration registration;
            lock (registered) if (!registered.TryGetValue(core.landing, out registration) || !registration.TerminalAssist) return true;
            registration.TerminalStatus = "outside terminal envelope";
            var vessel = core.vessel;
            if (vessel == null || vessel.LandedOrSplashed || !vessel.loaded || vessel.packed || vessel != FlightGlobals.ActiveVessel) return true;
            var state = core.vesselState;
            Vector3d up = (state.CoM - vessel.mainBody.position).normalized;
            Vector3d north = Vector3d.Exclude(up, vessel.mainBody.GetWorldSurfacePosition(vessel.latitude + .01, vessel.longitude, vessel.altitude) - state.CoM).normalized;
            Vector3d east = Vector3d.Cross(up, north).normalized;
            Vector3d offset = Vector3d.Exclude(up, vessel.mainBody.GetWorldSurfacePosition(registration.Latitude, registration.Longitude, registration.Ground) - state.CoM);
            Vector3d velocity = Vector3d.Exclude(up, vessel.srf_velocity);
            double clearance = Math.Min(state.altitudeBottom, state.altitudeTrue);
            double available=Math.Min(state.maxThrustAccel,state.limitedMaxThrustAccel);
            double ex=Vector3d.Dot(offset,east),ez=Vector3d.Dot(offset,north),vx=Vector3d.Dot(velocity,east),vz=Vector3d.Dot(velocity,north);
            if(NativeFinal) {
                bool previousCapture=registration.NativeFinalCapture;
                double captureExitSpeed=VerticalCapture&&CaptureCorridor?Math.Max(2,CaptureExitSpeed):CaptureExitSpeed;
                if(registration.NativeFinalCapture&&(offset.magnitude>10||velocity.magnitude>captureExitSpeed||Vector3d.Angle(vessel.ReferenceTransform.up,up)>20))registration.NativeFinalCapture=false;
                // Native horizontal kill damps velocity with an upright target.
                // Use a conservative four-second drift budget inside an 8m
                // corridor, retaining 17m margin to actual pad acceptance.
                bool corridor=VerticalCapture&&CaptureCorridor&&clearance>2&&clearance<1000&&velocity.magnitude<2&&offset.magnitude+4*velocity.magnitude<8;
                if((offset.magnitude<3&&velocity.magnitude<.25||corridor)&&Vector3d.Angle(vessel.ReferenceTransform.up,up)<10)registration.NativeFinalCapture=true;
                if(previousCapture!=registration.NativeFinalCapture) {
                    string id=SandboxFlight.Instance?.WarpFleet?.Pods.FirstOrDefault(p=>StockPods.Vessel(p)==vessel)?.RecoveryTestRunId??"";
                    System.IO.File.AppendAllText(System.IO.Path.Combine(KSPUtil.ApplicationRootPath,"saves",SandboxFlight.SaveName,"NativeFinalTransitions.tsv"),FormattableString.Invariant($"{Planetarium.GetUniversalTime():R}\t{id}\t{registration.NativeFinalCapture}\t{offset.magnitude:R}\t{velocity.magnitude:R}\t{clearance:R}\n"));
                }
                if(registration.NativeFinalCapture) {
                    if(VerticalCapture&&FlightRules.Finite(clearance)&&FlightRules.Finite(available)&&available>state.localg*1.1) {
                        // Keep the existing vertical-speed controller through
                        // capture. Native surface-velocity retrograde flips when
                        // a residual throttle transient reverses vertical speed.
                        // This requests stock controls only, never applied force.
                        double stoppingSpeed=Math.Sqrt(2*Math.Max(0,.5*(available-state.localg))*Math.Max(0,clearance-1));
                        double descent=Math.Max(.4,Math.Min(40,Math.Min(stoppingSpeed,Math.Max(.4,clearance/2))));
                        core.thrust.tmode=MechJebModuleThrustController.TMode.KEEP_VERTICAL;
                        core.thrust.trans_kill_h=true;
                        core.thrust.trans_spd_act=(float)-descent;
                        registration.TerminalStatus=FormattableString.Invariant($"captured / native vertical descent / target={-descent:F2} m/s");
                        __result=__instance;return false;
                    }
                    // With horizontal capture established, use the installed
                    // native braking/touchdown controller rather than an
                    // indefinite proportional powered hover over the pad.
                    registration.TerminalStatus="aligned / native final descent";
                    return true;
                }
            }
            if(Environment.GetCommandLineArgs().Contains("-railgun-intercept-test")&&clearance>30&&clearance<7000) {
                double now=Planetarium.GetUniversalTime();
                if(!FlightRules.Finite(registration.ApproachEnd)) {
                    Vector3d axis=vessel.ReferenceTransform.up;
                    double duration=PoweredApproach.Plan(ex,clearance,ez,vx,vessel.verticalSpeed,vz,state.localg,available,
                        Vector3d.Dot(axis,east),Vector3d.Dot(axis,up),Vector3d.Dot(axis,north),vessel.dynamicPressurekPa>10?15:45);
                    if(FlightRules.Finite(duration))registration.ApproachEnd=now+duration;
                }
                if(FlightRules.Finite(registration.ApproachEnd)) {
                    double remaining=Math.Max(2,registration.ApproachEnd-now);
                    var request=PoweredApproach.Command(ex,clearance,ez,vx,vessel.verticalSpeed,vz,state.localg,remaining);
                    Vector3d acceleration=up*request.Y+east*request.X+north*request.Z;
                    if(request.Y>0&&FlightRules.Finite(request.Magnitude)&&available>0) {
                        core.thrust.tmode=MechJebModuleThrustController.TMode.OFF;
                        core.thrust.trans_kill_h=false;
                        core.attitude.attitudeTo(acceleration.normalized,AttitudeReference.INERTIAL,registration.Owner);
                        double alignment=Vector3d.Dot(vessel.ReferenceTransform.up,acceleration.normalized);
                        // Preserve thrust-axis projection and native gimbal
                        // authority while aligning; reducing throttle by cos
                        // stalled the turn against the measured aerodynamic load.
                        core.thrust.targetThrottle=(float)(alignment>.5?Math.Max(0,Math.Min(1,request.Magnitude/available/alignment)):0);
                        registration.TerminalStatus=FormattableString.Invariant($"intercept / error={offset.magnitude:F2} m / remaining={remaining:F2} s / requestedAcceleration={request.Magnitude:F2} m/s2 / alignment={alignment:F3}");
                        __result=__instance;return false;
                    }
                }
            }
            if (Vector3d.Angle(vessel.ReferenceTransform.up, up) > 60) return true;
            var command = TerminalPadGuidance.Compute(Vector3d.Dot(offset, east), Vector3d.Dot(offset, north),
                Vector3d.Dot(velocity, east), Vector3d.Dot(velocity, north), Math.Min(state.altitudeBottom, state.altitudeTrue),
                vessel.verticalSpeed, state.localg, Math.Min(state.maxThrustAccel, state.limitedMaxThrustAccel),ApproachSpeed,ContinuousLookahead);
            if (!command.Active) return true;
            // Own this stage before the stock FinalDescent writes KEEP_SURFACE.
            // Alternating modes each frame resets MJ's vertical PID and wastes fuel.
            core.thrust.tmode = MechJebModuleThrustController.TMode.KEEP_VERTICAL;
            core.thrust.trans_kill_h = false;
            double verticalTarget = -(command.SlowDescent ? command.MaximumDescentSpeed : Math.Max(.4, Math.Min(20, clearance / 8)));
            core.thrust.trans_spd_act = (float)verticalTarget;
            double verticalSupport = Math.Max(state.localg, Math.Min(state.limitedMaxThrustAccel, state.localg + (verticalTarget - vessel.verticalSpeed) / 2.5));
            core.attitude.attitudeTo((up * verticalSupport + east * command.AccelerationX + north * command.AccelerationZ).normalized, AttitudeReference.INERTIAL, registration.Owner);
            registration.TerminalStatus = FormattableString.Invariant($"centering / error={offset.magnitude:F2} m / horizontal={velocity.magnitude:F2} m/s / tiltRequest={Math.Atan(Math.Sqrt(command.AccelerationX * command.AccelerationX + command.AccelerationZ * command.AccelerationZ) / verticalSupport) * 180 / Math.PI:F2} deg / descentTarget={core.thrust.trans_spd_act:F2} m/s");
            __result = __instance;
            return false;
        }
        static void PolicyPostfix(MechJebModuleLandingAutopilot __instance, ref IDescentSpeedPolicy __result)
        {
            lock (registered)
                if (__result != null && !(__result is EntryPolicy) && registered.TryGetValue(__instance, out var registration))
                    __result = new EntryPolicy(__result, registration.Radius, registration.Ground, registration.Core.vesselState.maxThrustAccel, registration.Core.vesselState.localg);
        }
        static Registration ForThrust(MechJebModuleThrustController controller)
        {
            lock (registered) return registered.Values.FirstOrDefault(registration => ReferenceEquals(registration.Core.thrust, controller));
        }
        static void PressurePostfix(MechJebModuleThrustController __instance, ref float __result)
        {
            // Ascent's pressure-based throttle cutoff cannot slow a descending rocket.
            // Keep the setting intact for ascent and every unrelated vessel.
            if (ForThrust(__instance) != null) __result = 1;
        }
        static void ThrustPrefix(MechJebModuleThrustController __instance)
        {
            var registration = ForThrust(__instance);
            if (registration == null || __instance.tmode != MechJebModuleThrustController.TMode.KEEP_SURFACE) return;
            var state = registration.Core.vesselState;
            if (state.altitudeASL > 15000 || state.speedVertical >= 0) return;
            var vessel = registration.Core.vessel;
            var centre = vessel.GetWorldPos3D(); var up = (centre - vessel.mainBody.position).normalized;
            double clearance = Math.Min(state.altitudeTrue, state.altitudeBottom);
            if (Physics.Raycast((Vector3)centre, (Vector3)(-up), out RaycastHit hit, 20000, 32768, QueryTriggerInteraction.Ignore))
                clearance = Math.Min(clearance, Math.Max(0, hit.distance - Math.Max(2, vessel.vesselSize.magnitude * .6)));
            double safe = ReentryEnvelope.TerminalSpeedCeiling(clearance, state.maxThrustAccel, state.localg);
            float desired = __instance.trans_spd_act;
            if (float.IsNaN(desired) || float.IsInfinity(desired)) {
                Debug.LogWarning("[RailgunSandbox] Invalid native terminal target; using measured terrain braking envelope.");
                desired = (float)safe;
            }
            __instance.trans_spd_act = (float)Math.Min(desired, safe);
        }
        sealed class EntryPolicy : IDescentSpeedPolicy
        {
            readonly IDescentSpeedPolicy native; readonly double radius, ground, acceleration, gravity;
            public EntryPolicy(IDescentSpeedPolicy native, double radius, double ground, double acceleration, double gravity)
            { this.native = native; this.radius = radius; this.ground = ground; this.acceleration = acceleration; this.gravity = gravity; }
            public double MaxAllowedSpeed(Vector3d position, Vector3d velocity)
            {
                double altitude = position.magnitude - radius;
                double speed = ReentryEnvelope.Limit(native.MaxAllowedSpeed(position, velocity), altitude);
                if (altitude < 15000) speed = Math.Min(speed, ReentryEnvelope.TerminalSpeedCeiling(altitude - ground, acceleration, gravity));
                return speed;
            }
        }
    }
}
