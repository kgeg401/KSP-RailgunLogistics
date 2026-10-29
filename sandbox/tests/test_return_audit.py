"""Tests of evidence rejection rules; these do not simulate a successful KSP flight."""
import math
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from audit_return import audit, clock_chain, initial_orbit


class ReturnEvidenceTests(unittest.TestCase):
    def test_continuous_clock(self):
        valid, seconds, count = clock_chain([f"UT={1000 + 2*i}\n" for i in range(120)])
        self.assertTrue(valid)
        self.assertEqual((seconds, count), (238, 120))

    def test_rewind_rejected(self):
        rows = [f"UT={1000 + 2*i}\n" for i in range(120)]
        rows[60] = "UT=1000\n"
        self.assertFalse(clock_chain(rows)[0])

    def test_jump_rejected(self):
        rows = [f"UT={1000 + 2*i + (100 if i >= 60 else 0)}\n" for i in range(120)]
        self.assertFalse(clock_chain(rows)[0])

    def test_short_or_incomplete_log_rejected(self):
        self.assertFalse(clock_chain(["UT=1000\n"] * 100)[0])
        self.assertFalse(clock_chain(["UT=1000\n"] * 120 + ["missing timestamp"])[0])
        self.assertFalse(clock_chain(["UT=1e999\n"] * 120)[0])

    def test_descent_replay_cannot_be_initial_orbit(self):
        sample = b"VESSEL\n{\nsit = FLYING\nfleetId = test\nPART\n{\nuid = 42\n}\nORBIT\n{\nREF = 1\nSMA = 870000\nECC = 0\n}\n}\n"
        self.assertFalse(initial_orbit(sample, "test", {42}))

    def test_wrong_body_unsafe_orbit_and_identity_rejected(self):
        sample = b"VESSEL\n{\nsit = ORBITING\nfleetId = test\nPART\n{\nuid = 42\n}\nORBIT\n{\nREF = 1\nSMA = 870000\nECC = 0\n}\n}\n"
        self.assertTrue(initial_orbit(sample, "test", {42}))
        self.assertFalse(initial_orbit(sample, "other", {42}))
        self.assertFalse(initial_orbit(sample, "test", {43}))
        self.assertFalse(initial_orbit(sample.replace(b"REF = 1", b"REF = 2"), "test", {42}))
        self.assertFalse(initial_orbit(sample.replace(b"SMA = 870000", b"SMA = 670000"), "test", {42}))

    def test_partial_landing_report_never_promoted_to_full_success(self):
        sample = b"VESSEL\n{\nsit = ORBITING\nfleetId = test\nPART\n{\nuid = 42\n}\nORBIT\n{\nREF = 1\nSMA = 870000\nECC = 0\n}\n}\n"
        ledger = "<Fleet><Pods><PodRecord><VesselId>test</VesselId><PartIds><unsignedInt>42</unsignedInt></PartIds><Phase>Rendezvous</Phase><RecoveryTest>false</RecoveryTest><CargoTransferred>false</CargoTransferred></PodRecord></Pods></Fleet>"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "initial").mkdir()
            (root / "initial/persistent.sfs").write_bytes(sample)
            (root / "initial/RailgunReusableFleet.xml").write_text(ledger)
            (root / "RailgunReusableFleet.xml").write_text(ledger)
            (root / "status.jsonl").write_text("\n".join(json.dumps({"status": f"UT={1000 + i}\n"}) for i in range(120)))
            (root / "events.jsonl").write_text(json.dumps({"event": "command", "value": "reentry-test1"}))
            # All available landing checks passed, but its missing telemetry
            # caused an early error before the remaining checks were created.
            partial = {"success": False, "checks": {"nativeLanded": True}, "error": "missing telemetry"}
            with patch("audit_return.audit_landing", return_value=partial):
                result = audit(root, hashlib.sha256(sample).hexdigest())
            self.assertFalse(result["success"])
            self.assertFalse(result["uninterruptedOrbitToHomePad"])
            self.assertFalse(result["checks"]["completeLandingAudit"])
            self.assertTrue(all(value for key, value in result["checks"].items() if key != "completeLandingAudit"))


if __name__ == "__main__":
    unittest.main()
