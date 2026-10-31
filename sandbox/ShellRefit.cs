using System;
using System.Linq;
using UnityEngine;

namespace RailgunSandbox
{
    public static class ShellRefit
    {
        // Return true only when the closed native replacement is verified.
        // A persisted native marker makes an interrupted construction resumable
        // without manufacturing a second shell or replacing the original pod.
        public static bool Update(PodRecord pod, Vessel vessel, double now, Action persist)
        {
            if (pod.Phase != FlightPhase.Refitting || !vessel.Landed)
                throw new InvalidOperationException("Shell refit requires a physically landed pod in depot refitting.");
            if (FlightRules.ClosedShellRecord(pod)) {
                if (!LaunchShell.ClosedPhysical(pod, vessel)) {
                    pod.Detail = "Waiting for replacement shell panels and native payload shielding"; return false;
                }
                return true;
            }
            var marker = StockPods.Marker(pod) ?? throw new InvalidOperationException("Returned pod construction marker missing.");
            var candidates = vessel.parts.Where(part => part.partInfo.name == LaunchShell.PartName && !pod.PartIds.Contains(part.flightID)).ToArray();
            if (candidates.Length > 1) throw new InvalidOperationException("Multiple protective shells found; refit halted without changing the pod.");
            if (candidates.Length == 0) {
                if (marker.replacementShellPartId != 0 && marker.replacementShellPartId != pod.LaunchShellPartId)
                    throw new InvalidOperationException("Persisted replacement shell is missing; refit halted instead of duplicating hardware.");
                if (vessel.srfSpeed > .2) { pod.Detail = "Waiting for the returned pod to settle before fitting its protective shell"; return false; }
                var shell = StockPods.AttachReplacementShell(pod, vessel);
                marker.replacementShellPartId = shell.flightID;
                marker.replacementShellCreatedAt = now;
                pod.Detail = "Depot attached new protective shell; waiting for native construction and shielding";
                persist();
                Debug.Log("[RailgunSandbox] Attached replacement shell " + shell.flightID + " to original pod " + pod.VesselId);
                return false;
            }
            var candidate = candidates[0];
            if (candidate.flightID == pod.LaunchShellPartId)
                throw new InvalidOperationException("Spent protective shell is still attached; refit cannot reset its separation history.");
            if (marker.replacementShellPartId != 0 && marker.replacementShellPartId != pod.LaunchShellPartId && marker.replacementShellPartId != candidate.flightID)
                throw new InvalidOperationException("Native replacement shell does not match persisted construction identity.");
            var check = FleetWire.Copy(pod);
            if (check.LaunchShellRequired) FlightRules.RearmShell(check, candidate.flightID);
            else FlightRules.UpgradeLegacyShell(check, candidate.flightID);
            if (!LaunchShell.ClosedPhysical(check, vessel)) {
                pod.Detail = "Waiting for replacement shell panels and native payload shielding";
                if (marker.replacementShellCreatedAt >= 0 && now - marker.replacementShellCreatedAt > 30)
                    throw new InvalidOperationException("Replacement shell did not establish native payload shielding; pod remains unavailable.");
                return false;
            }
            if (pod.LaunchShellRequired) FlightRules.RearmShell(pod, candidate.flightID);
            else FlightRules.UpgradeLegacyShell(pod, candidate.flightID);
            marker.replacementShellPartId = candidate.flightID;
            pod.Detail = "New closed protective shell verified; completing depot refit";
            persist();
            return false; // Acknowledge the native replacement before releasing Ready.
        }
    }
}
