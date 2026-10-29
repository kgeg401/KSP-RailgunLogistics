"""Read-only native shell/ascent audit. Does not establish a delivery or Luna flight."""
from pathlib import Path
import csv
import json
import math
import re
import sys
import xml.etree.ElementTree as ET
from audit_rail_flight import nodes, fields


def audit(directory):
    directory = Path(directory)
    checks = {}
    report = {"success": False, "checks": checks, "fullLogisticsRoundTrip": False, "multiplayerVerified": False}
    try:
        pod = ET.parse(directory / "RailgunReusableFleet.xml").getroot().find("Pods/PodRecord")
        ids = {int(n.text) for n in pod.findall("PartIds/unsignedInt")}
        shell_id = int(pod.findtext("LaunchShellPartId"))
        opened = float(pod.findtext("LaunchShellOpenedAt"))
        clear = float(pod.findtext("LaunchShellClearAt"))
        checks["temporaryShellNotReusablePodPart"] = shell_id not in ids and len(ids) == 29
        checks["nativeSeparationAcknowledged"] = pod.findtext("LaunchShellPanelsReleased") == "true" and pod.findtext("LaunchShellBaseReleased") == "true" and 0 <= opened <= clear
        lines = (directory / "persistent.sfs").read_text(encoding="utf-8-sig").splitlines()
        vessels = [v for v in nodes("VESSEL", lines) if any(line.strip() == "fleetId = " + pod.findtext("VesselId") for line in v)]
        checks["oneOriginalPod"] = len(vessels) == 1
        if len(vessels) == 1:
            parts = list(nodes("PART", vessels[0]))
            checks["original29PartsPreserved"] = {int(fields(p)["uid"]) for p in parts} == ids
            orbit = fields(next(nodes("ORBIT", vessels[0])))
            sma, ecc = float(orbit["SMA"]), float(orbit["ECC"])
            report["periapsisMetres"] = sma * (1 - ecc) - 600000
            report["apoapsisMetres"] = sma * (1 + ecc) - 600000
            checks["nativeStableOrbit"] = orbit["REF"] == "1" and math.isfinite(sma) and 0 <= ecc < 1 and report["periapsisMetres"] > 73000
        with (directory / "RailLaunchEvidence-v2.tsv").open() as f:
            trace = list(csv.DictReader(f, delimiter="\t"))
        checks["completeLaunchPhases"] = {"Reserved", "RailLaunch", "Ascent"}.issubset({r["phase"] for r in trace})
        checks["continuousPhysicsEvidence"] = len(trace) > 500 and all(0 < float(r["fixedDt"]) <= .04 for r in trace)
        checks["noEarlyPropulsion"] = all(float(r["thrustKN"]) == 0 and r["rcs"] == "False" and float(r["liquidFuel"]) == 5760 and float(r["oxidizer"]) == 7040 for r in trace if r["circularizationStarted"] == "False")
        release = min(trace, key=lambda r: abs(float(r["ut"]) - opened))
        report["separationAltitudeMetres"] = float(release["altitude"])
        checks["requestedAltitudeAndMuzzleClearance"] = report["separationAltitudeMetres"] >= 50000 and float(release["alongRail"]) >= 100000
        burning = [r for r in trace if float(r["thrustKN"]) > 0]
        checks["realCircularizationAfterSeparation"] = bool(burning) and all(float(r["ut"]) >= clear + 3 and float(r["altitude"]) > 71000 and int(r["parts"]) == 29 for r in burning)
        checks["realFuelConsumed"] = any(float(r["liquidFuel"]) < 5760 for r in burning)
        checks["thermalAndAssemblySurvival"] = bool(trace) and all(int(r["parts"]) in (29, 30) and 0 < float(r["heatRatio"]) < 1 for r in trace)
        report["peakHeatRatio"] = max(float(r["heatRatio"]) for r in trace)
        checks["guideForceEndsAtDeparture"] = all(float(r["guideAcceleration"]) == 0 for r in trace if r["phase"] == "Ascent")
        checks["circularizationWithinBudget"] = pod.findtext("CircularizationStarted") == "true" and 0 < float(pod.findtext("CircularizationDeltaV")) <= 800
        statuses = [json.loads(line)["status"] for line in (directory / "status.jsonl").read_text().splitlines()]
        flags = [re.search(r"^NativeChecks: (.*)$", s, re.M) for s in statuses]
        flags = [dict(t.split("=", 1) for t in m.group(1).split()) for m in flags if m]
        checks["normalNativePhysics"] = bool(flags) and all(float(f["warp"]) == 1 and abs(float(f["fixedDt"]) - .02) < 1e-7 and all(f[k] == "False" for k in ["infinitePropellant", "infiniteElectricity", "ignoreTemperature", "unbreakableJoints", "noCrashDamage"]) for f in flags)
        checks["all29PartsNativelyShieldedBeforeLaunch"] = any("attached=True scalar=0" in s and "shielded=29/29" in s and "Pod 1: Reserved" in s for s in statuses)
        checks["noDeliveryOrReturnCredit"] = pod.findtext("CargoTransferred") == "false" and pod.findtext("FlightsCompleted") == "0"
        shell_trace = directory / "LaunchShellEvidence.tsv"
        report["detailedShellTracePresent"] = shell_trace.exists()
        if shell_trace.exists():
            with shell_trace.open() as f:
                shell = list(csv.DictReader(f, delimiter="\t"))
            checks["detailedTracePreservesOriginalAssembly"] = bool(shell) and all(int(r["originalPartsPresent"]) == 29 for r in shell)
            checks["detailedClosedShielding"] = any(r["attached"] == "True" and r["fairingScalar"] == "0" and r["shieldedOriginalParts"] == "29" for r in shell)
            checks["detailedSeparatedClearance"] = any(r["attached"] == "False" and r["ignitionPermitted"] == "True" for r in shell)
        report["success"] = all(checks.values())
    except (OSError, ValueError, KeyError, StopIteration, TypeError, ET.ParseError) as error:
        report["error"] = str(error)
    return report


if __name__ == "__main__":
    result = audit(sys.argv[1])
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result["success"] else 1)
