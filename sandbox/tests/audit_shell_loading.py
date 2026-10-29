"""Read-only native shell loading/rail-release acceptance. No orbital/return claim."""
from pathlib import Path
import csv
import json
import sys
import xml.etree.ElementTree as ET
from audit_rail_flight import nodes, fields


def audit(directory):
    root = Path(directory)
    checks = {}
    report = {"success": False, "checks": checks, "fullLogisticsRoundTrip": False, "multiplayerVerified": False}
    try:
        with (root / "ShellLoading-v2.tsv").open() as f:
            rows = list(csv.DictReader(f, delimiter="\t"))
        stages = ["Gripping", "Lifting", "Traversing", "Tilting", "Seating", "Docked"]
        order = [stages.index(r["transfer"]) for r in rows]
        checks["completeMeasuredTransfer"] = set(stages) == {r["transfer"] for r in rows}
        checks["transferOrder"] = order == sorted(order)
        checks["uprightLiftBeforeTraverse"] = all(float(r["y"]) >= 49.65 for r in rows if r["transfer"] == "Traversing")
        checks["tiltOverRail"] = all(abs(float(r["x"])) < .35 and abs(float(r["z"])) < .35 for r in rows if r["transfer"] == "Tilting")
        checks["carrierArrivesBeforeGrip"] = all(r["positioned"] == "True" for r in rows if r["transfer"] == "Gripping" and float(r["grip"]) > 0)
        checks["shellHeldDuringTransfer"] = all(float(r["grip"]) == 1 for r in rows if r["transfer"] != "Gripping")
        sequence = {r["stage"] for r in rows}
        checks["completeShellHandoff"] = {"Capturing", "Clamping", "Locking", "ReleasingCarrier", "RetractingCarrier", "Charging", "Unlocking", "Clearing", "Ready"}.issubset(sequence)
        handoff = [r for r in rows if r["stage"] in ("ReleasingCarrier", "RetractingCarrier")]
        checks["railLocksHoldDuringHandoff"] = bool(handoff) and all(float(r["clamps"]) == 1 and float(r["pins"]) == 1 for r in handoff)
        checks["chargeAfterCarrierClear"] = all(float(r["retraction"]) == 1 and float(r["carrierGrip"]) == 0 for r in rows if float(r["charge"]) > 0)
        ready = [r for r in rows if r["launchClear"] == "True"]
        checks["hardwareClearBeforeLaunch"] = bool(ready) and all(float(r["elapsed"]) >= 21 and float(r["carrierGrip"]) == 0 and float(r["retraction"]) == 1 and float(r["clamps"]) == 0 and float(r["pins"]) == 0 and r["seated"] == "True" for r in ready)
        checks["thirtyNativePartsDuringLoading"] = all(int(r["parts"]) == 30 for r in rows)
        with (root / "RailLaunchEvidence-v2.tsv").open() as f:
            flight = list(csv.DictReader(f, delimiter="\t"))
        loading_and_rail = [r for r in flight if r["phase"] in ("Reserved", "RailLaunch")]
        checks["unpoweredLoadingAndRailRelease"] = bool(loading_and_rail) and all(float(r["thrustKN"]) == 0 and r["rcs"] == "False" and float(r["liquidFuel"]) == 5760 and float(r["oxidizer"]) == 7040 for r in loading_and_rail)
        rail = [r for r in flight if r["phase"] == "RailLaunch"]
        checks["actualRailDeparture"] = bool(rail) and max(float(r["alongRail"]) for r in rail) > 100 and max(float(r["surfaceSpeed"]) for r in rail) > 50
        checks["normalPhysicsAndThermalSurvival"] = all(int(r["parts"]) == 30 and abs(float(r["fixedDt"]) - .02) < 1e-7 and float(r["heatRatio"]) < 1 for r in loading_and_rail)
        pod = ET.parse(root / "RailgunReusableFleet.xml").getroot().find("Pods/PodRecord")
        ids = {int(n.text) for n in pod.findall("PartIds/unsignedInt")}
        lines = (root / "persistent.sfs").read_text(encoding="utf-8-sig").splitlines()
        vessel = next(v for v in nodes("VESSEL", lines) if any(l.strip() == "fleetId = " + pod.findtext("VesselId") for l in v))
        actual = {int(fields(p)["uid"]) for p in nodes("PART", vessel)}
        # The recording may continue through the separately tested, intentional
        # atmospheric fairing release. Never count that temporary part as a
        # missing reusable pod part or waive an original part identity check.
        released = pod.findtext("LaunchShellBaseReleased") == "true" and pod.findtext("LaunchShellPanelsReleased") == "true"
        expected = ids if released else ids | {int(pod.findtext("LaunchShellPartId"))}
        checks["originalPodAndShellIdentity"] = len(ids) == 29 and actual == expected
        checks["noDeliveryCredit"] = pod.findtext("CargoTransferred") == "false" and pod.findtext("FlightsCompleted") == "0"
        report["samples"] = len(rows)
        report["firstReadyElapsed"] = float(ready[0]["elapsed"]) if ready else None
        report["transferHeightMetres"] = max(float(r["y"]) for r in rows)
        report["success"] = all(checks.values())
    except (OSError, ValueError, KeyError, StopIteration, TypeError, ET.ParseError) as error:
        report["error"] = str(error)
    return report


if __name__ == "__main__":
    result = audit(sys.argv[1])
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result["success"] else 1)
