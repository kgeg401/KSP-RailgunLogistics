using System;
using System.Linq;
using MuMech;
using UnityEngine;

namespace RailgunSandbox
{
    public static class NativeDeliveryEta
    {
        // Read-only use of native orbit patches. No maneuver placement or guidance mutation.
        public static DeliveryEstimate Sample(PodRecord pod, Vessel vessel, Vessel receiver, MechJebCore core, bool ready, double now)
        {
            var state = DeliveryEta.State(pod, ready);
            if (pod.CargoTransferred || !ready || (pod.Phase != FlightPhase.Rendezvous && pod.Phase != FlightPhase.Docking)) return state;
            if (vessel == null || receiver == null || core == null || !vessel.loaded || vessel.packed ||
                receiver.mainBody != vessel.mainBody || vessel.mainBody == null) return state;
            try {
                double separation = (vessel.GetWorldPos3D() - receiver.GetWorldPos3D()).magnitude;
                double speed = core.GetComputerModule<MechJebModuleDockingAutopilot>().speedLimit;
                if (separation <= 160 && (vessel.obt_velocity - receiver.obt_velocity).magnitude <= 2)
                    return DeliveryEta.Encounter(now, now, separation, speed, "Based on the current docking approach; alignment and capture time can vary");
                if (pod.Phase == FlightPhase.Docking) return state;
                var rendezvous = core.GetComputerModule<MechJebModuleRendezvousAutopilot>();
                if (!rendezvous.enabled) return state;
                var nodes = vessel.patchedConicSolver?.maneuverNodes;
                Orbit path = nodes != null && nodes.Count > 0 ? nodes.Last().nextPatch : vessel.orbit;
                double start = nodes != null && nodes.Count > 0 ? Math.Max(now, nodes.Last().UT) : now;
                if (path == null || path.referenceBody != receiver.mainBody || path.eccentricity >= 1) return state;
                double encounter = path.NextClosestApproachTime(receiver.orbit, start);
                double miss = path.Separation(receiver.orbit, encounter);
                double initialMiss = path.Separation(receiver.orbit, start);
                double radius = path.getRelativePositionAtUT(start).magnitude;
                if (FlightRules.Finite(initialMiss) && initialMiss >= 0 && initialMiss < radius / 25 &&
                    !(FlightRules.Finite(encounter) && encounter >= start && encounter < start + 150 && miss < 100))
                    return DeliveryEta.Rendezvous(now, start, initialMiss, rendezvous.maxClosingSpeed, speed,
                        "Based on the next close-approach maneuver and docking; the route can change");
                if (!FlightRules.Finite(encounter) || encounter < start || encounter - start > Math.Max(path.period, receiver.orbit.period) * 2 ||
                    !FlightRules.Finite(miss) || miss > receiver.orbit.semiMajorAxis / 25)
                    return state;
                return DeliveryEta.Rendezvous(now, encounter, miss, rendezvous.maxClosingSpeed, speed,
                    nodes != null && nodes.Count > 0 ? "Based on planned rendezvous and docking; the route can change" : "Based on the current orbit and docking approach; the route can change");
            } catch (Exception ex) {
                // A native patch may be between rebuilds. Invalidate instead of
                // retaining a countdown from a trajectory that no longer exists.
                Debug.LogWarning("[RailgunSandbox] ETA awaiting a valid native route: " + ex.GetType().Name);
                return state;
            }
        }
    }
}
