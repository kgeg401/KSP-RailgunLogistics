"""Strict acceptance of a fresh two-client native logistics round trip.

Bundle: manifest.json, events.jsonl, status.jsonl, initial/ and final/ server
snapshots (Fleet.xml and matching Vessels/*.txt), stages/*/Fleet.xml, and
client-A/B copies of Railgun-MP-Test/roundtrip plus LandingEvidence.tsv.
Diagnostic resumes, fixture teleports, omitted stages and interrupted captures
cannot satisfy this audit. A delivery-only or landing-only pass is insufficient.
"""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import re
import xml.etree.ElementTree as ET

from audit_delivery import audit as delivery_audit, fields, module, native_parts, nodes, resources


PHASES = ["Ready", "Reserved", "RailLaunch", "Ascent", "Rendezvous", "Docking", "Unloading", "Undocking", "Returning", "Landing", "Refitting", "Ready"]
CHEATS = ["infinitePropellant", "infiniteElectricity", "ignoreTemperature", "unbreakableJoints", "noCrashDamage"]


def pods(path):
    root = ET.parse(path).getroot()
    return root.findall("Slots/FleetSlot/Pod")


def ids(pod):
    return {int(n.text) for n in pod.findall("PartIds/unsignedInt")}


def tsv(path):
    with path.open(encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream, delimiter="\t"))


def jsonl(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]


def sequence(values):
    collapsed = []
    for phase in values:
        if not collapsed or collapsed[-1] != phase:
            collapsed.append(phase)
    return collapsed == PHASES


def mission_history(first, last, witnesses):
    """Merge observed committed phases, never invent an omitted transition."""
    start, end = float(first.findtext("PhaseStarted")), float(last.findtext("PhaseStarted"))
    if not math.isfinite(start) or not math.isfinite(end) or end < start:
        raise ValueError("Invalid mission phase clock")
    unique = {}
    for pod in witnesses:
        time = float(pod.findtext("PhaseStarted"))
        if not math.isfinite(time):
            raise ValueError("Nonfinite phase witness")
        if start <= time <= end:
            key = (time, pod.findtext("Phase"), pod.findtext("OrderId"), pod.findtext("CargoTransferred"))
            unique[key] = pod
    return [unique[k] for k in sorted(unique)]


def clock(values, elapsed=None):
    if not (len(values) >= 100 and all(math.isfinite(v) for v in values) and values[-1] - values[0] >= 45 and all(0 <= b - a <= 6 for a, b in zip(values, values[1:]))):
        return False
    elapsed = [i * .5 for i in range(len(values))] if elapsed is None else elapsed
    if len(elapsed) != len(values) or not all(math.isfinite(t) for t in elapsed) or any(b < a for a, b in zip(elapsed, elapsed[1:])):
        return False
    advanced = elapsed[0]
    for index in range(1, len(values)):
        if values[index] > values[index - 1]:
            advanced = elapsed[index]
        if elapsed[index] - advanced > 6:
            return False
    return True


def native_time(status):
    # A partial filesystem read is missing evidence, never an omitted sample.
    match = re.search(r"(?m)^UT=([^\r\n]+)", status)
    try:
        return float(match.group(1)) if match else math.nan
    except ValueError:
        return math.nan


def vessel(directory, identity):
    matches = []
    for path in (directory / "Vessels").glob("*.txt"):
        text = path.read_text(encoding="utf-8-sig")
        if re.search(r"(?m)^\s*fleetId = " + re.escape(identity) + r"\s*$", text):
            matches.append(text)
    if len(matches) != 1:
        raise ValueError("Expected one original native vessel for " + identity)
    return matches[0]


def vessel_fields(text):
    lines = text.splitlines()
    return fields(next(nodes("VESSEL", lines), lines))


def home_distance(before, after):
    a, b = vessel_fields(before), vessel_fields(after)
    lat1, lat2 = math.radians(float(a["lat"])), math.radians(float(b["lat"]))
    lon = math.radians(float(b["lon"]) - float(a["lon"]))
    hav = math.sin((lat2 - lat1) / 2) ** 2 + math.cos(lat1) * math.cos(lat2) * math.sin(lon / 2) ** 2
    return 1200000 * math.asin(math.sqrt(min(1, max(0, hav))))


def audit(directory):
    directory = Path(directory)
    checks = {}
    report = {"success": False, "fullLogisticsRoundTrip": False, "checks": checks}
    try:
        manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8-sig"))
        checks["freshNativeMission"] = manifest["mode"] == "fresh-native" and manifest["diagnostic"] is False and manifest["port"] == 8817
        initial_file = directory / "initial/Fleet.xml"
        checks["pinnedInitialFleet"] = hashlib.sha256(initial_file.read_bytes()).hexdigest() == manifest["initialFleetSHA256"]
        initial, final = pods(initial_file), pods(directory / "final/Fleet.xml")
        if len(initial) != 2 or len(final) != 2:
            raise ValueError("Two stable fleet slots required")
        first, last = initial[0], final[0]
        checks["freshDispatchAndSingleCompletedFlight"] = all(p.findtext("Phase") == "Ready" and p.findtext("OrderId", "") == "" for p in initial) and last.findtext("Phase") == "Ready" and last.findtext("OrderId", "") == "" and int(last.findtext("FlightsCompleted")) == int(first.findtext("FlightsCompleted")) + 1 and last.findtext("CargoTransferred") == "true"
        checks["sameOriginalPodIdentities"] = all(a.findtext("VesselId") == b.findtext("VesselId") and ids(a) == ids(b) and len(ids(a)) == int(a.findtext("OriginalParts")) > 0 for a, b in zip(initial, final))
        checks["spareRemainsAvailable"] = final[1].findtext("Phase") == "Ready" and final[1].findtext("FlightsCompleted") == initial[1].findtext("FlightsCompleted") and final[1].findtext("OrderId", "") == "" and final[1].findtext("CargoTransferred") == initial[1].findtext("CargoTransferred")
        stages = sorted((directory / "stages").glob("*/Fleet.xml"))
        stage_pods = [pods(path)[0] for path in stages]
        # Native receipt observers preserve short server-confirmed phases that
        # filesystem polling can miss. Local proposed client state is excluded.
        epoch = ET.parse(initial_file).getroot().findtext("Epoch")
        for client in ("A", "B"):
            receipt_root = directory / ("client-" + client) / "roundtrip/authority"
            for path in sorted(receipt_root.glob("*/Fleet.xml")):
                receipt = path.parent / "receipt.txt"
                if not receipt.exists() or "source=received-server-message" not in receipt.read_text():
                    raise ValueError("Phase witness is not a captured server receipt")
                root = ET.parse(path).getroot()
                if root.findtext("Epoch") != epoch:
                    raise ValueError("Phase witness belongs to another server session")
                stage_pods.append(root.find("Slots/FleetSlot/Pod"))
        stage_pods = mission_history(first, last, stage_pods)
        checks["completeOrderedMission"] = sequence([first.findtext("Phase")] + [p.findtext("Phase") for p in stage_pods] + [last.findtext("Phase")])
        # Continue collecting bounded delivery/return results for a diagnostic
        # resume. The fresh/start/phase checks above still reject it as a full
        # trip; missing launch evidence must never become inferred evidence.
        dispatched = next((p for p in stage_pods if p.findtext("Phase") == "Reserved"), first)
        launched = next((p for p in stage_pods if p.findtext("Phase") == "RailLaunch"), first)
        cargo_part = int(launched.findtext("CargoPart"))
        order = dispatched.findtext("OrderId")
        checks["sameManifestThroughoutFlight"] = cargo_part > 0 and all(p.findtext("RecoveryTest") == "false" and ids(p) == ids(first) and all(p.findtext(k) == dispatched.findtext(k) for k in ("OrderId", "TargetAnchor", "TargetPort", "Resource", "CargoUnits", "OwnPort")) and (p.findtext("Phase") == "Reserved" or int(p.findtext("CargoPart")) == cargo_part) for p in stage_pods if p.findtext("Phase") != "Ready")
        events = jsonl(directory / "events.jsonl")
        commands = [e["value"] for e in events if e.get("event") == "command"]
        checks["noReplayRestartOrFixtureCommands"] = commands.count("dispatch1") == 1 and all(c in {"dispatch1", "watch1", "receiver", "snapshot", "save-and-exit"} or c.startswith("showcase-") for c in commands) and not any(e.get("event") in {"process-exited-early", "observer-error", "resume", "clock-change", "timeout", "bounded-stop", "native-peer-failure"} for e in events)
        samples = jsonl(directory / "status.jsonl")
        process_ids = manifest["processIds"]
        checks["sameThreeNativeProcessesThroughout"] = len(process_ids) == 3 and len(set(process_ids.values())) == 3 and bool(samples) and all(s["aliveProcesses"] == process_ids for s in samples)
        checks["bothPeersConnectedAndUpdating"] = manifest.get("helperStatusRequired") is True and bool(samples) and all(
            s.get("peerStates", {}).get(role, "").startswith("network=Running ") and 0 <= s.get("statusAgeSeconds", {}).get(role, math.inf) <= 6
            for s in samples for role in ("A", "B"))
        checks["normalNativePhysics"] = bool(samples)
        for client in ("A", "B"):
            times = []
            for sample in samples:
                status = sample["statuses"][client]
                times.append(native_time(status))
                match = re.search(r"(?m)^NativeChecks: (.*)$", status)
                flags = dict(token.split("=", 1) for token in match.group(1).split()) if match else {}
                checks["normalNativePhysics"] &= flags.get("warp") == "1" and abs(float(flags.get("fixedDt", "nan")) - .02) < 1e-7 and all(flags.get(k) == "False" for k in CHEATS)
            checks[client + "UninterruptedNativeClock"] = clock(times, [sample["elapsed"] for sample in samples])
        native = []
        orientation = []
        transactions = []
        accepted = []
        for client in ("A", "B"):
            root = directory / ("client-" + client)
            native.extend(dict(r, client=client) for r in tsv(root / "roundtrip/native.tsv"))
            orientation.extend(tsv(root / "roundtrip/orientation.tsv"))
            transactions.extend(delivery_audit(p.parent) for p in (root / "roundtrip/transactions").glob("*/transaction.tsv"))
            landing = root / "LandingEvidence.tsv"
            if landing.exists():
                accepted.extend(r for r in tsv(landing) if r["pod"] == first.findtext("VesselId") and r["recoveryTest"] == "False" and r["accepted"] == "True")
        owned = [r for r in native if r["pod"] == "1" and r["loaded"] == "True" and r["packed"] == "False" and r["pilotLease"] == "True" and r["nativeOwner"] == "True"]
        checks["originalNativeAssemblyAndThermals"] = len(owned) >= 100 and all(int(r["originalParts"]) == len(ids(first)) and 0 < float(r["heatRatio"]) < 1 and float(r["warp"]) == 1 and abs(float(r["fixedDt"]) - .02) < 1e-7 for r in owned)
        checks["nativePhysicsObservedInEveryFlightStage"] = set(PHASES[1:-1]) <= {r["phase"] for r in owned}
        pre_refit = sorted((r for r in owned if r["phase"] not in {"Ready", "Reserved", "Refitting"}), key=lambda r: float(r["ut"]))
        checks["noMidflightPropulsionRefill"] = len(pre_refit) >= 100 and all(float(b[k]) <= float(a[k]) + 1e-5 for a, b in zip(pre_refit, pre_refit[1:]) for k in ("liquidFuel", "oxidizer"))
        checks["oneConservedPhysicalDelivery"] = len(transactions) == 1 and transactions[0]["success"] and transactions[0]["order"] == order
        checks["oneStrictNativeLanding"] = len(accepted) == 1 and all(float(r["stableSeconds"]) >= 5 and 0 <= float(r["padMetres"]) <= 25 and 0 <= float(r["uprightDegrees"]) <= 8 and 0 <= float(r["surfaceSpeed"]) <= .5 and int(r["intact"]) == int(r["original"]) == len(ids(first)) and r["nativeLanded"] == "True" and float(r["warp"]) == 1 and abs(float(r["physicsDt"]) - .02) < 1e-7 for r in accepted)
        if len(accepted) == 1:
            touchdown = float(accepted[0]["ut"])
            stability = sorted((r for r in orientation if r["pod"] == "1" and touchdown - 5 <= float(r["ut"]) <= touchdown), key=lambda r: float(r["ut"]))
            checks["independentlyObservedLandingStability"] = len(stability) >= 12 and float(stability[-1]["ut"]) - float(stability[0]["ut"]) >= 4.5 and all(r["landed"] == "True" and r["body"] == "Kerbin" and 0 <= float(r["speed"]) <= .5 and 0 <= float(r["uprightDegrees"]) <= 8 for r in stability) and all(float(b["ut"]) - float(a["ut"]) <= .8 for a, b in zip(stability, stability[1:]))
        else:
            checks["independentlyObservedLandingStability"] = False
        for index, p in enumerate(final):
            text = vessel(directory / "final", p.findtext("VesselId"))
            initial_native = vessel(directory / "initial", p.findtext("VesselId"))
            parts = native_parts(text)
            root_fields = vessel_fields(text)
            checks["pod" + str(index + 1) + "PhysicallyHomeAndIntact"] = parts.keys() == ids(p) and root_fields.get("sit") == "LANDED" and root_fields.get("landed") == "True" and fields(next(nodes("ORBIT", text.splitlines()))).get("REF") == "1" and home_distance(initial_native, text) <= 25
            checks["pod" + str(index + 1) + "StartedIntactAtHome"] = native_parts(initial_native).keys() == ids(p) and vessel_fields(initial_native).get("sit") == "LANDED"
            if index == 0:
                cargo = resources(parts[cargo_part])[dispatched.findtext("Resource")]
                markers = [module(part, "RailgunFlightPod") for part in parts.values() if any(fields(n).get("name") == "RailgunFlightPod" for n in nodes("MODULE", part))]
                checks["emptyLockedCargoAndNativeReceiptPersist"] = cargo[0] == 0 and not cargo[2] and len(markers) == 1 and markers[0].get("completedOrder") == order
                propulsion = [r for part in parts.values() if fields(part).get("name") not in {"krlReusableMonoCargo", "krlReusableFuelCargo"} for name, r in resources(part).items() if name in {"LiquidFuel", "Oxidizer", "MonoPropellant", "ElectricCharge"}]
                checks["actuallyRefitted"] = bool(propulsion) and all(abs(r[0] - r[1]) < 1e-6 for r in propulsion)
        report.update(order=order, delivery=transactions, nativeSamples=len(owned), peakHeatRatio=max(float(r["heatRatio"]) for r in owned))
        report["success"] = report["fullLogisticsRoundTrip"] = all(checks.values())
    except Exception as error:
        report["error"] = str(error)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    result = audit(parser.parse_args().directory)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result["success"] else 1)
