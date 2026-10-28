"""Read-only acceptance of native recovery evidence; never credits a delivery.

Usage: python audit_landing.py EVIDENCE_DIRECTORY
The caller must distinguish a saved-descent replay from an uninterrupted return.
"""
import csv
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
from audit_rail_flight import nodes, fields


def audit(directory):
    directory = Path(directory)
    checks = {}
    report = {"success": False, "checks": checks, "fullLogisticsRoundTrip": False}
    try:
        pod = ET.parse(directory / "RailgunReusableFleet.xml").getroot().find("Pods/PodRecord")
        ids = {int(n.text) for n in pod.findall("PartIds/unsignedInt")}
        report["runId"] = pod.findtext("RecoveryTestRunId")
        checks["nativeRecoveryAccepted"] = pod.findtext("Phase") == "RecoveryTestComplete"
        checks["noDeliveryOrReuseCredit"] = pod.findtext("RecoveryTest") == "true" and pod.findtext("CargoTransferred") == "false" and pod.findtext("FlightsCompleted") == "0"
        lines = (directory / "persistent.sfs").read_text(encoding="utf-8-sig").splitlines()
        vessels = [v for v in nodes("VESSEL", lines) if any(l.strip() == "fleetId = " + pod.findtext("VesselId") for l in v)]
        checks["oneOriginalPhysicalPod"] = len(vessels) == 1
        if len(vessels) == 1:
            vessel = vessels[0]
            parts = list(nodes("PART", vessel))
            checks["allOriginalPartIds"] = {int(fields(p)["uid"]) for p in parts} == ids and len(parts) == int(pod.findtext("OriginalParts")) > 0
            checks["nativeLanded"] = fields(vessel).get("sit") == "LANDED" and fields(vessel).get("landed") == "True"
            cargo = [fields(r) for p in parts if fields(p).get("name") == "krlReusableMonoCargo" for r in nodes("RESOURCE", p) if fields(r).get("name") == "MonoPropellant"]
            checks["lockedCargoPreserved"] = len(cargo) == 1 and float(cargo[0]["amount"]) == float(pod.findtext("CargoUnits")) > 0 and cargo[0]["flowState"].lower() == "false"
        rows = list(csv.DictReader((directory / "LandingEvidence.tsv").open(), delimiter="\t"))
        accepted = [r for r in rows if r["runId"] == report["runId"] and r["pod"] == pod.findtext("VesselId") and r["accepted"] == "True"]
        checks["oneMatchingAcceptance"] = len(accepted) == 1
        if len(accepted) == 1:
            row = accepted[0]
            report.update({"padMetres": float(row["padMetres"]), "uprightDegrees": float(row["uprightDegrees"]), "surfaceSpeed": float(row["surfaceSpeed"]), "stableSeconds": float(row["stableSeconds"])})
            checks["strictLandingEnvelope"] = float(row["stableSeconds"]) >= 5 and 0 <= report["padMetres"] <= 25 and 0 <= report["uprightDegrees"] <= 8 and 0 <= report["surfaceSpeed"] <= .5 and row["nativeLanded"] == "True" and int(row["intact"]) == int(row["original"]) == len(ids) and float(row["warp"]) == 1 and abs(float(row["physicsDt"]) - .02) < 1e-7
        trace = list(csv.DictReader((directory / "RecoveryTelemetry.tsv").open(), delimiter="\t"))
        checks["intactAndWithinThermalLimitsThroughout"] = bool(trace) and all(int(r["parts"]) == len(ids) and 0 < float(r["heatRatio"]) < 1 and abs(float(r["physicsDt"]) - .02) < 1e-7 for r in trace)
        report["peakHeatRatio"] = max(float(r["heatRatio"]) for r in trace)
        flags = []
        fuel = []
        for line in (directory / "status.jsonl").read_text().splitlines():
            status = json.loads(line)["status"]
            match = re.search(r"^NativeChecks: (.*)$", status, re.M)
            if match:
                flags.append(dict(t.split("=", 1) for t in match.group(1).split()))
            # First pod only; the second pod may be an unloaded fixture.
            first = status.split("Pod 2:")[0]
            amount = re.search(r"LiquidFuel: propulsion=([0-9.eE+\-]+)", first)
            if amount:
                fuel.append(float(amount.group(1)))
        cheats = ["infinitePropellant", "infiniteElectricity", "ignoreTemperature", "unbreakableJoints", "noCrashDamage"]
        checks["normalNativePhysics"] = bool(flags) and all(float(f["warp"]) == 1 and abs(float(f["fixedDt"]) - .02) < 1e-7 and all(f[k] == "False" for k in cheats) for f in flags)
        checks["noPropulsionRefill"] = len(fuel) > 1 and all(b <= a + 1e-6 for a, b in zip(fuel, fuel[1:]))
        report["success"] = all(checks.values())
    except Exception as error:
        report["error"] = str(error)
    return report


if __name__ == "__main__":
    result = audit(sys.argv[1])
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["success"] else 1)
