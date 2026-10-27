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
        sealed class Registration { public double Radius, Ground; public object Owner; public MechJebCore Core; }
        static readonly Dictionary<MechJebModuleLandingAutopilot, Registration> registered = new Dictionary<MechJebModuleLandingAutopilot, Registration>();
        static bool installed;
        public static void Register(MechJebCore core, CelestialBody body, object owner, double targetLatitude, double targetLongitude)
        {
            if (body.bodyName != "Kerbin" || Math.Abs(body.atmosphereDepth - 70000) > 1)
                throw new InvalidOperationException("The entry envelope is currently calibrated only for the installed 70 km Kerbin atmosphere.");
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
            double ground = body.pqsController.GetSurfaceHeight(body.GetRelSurfaceNVector(targetLatitude, targetLongitude)) - body.Radius;
            lock (registered) registered[core.landing] = new Registration { Radius = body.Radius, Ground = ground, Owner = owner, Core = core };
        }
        public static void Remove(MechJebModuleLandingAutopilot controller) { if (controller != null) lock (registered) registered.Remove(controller); }
        public static void RemoveOwner(object owner)
        {
            lock (registered) foreach (var key in registered.Where(pair => ReferenceEquals(pair.Value.Owner, owner)).Select(pair => pair.Key).ToArray()) registered.Remove(key);
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
