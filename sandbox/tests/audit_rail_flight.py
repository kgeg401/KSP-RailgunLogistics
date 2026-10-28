"""Read-only acceptance audit for one isolated native rail-only flight.

Usage: python audit_rail_flight.py <evidence directory>
Requires paired final save/ledger, complete launch telemetry and status.jsonl.
It does not establish docking, delivery, home landing or multiplayer behavior.
"""
from pathlib import Path
import csv
import json
import math
import re
import sys
import xml.etree.ElementTree as ET


def nodes(kind, lines):
    for start, line in enumerate(lines):
        if line.strip() != kind or start + 1 >= len(lines) or lines[start + 1].strip() != "{":
            continue
        depth, end = 1, start + 2
        while depth and end < len(lines):
            depth += lines[end].count("{") - lines[end].count("}")
            end += 1
        if depth:
            raise ValueError("Incomplete native node")
        yield lines[start + 2:end - 1]


def fields(lines):
    result, depth = {}, 0
    for line in lines:
        if depth == 0 and " = " in line:
            key, value = line.split("=", 1)
            result[key.strip()] = value.strip()
        depth += line.count("{") - line.count("}")
    return result


def audit(directory):
    directory = Path(directory)
    checks = {}
    report = {"success": False, "checks": checks, "fullLogisticsRoundTrip": False}
    try:
        pod = ET.parse(directory / "RailgunReusableFleet.xml").getroot().find("Pods/PodRecord")
        expected = {int(item.text) for item in pod.findall("PartIds/unsignedInt")}
        report["pod"] = pod.findtext("VesselId")
        checks["railOnlyNativeOrbit"] = pod.findtext("RailOnlyAscent") == "true" and pod.findtext("Phase") == "Rendezvous"
        checks["separateCircularizationBurn"] = pod.findtext("CircularizationStarted") == "true" and 0 < float(pod.findtext("CircularizationDeltaV")) <= 800
        checks["noDeliveryCredit"] = pod.findtext("CargoTransferred") == "false" and pod.findtext("FlightsCompleted") == "0"
        lines = (directory / "persistent.sfs").read_text(encoding="utf-8-sig").splitlines()
        vessels = [v for v in nodes("VESSEL", lines) if any(l.strip() == "fleetId = " + report["pod"] for l in v)]
        checks["oneOriginalPhysicalPod"] = len(vessels) == 1
        if len(vessels) == 1:
            parts = list(nodes("PART", vessels[0]))
            actual = {int(fields(part)["uid"]) for part in parts}
            checks["allOriginalPartIds"] = actual == expected and len(parts) == int(pod.findtext("OriginalParts")) > 0
            orbit = fields(next(nodes("ORBIT", vessels[0])))
            sma, ecc = float(orbit["SMA"]), float(orbit["ECC"])
            report["periapsisMetres"] = sma * (1 - ecc) - 600000
            report["apoapsisMetres"] = sma * (1 + ecc) - 600000
            checks["stableKerbinOrbit"] = orbit["REF"] == "1" and math.isfinite(sma) and 0 <= ecc < 1 and report["periapsisMetres"] > 73000
            resources = [fields(resource) for part in parts if fields(part).get("name") == "krlReusableMonoCargo" for resource in nodes("RESOURCE", part)]
            cargo = [r for r in resources if r.get("name") == "MonoPropellant"]
            checks["lockedCargoPreserved"] = len(cargo) == 1 and float(cargo[0]["amount"]) == float(pod.findtext("CargoUnits")) > 0 and cargo[0]["flowState"].lower() == "false"
        trace = directory / "RailLaunchEvidence-v2.tsv"
        if not trace.exists():
            trace = directory / "RailLaunchEvidence.tsv"
        rows = list(csv.DictReader(trace.open(), delimiter="\t"))
        preburn = [r for r in rows if r["circularizationStarted"] == "False"]
        burn = [r for r in rows if float(r["thrustKN"]) > 0]
        checks["observedRailAndCoast"] = {"RailLaunch", "Ascent"}.issubset({r["phase"] for r in preburn})
        if "guideAcceleration" in rows[0]:
            checks["guideForceEndsAtDeparture"] = all(float(r["guideAcceleration"]) == 0 for r in rows if r["phase"] == "Ascent") and any(float(r["guideAcceleration"]) > 0 for r in rows if r["phase"] == "RailLaunch")
        checks["zeroThrustAndRcsBeforeBurn"] = bool(preburn) and all(float(r["thrustKN"]) == 0 and r["rcs"] == "False" for r in preburn)
        checks["fuelUnchangedBeforeBurn"] = bool(preburn) and all(float(r["liquidFuel"]) == float(preburn[0]["liquidFuel"]) and float(r["oxidizer"]) == float(preburn[0]["oxidizer"]) for r in preburn)
        checks["burnOnlyAboveAtmosphere"] = bool(burn) and all(float(r["altitude"]) > 71000 for r in burn)
        checks["nativePartsAndThermalsThroughout"] = bool(rows) and all(int(r["parts"]) == len(expected) and 0 < float(r["heatRatio"]) < 1 and abs(float(r["fixedDt"]) - .02) < 1e-7 for r in rows)
        report["peakHeatRatio"] = max(float(r["heatRatio"]) for r in rows)
        report["muzzleSpeedMetresPerSecond"] = float(next(r for r in rows if r["phase"] == "Ascent")["surfaceSpeed"])
        flags = [re.search(r"^NativeChecks: (.*)$", json.loads(line)["status"], re.M) for line in (directory / "status.jsonl").read_text().splitlines()]
        flags = [dict(token.split("=", 1) for token in match.group(1).split()) for match in flags if match]
        cheats = ["infinitePropellant", "infiniteElectricity", "ignoreTemperature", "unbreakableJoints", "noCrashDamage"]
        checks["normalNativePhysics"] = bool(flags) and all(float(f["warp"]) == 1 and abs(float(f["fixedDt"]) - .02) < 1e-7 and all(f[k] == "False" for k in cheats) for f in flags)
        report["success"] = all(checks.values())
    except Exception as error:
        report["error"] = str(error)
    return report


if __name__ == "__main__":
    result = audit(sys.argv[1])
    print(json.dumps(result, indent=2))
    sys.exit(0 if result["success"] else 1)
