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
        sealed class Registration { public double Radius, Ground, Latitude, Longitude; public object Owner; public MechJebCore Core; public bool TerminalAssist; public string TerminalStatus = "inactive"; public double ApproachEnd=double.NaN; }
        static readonly Dictionary<MechJebModuleLandingAutopilot, Registration> registered = new Dictionary<MechJebModuleLandingAutopilot, Registration>();
        static bool installed, terminalInstalled;
        static bool OwnsLanding(Vessel vessel)
        {
            lock(registered)return registered.Values.Any(r=>r.Core!=null&&r.Core.vessel==vessel);
        }
        public static void Register(MechJebCore core, CelestialBody body, object owner, double targetLatitude, double targetLongitude, bool terminalAssist = false)
        {
            if (body.bodyName != "Kerbin" || Math.Abs(body.atmosphereDepth - 70000) > 1)
                throw new InvalidOperationException("The entry envelope is currently calibrated only for the installed 70 km Kerbin atmosphere.");
            AirbrakeForecast.Install(OwnsLanding);
            if (!installed) {
                var method = AccessTools.Method(typeof(MechJebModuleLandingAutopilot), "PickDescentSpeedPolicy");
                if (method == null || method.ReturnType != typeof(IDescentSpeedPolicy))
                    throw new InvalidOperationException("Installed MechJeb landing speed API is incompatible.");
                new Harmony("local.railgun.recovery-speed").Patch(method, postfix: new HarmonyMethod(typeof(RecoveryGuidance), nameof(PolicyPostfix)));
                var pressure = AccessTools.Method(typeof(MechJebModuleThrustController), "MaximumDynamicPressureThrottle");
                var drive = AccessTools.Method(typeof(MechJebModuleThrustController), "Drive");
                if (pressure == null || drive == null) throw new InvalidOperationException("Installed MechJeb thrust API is incompatible.");
                var harmony = new Harmony("local.railgun.recovery-speed");
                harmony.Patch(pressure, postfix: new HarmonyMethod(typeof(RecoveryGuidance), nameof(PressurePostfix)));
                harmony.Patch(drive, prefix: new HarmonyMethod(typeof(RecoveryGuidance), nameof(ThrustPrefix)));
                installed = true;
                Debug.Log("[RailgunSandbox] Native predictor/flight entry-speed policy patch connected.");
            }
            if (terminalAssist && !terminalInstalled) {
                var finalDescent = AccessTools.Method(typeof(MuMech.Landing.FinalDescent), "Drive");
                if (finalDescent == null || finalDescent.ReturnType != typeof(AutopilotStep)) throw new InvalidOperationException("Installed MechJeb final-descent API is incompatible.");
                new Harmony("local.railgun.terminal-pad").Patch(finalDescent, prefix: new HarmonyMethod(typeof(RecoveryGuidance), nameof(TerminalPrefix)));
                terminalInstalled = true;
            }
            double ground = body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(targetLatitude, targetLongitude)) - body.Radius;
            lock (registered) registered[core.landing] = new Registration { Radius = body.Radius, Ground = ground, Latitude = targetLatitude, Longitude = targetLongitude, Owner = owner, Core = core, TerminalAssist = terminalAssist };
        }
        public static void Remove(MechJebModuleLandingAutopilot controller)
        {
            if (controller == null) return;
            lock (registered) {
                if (registered.TryGetValue(controller, out var registration) && registration.TerminalAssist && registration.Core != null)
                    registration.Core.attitude.users.Remove(registration.Owner);
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
                vessel.verticalSpeed, state.localg, Math.Min(state.maxThrustAccel, state.limitedMaxThrustAccel));
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
