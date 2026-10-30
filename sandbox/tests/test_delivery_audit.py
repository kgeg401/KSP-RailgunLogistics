"""Reject false cargo credit, wrong docks, duplicate identities and reused receipts."""
import tempfile
import unittest
from pathlib import Path
from audit_delivery import audit


def tank(amount, locked=False):
    return f"RESOURCE\n{{\nname = MonoPropellant\namount = {amount}\nmaxAmount = 200\nflowState = {str(not locked)}\n}}\n"


def part(uid, body):
    return f"PART\n{{\nuid = {uid}\n{body}}}\n"


def port(partner):
    return f"MODULE\n{{\nname = ModuleDockingNode\nstate = Docked (docker)\ndockUId = {partner}\n}}\n"


def native(cargo, receiver, receipt=""):
    return (part(1, tank(cargo, True)) + part(2, port(3)) + part(3, port(2)) + part(4, tank(receiver)) +
            part(5, "MODULE\n{\nname = RailgunFlightPod\nfleetId = original\ncompletedOrder = " + receipt + "\n}\n" + tank(60)))


class DeliveryAuditTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        (self.root / "fleet.xml").write_text("""<FleetState><Pods><PodRecord>
<Number>1</Number><VesselId>original</VesselId><OrderId>order-a</OrderId><Resource>MonoPropellant</Resource>
<CargoUnits>100</CargoUnits><CargoPart>1</CargoPart><OwnPort>2</OwnPort><TargetPort>3</TargetPort><TargetAnchor>4</TargetAnchor>
<OriginalParts>3</OriginalParts><PartIds><unsignedInt>1</unsignedInt><unsignedInt>2</unsignedInt><unsignedInt>5</unsignedInt></PartIds>
</PodRecord></Pods></FleetState>""")
        (self.root / "before.txt").write_text(native(100, 20))
        (self.root / "after.txt").write_text(native(0, 120, "order-a"))
        (self.root / "transaction.tsv").write_text("ut\tpod\torder\tresource\tsource\tcargoBefore\tcargoAfter\treceiverBefore\treceiverAfter\texception\n10\t1\torder-a\tMonoPropellant\t1\t100\t0\t20\t120\t\n")

    def tearDown(self):
        self.tmp.cleanup()

    def change(self, name, old, new):
        path = self.root / name
        path.write_text(path.read_text().replace(old, new))

    def test_conserved_native_transfer_passes_but_is_not_full_trip(self):
        result = audit(self.root)
        self.assertTrue(result["success"], result)
        self.assertFalse(result["fullLogisticsRoundTrip"])

    def test_wrong_port_rejected(self):
        self.change("before.txt", "dockUId = 3", "dockUId = 9")
        self.assertFalse(audit(self.root)["success"])

    def test_no_physical_join_rejected(self):
        self.change("after.txt", "state = Docked (docker)", "state = Ready")
        self.assertFalse(audit(self.root)["success"])

    def test_resource_created_without_draining_rejected(self):
        (self.root / "after.txt").write_text(native(100, 120, "order-a"))
        self.assertFalse(audit(self.root)["success"])

    def test_partial_or_excess_transfer_rejected(self):
        for amount in (119, 121):
            (self.root / "after.txt").write_text(native(0, amount, "order-a"))
            self.assertFalse(audit(self.root)["success"])

    def test_propulsion_used_as_cargo_rejected(self):
        self.change("after.txt", "amount = 60", "amount = 59")
        self.assertFalse(audit(self.root)["success"])

    def test_reused_receipt_rejected(self):
        self.change("before.txt", "completedOrder = \n", "completedOrder = order-a\n")
        self.assertFalse(audit(self.root)["success"])

    def test_duplicate_native_identity_rejected(self):
        with (self.root / "after.txt").open("a") as stream:
            stream.write(part(1, tank(0, True)))
        self.assertFalse(audit(self.root)["success"])

    def test_wrong_manifest_rejected(self):
        self.change("transaction.tsv", "order-a", "order-b")
        self.assertFalse(audit(self.root)["success"])

    def test_native_float32_capacity_rounding_is_not_cargo_creation(self):
        for name in ("before.txt", "after.txt"):
            self.change(name, "amount = 60\nmaxAmount = 200", "amount = 82.0125\nmaxAmount = 82.012496948242188")
        self.assertTrue(audit(self.root)["success"])

    def test_real_native_overfill_is_rejected(self):
        for name in ("before.txt", "after.txt"):
            self.change(name, "amount = 60\nmaxAmount = 200", "amount = 82.02\nmaxAmount = 82.012496948242188")
        self.assertFalse(audit(self.root)["success"])

    def test_legacy_recorder_xml_declaration_can_be_decoded(self):
        path = self.root / "fleet.xml"
        path.write_text('<?xml version="1.0" encoding="utf-16"?>' + path.read_text(), encoding="utf-8")
        self.assertTrue(audit(self.root)["success"])

    def test_transient_exception_is_not_success(self):
        self.change("transaction.tsv", "\t120\t\n", "\t120\tSystem.IO.IOException\n")
        self.assertFalse(audit(self.root)["success"])

    def test_incomplete_bundle_is_not_success(self):
        (self.root / "after.txt").unlink()
        self.assertFalse(audit(self.root)["success"])


if __name__ == "__main__":
    unittest.main()
