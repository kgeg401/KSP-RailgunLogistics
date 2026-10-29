"""Read-only acceptance of an uninterrupted native orbit-to-pad recovery.

This is a return-only test, never proof of a delivery/reuse cycle. The caller
must supply the SHA-256 of the original captured post-launch native save.
Saved-descent replays are rejected even if they landed successfully.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import xml.etree.ElementTree as ET

from audit_landing import audit as audit_landing
from audit_rail_flight import nodes, fields


def clock_chain(statuses):
    times = []
    for status in statuses:
        match = re.search(r"^UT=([0-9.eE+\-]+)$", status, re.M)
        if not match:
            return False, 0.0, len(times)
        value = float(match.group(1))
        if not math.isfinite(value):
            return False, 0.0, len(times)
        times.append(value)
    valid = len(times) > 100 and all(0 <= b - a <= 6 for a, b in zip(times, times[1:]))
    return valid, times[-1] - times[0] if times else 0.0, len(times)


def initial_orbit(save_bytes, identity, expected_ids):
    matches = [v for v in nodes("VESSEL", save_bytes.decode("utf-8-sig").splitlines())
               if any(line.strip() == "fleetId = " + identity for line in v)]
    if len(matches) != 1:
        return False
    vessel = matches[0]
    parts = list(nodes("PART", vessel))
    if fields(vessel).get("sit") != "ORBITING" or len(parts) != len(expected_ids):
        return False
    if {int(fields(p)["uid"]) for p in parts} != expected_ids:
        return False
    orbit = fields(next(nodes("ORBIT", vessel)))
    sma, eccentricity = float(orbit["SMA"]), float(orbit["ECC"])
    return orbit.get("REF") == "1" and math.isfinite(sma) and math.isfinite(eccentricity) and \
        0 <= eccentricity < 1 and sma * (1 - eccentricity) - 600000 > 73000


def audit(directory, expected_initial_sha256):
    directory = Path(directory)
    report = audit_landing(directory)
    checks = report["checks"]
    # audit_landing may catch a missing/corrupt artifact after populating only
    # some checks. Its complete result must pass, not just that partial set.
    checks["completeLandingAudit"] = report["success"] is True
    report["uninterruptedOrbitToHomePad"] = False
    try:
        if not re.fullmatch(r"[0-9a-fA-F]{64}", expected_initial_sha256):
            raise ValueError("A complete captured-native-save SHA-256 is required")
        initial_bytes = (directory / "initial/persistent.sfs").read_bytes()
        digest = hashlib.sha256(initial_bytes).hexdigest()
        checks["exactCapturedInitialNativeSave"] = digest == expected_initial_sha256.lower()
        report["initialNativeSaveSHA256"] = digest
        initial = ET.parse(directory / "initial/RailgunReusableFleet.xml").getroot().find("Pods/PodRecord")
        final = ET.parse(directory / "RailgunReusableFleet.xml").getroot().find("Pods/PodRecord")
        ids = {int(n.text) for n in initial.findall("PartIds/unsignedInt")}
        checks["initialIntactKerbinOrbit"] = initial_orbit(initial_bytes, initial.findtext("VesselId"), ids)
        checks["freshReturnOnlyStart"] = initial.findtext("Phase") == "Rendezvous" and \
            initial.findtext("RecoveryTest") == "false" and initial.findtext("CargoTransferred") == "false"
        manifest = ["VesselId", "OrderId", "CargoUnits", "CargoPart", "Resource", "OriginalParts", "OwnPort", "TargetPort", "TargetAnchor"]
        checks["samePodAndCargoManifest"] = all(initial.findtext(k) == final.findtext(k) for k in manifest) and \
            ids == {int(n.text) for n in final.findall("PartIds/unsignedInt")}
        statuses = [json.loads(line)["status"] for line in (directory / "status.jsonl").read_text(encoding="utf-8-sig").splitlines()]
        continuous, seconds, samples = clock_chain(statuses)
        checks["monotonicNativeClock"] = continuous
        report.update(simulatedSeconds=seconds, statusSamples=samples)
        events = [json.loads(line) for line in (directory / "events.jsonl").read_text(encoding="utf-8-sig").splitlines()]
        commands = [event.get("value", "") for event in events if event.get("event") == "command"]
        checks["oneContinuousReturnStart"] = commands.count("reentry-test1") == 1 and \
            all(command in {"reentry-test1", "snapshot", "save-and-exit"} for command in commands) and \
            not any(event.get("event") in {"observer-error", "timeout", "bounded-stop", "process-exited-early"} or
                    (event.get("event") == "observer-start" and event.get("resume")) for event in events)
        report["uninterruptedOrbitToHomePad"] = all(checks.values())
        report["success"] = report["uninterruptedOrbitToHomePad"]
    except Exception as error:
        report["success"] = False
        report["chainError"] = str(error)
    report["fullLogisticsRoundTrip"] = False
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--initial-sha256", required=True)
    arguments = parser.parse_args()
    result = audit(arguments.directory, arguments.initial_sha256)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result["success"] else 1)
