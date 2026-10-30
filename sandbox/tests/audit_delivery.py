"""Read-only audit of one physical cargo handoff, not a full-trip acceptance.

Consumes the test-only CargoTransferProbe's immediate native before/after
snapshots. No live tanks, port state, or saved receipt is modified by this audit.
"""
import argparse
import csv
import json
import math
import struct
from pathlib import Path
import xml.etree.ElementTree as ET

from audit_rail_flight import fields, nodes


def native_parts(text):
    parts = list(nodes("PART", text.splitlines()))
    keyed = {int(fields(p)["uid"]): p for p in parts}
    if not parts or len(keyed) != len(parts):
        raise ValueError("Missing or duplicate native part identities")
    return keyed


def resources(part):
    result = {}
    for node in nodes("RESOURCE", part):
        item = fields(node)
        name = item["name"]
        if name in result:
            raise ValueError("Duplicate resource on a native part")
        amount, capacity = float(item["amount"]), float(item["maxAmount"])
        # KSP can serialize a double amount against a float32-quantized capacity:
        # native 82.0125 amount / 82.012496948242188 max is a measured example.
        # Admit at most one float32 capacity ULP; cargo deltas still use 1e-6.
        rounding = 0.0
        if math.isfinite(capacity) and 0 <= capacity < 3.4e38:
            bits = struct.unpack("!I", struct.pack("!f", capacity))[0]
            rounding = max(0.0, struct.unpack("!f", struct.pack("!I", bits + 1))[0] - capacity)
        if not all(math.isfinite(v) for v in (amount, capacity)) or capacity < 0 or not 0 <= amount <= capacity + rounding:
            raise ValueError("Invalid native resource amount")
        result[name] = (amount, capacity, item["flowState"].lower() == "true")
    return result


def module(part, name):
    matches = [fields(n) for n in nodes("MODULE", part) if fields(n).get("name") == name]
    if len(matches) != 1:
        raise ValueError("Expected one native " + name)
    return matches[0]


def audit(directory):
    directory = Path(directory)
    checks = {}
    report = {"success": False, "checks": checks, "fullLogisticsRoundTrip": False}
    try:
        with (directory / "transaction.tsv").open(encoding="utf-8-sig") as stream:
            rows = list(csv.DictReader(stream, delimiter="\t"))
        if len(rows) != 1:
            raise ValueError("Expected exactly one transaction record")
        row = rows[0]
        # Older private probes wrote a UTF-16 declaration into a UTF-8 file.
        # Decode that legacy recorder format without changing the captured bytes.
        xml = (directory / "fleet.xml").read_bytes()
        root = ET.fromstring(xml if xml.startswith((b"\xff\xfe", b"\xfe\xff")) else xml.decode("utf-8-sig"))
        pods = root.findall("Pods/PodRecord") or root.findall("Slots/FleetSlot/Pod")
        pod = next(p for p in pods if p.findtext("Number") == row["pod"])
        ids = {int(n.text) for n in pod.findall("PartIds/unsignedInt")}
        before = native_parts((directory / "before.txt").read_text(encoding="utf-8-sig"))
        after = native_parts((directory / "after.txt").read_text(encoding="utf-8-sig"))
        source, own, target, anchor = [int(pod.findtext(k)) for k in ("CargoPart", "OwnPort", "TargetPort", "TargetAnchor")]
        order, resource = pod.findtext("OrderId"), pod.findtext("Resource")
        units = float(pod.findtext("CargoUnits"))
        checks["validManifest"] = bool(order) and row["order"] == order and row["resource"] == resource and int(row["source"]) == source and math.isfinite(units) and units > 0
        checks["originalPartsRetained"] = len(ids) == int(pod.findtext("OriginalParts")) > 0 and ids <= before.keys() and before.keys() == after.keys()
        checks["receiverIsSeparateAssembly"] = anchor in before and anchor not in ids and target in before and target not in ids and own in ids
        for label, parts in (("before", before), ("after", after)):
            a, b = module(parts[own], "ModuleDockingNode"), module(parts[target], "ModuleDockingNode")
            checks[label + "ReciprocalNativeDock"] = a.get("state", "").startswith("Docked") and b.get("state", "").startswith("Docked") and int(a["dockUId"]) == target and int(b["dockUId"]) == own
        old = {uid: resources(p) for uid, p in before.items()}
        new = {uid: resources(p) for uid, p in after.items()}
        a, b = old[source][resource], new[source][resource]
        checks["lockedManifestRemovedExactlyOnce"] = not a[2] and not b[2] and a[1] == b[1] and abs(a[0] - units) < 1e-6 and abs(b[0]) < 1e-6
        receiver_delta = 0.0
        unchanged = True
        for uid in before:
            if old[uid].keys() != new[uid].keys():
                unchanged = False
                continue
            for name, prior in old[uid].items():
                final = new[uid][name]
                delta = final[0] - prior[0]
                unchanged &= prior[1:] == final[1:]
                if uid not in ids and name == resource:
                    receiver_delta += delta
                    unchanged &= delta >= -1e-6 and (delta <= 1e-6 or prior[2])
                elif uid != source or name != resource:
                    unchanged &= abs(delta) < 1e-6
        checks["conservedInActualReceiverTanks"] = abs(receiver_delta - units) < 1e-6
        checks["noUnrelatedResourceChanges"] = bool(unchanged)
        markers_before = [fields(m) for p in before.values() for m in nodes("MODULE", p) if fields(m).get("name") == "RailgunFlightPod" and fields(m).get("fleetId") == pod.findtext("VesselId")]
        markers_after = [fields(m) for p in after.values() for m in nodes("MODULE", p) if fields(m).get("name") == "RailgunFlightPod" and fields(m).get("fleetId") == pod.findtext("VesselId")]
        checks["newNativeReceiptPairedWithDrainedTank"] = len(markers_before) == len(markers_after) == 1 and markers_before[0].get("completedOrder", "") != order and markers_after[0].get("completedOrder") == order
        checks["noTransferException"] = not row["exception"]
        report.update(order=order, resource=resource, units=units, nativeReceiverIncrease=receiver_delta, success=all(checks.values()))
    except Exception as error:
        report["error"] = str(error)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    result = audit(parser.parse_args().directory)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result["success"] else 1)
