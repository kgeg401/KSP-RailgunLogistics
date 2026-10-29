using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RailgunSandbox
{
    public static class LaunchShell
    {
        public const string PartName = "krlLaunchShell";
        public const double Radius = 3.5;

        public static void Sample(PodRecord p, Vessel v, string folder, double now)
        {
            if (!p.LaunchShellRequired) return;
            string path = Path.Combine(folder, "LaunchShellEvidence.tsv");
            if (!File.Exists(path)) File.WriteAllText(path, "ut\tphase\taltitude\tverticalSpeed\tattached\tfairingScalar\tpanels\tshieldedOriginalParts\toriginalPartsPresent\topenedAt\tpanelsReleased\tbaseReleased\tclearAt\tignitionPermitted\tmassTonnes\n");
            var shell = v.parts.FirstOrDefault(part => part.flightID == p.LaunchShellPartId);
            var fairing = shell?.FindModuleImplementing<ModuleProceduralFairing>();
            File.AppendAllText(path, FormattableString.Invariant($"{now:R}\t{p.Phase}\t{v.altitude:R}\t{v.verticalSpeed:R}\t{shell != null}\t{fairing?.GetScalar}\t{fairing?.Panels?.Count ?? 0}\t{v.parts.Count(part => p.PartIds.Contains(part.flightID) && part.ShieldedFromAirstream)}\t{v.parts.Count(part => p.PartIds.Contains(part.flightID))}\t{p.LaunchShellOpenedAt:R}\t{p.LaunchShellPanelsReleased}\t{p.LaunchShellBaseReleased}\t{p.LaunchShellClearAt:R}\t{LaunchShellPolicy.MayBurn(true, shell != null, p.LaunchShellClearAt, now)}\t{v.GetTotalMass():R}\n"));
        }

        public static void ValidateClosed(PodRecord p, Vessel v)
        {
            if (!p.LaunchShellRequired) return;
            var shell = v.parts.SingleOrDefault(part => part.flightID == p.LaunchShellPartId);
            var fairing = shell?.FindModuleImplementing<ModuleProceduralFairing>();
            if (!p.RailOnlyAscent || p.LaunchShellOpenedAt >= 0 || p.LaunchShellPanelsReleased || p.LaunchShellBaseReleased ||
                fairing == null || fairing.GetScalar > .5f || fairing.Panels == null || fairing.Panels.Count == 0 ||
                v.parts.Count(part => p.PartIds.Contains(part.flightID) && part.ShieldedFromAirstream) != p.OriginalParts ||
                shell.FindModuleImplementing<ModuleDecouple>()?.isDecoupled != false)
                throw new InvalidOperationException("Launch requires a closed physical protective shell and a rail-only ascent.");
        }

        public static void Profile(ConfigNode node, double payloadHeight)
        {
            var fairing = node.GetNodes("MODULE").Single(n => n.GetValue("name") == "ModuleProceduralFairing");
            fairing.RemoveNodes("XSECTION");
            fairing.SetValue("fsm", "st_idle", true);
            fairing.SetValue("nArcs", 4, true);
            fairing.SetValue("useClamshell", true, true);
            fairing.SetValue("ejectionForce", 150, true);
            Section(fairing, 0, 1.875);
            Section(fairing, 1.5, Radius);
            Section(fairing, payloadHeight + 1, Radius);
            Section(fairing, payloadHeight + 5, 0);
        }
        static void Section(ConfigNode node, double height, double radius)
        {
            var section = node.AddNode("XSECTION");
            section.AddValue("h", height.ToString("R", CultureInfo.InvariantCulture));
            section.AddValue("r", radius.ToString("R", CultureInfo.InvariantCulture));
        }

        // True only after stock deployment, native base separation, and clearance.
        // A missing closed shell is a failure, not evidence of successful jettison.
        public static bool Update(PodRecord p, Vessel v, double alongRail, double now)
        {
            if (!p.LaunchShellRequired) return true;
            var shell = v.parts.FirstOrDefault(part => part.flightID == p.LaunchShellPartId);
            if (shell == null) {
                if (p.LaunchShellOpenedAt < 0 || !p.LaunchShellPanelsReleased || !p.LaunchShellBaseReleased)
                    throw new InvalidOperationException("Launch shell lost before verified native panel and base release.");
                var debris = FlightGlobals.Vessels.Where(other => other != null && other.loaded && other != v)
                    .SelectMany(other => other.parts).FirstOrDefault(part => part.flightID == p.LaunchShellPartId);
                if (debris != null && (debris.transform.position - v.parts.First(part => p.PartIds.Contains(part.flightID) && part.FindModuleImplementing<ModuleEngines>() != null).transform.position).magnitude < 8) {
                    if (now > p.LaunchShellOpenedAt + 30) throw new InvalidOperationException("Detached shell base has not cleared the engine; ignition blocked.");
                    p.LaunchShellClearAt = -1; p.Detail = "Waiting for aft shell base to move clear of engine"; return false;
                }
                if (p.LaunchShellClearAt < 0) p.LaunchShellClearAt = now;
                bool clear = LaunchShellPolicy.MayBurn(true, false, p.LaunchShellClearAt, now);
                if (!clear) p.Detail = "Launch shell separated; waiting for engine clearance";
                return clear;
            }
            var fairing = shell.FindModuleImplementing<ModuleProceduralFairing>();
            var decoupler = shell.FindModuleImplementing<ModuleDecouple>();
            if (fairing == null || decoupler == null) throw new InvalidOperationException("Launch shell lacks native fairing or base release hardware.");
            if (p.LaunchShellOpenedAt < 0) {
                p.Detail = "Protective shell closed; separating above 50 km after rail clearance";
                if (fairing.GetScalar > .5f) throw new InvalidOperationException("Launch shell opened before authorized separation.");
                if (LaunchShellPolicy.MayOpen(p.Phase, v.altitude, v.verticalSpeed, alongRail)) {
                    fairing.DeployFairing();
                    p.LaunchShellOpenedAt = now;
                    p.Detail = "Protective shell opening; native panel separation";
                    Debug.Log("[RailgunSandbox] Stock launch shell deployment commanded at " + v.altitude);
                }
            } else {
                p.Detail = "Separating stock launch shell panels and aft base";
                if (fairing.GetScalar > .5f) p.LaunchShellPanelsReleased = true;
                if (p.LaunchShellPanelsReleased && now >= p.LaunchShellOpenedAt + 1 && !decoupler.isDecoupled) {
                    decoupler.Decouple();
                    p.LaunchShellBaseReleased = decoupler.isDecoupled;
                }
                if (now > p.LaunchShellOpenedAt + 15) throw new InvalidOperationException("Launch shell did not physically separate; ignition blocked.");
            }
            return false;
        }
    }
}
