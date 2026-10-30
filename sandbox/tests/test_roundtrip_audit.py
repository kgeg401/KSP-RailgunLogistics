import math
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
from audit_roundtrip import PHASES, audit, clock, mission_history, native_time, sequence


class RoundTripAuditTests(unittest.TestCase):
    def test_full_order_with_repeated_samples(self):
        self.assertTrue(sequence([p for p in PHASES for _ in range(3)]))

    def test_ready_counter_alone_is_not_a_trip(self):
        self.assertFalse(sequence(["Ready", "Ready"]))

    def test_saved_orbit_resume_is_not_complete(self):
        self.assertFalse(sequence(PHASES[4:]))

    def test_every_stage_is_required(self):
        for index in range(1, len(PHASES) - 1):
            self.assertFalse(sequence(PHASES[:index] + PHASES[index + 1:]), PHASES[index])

    def test_backwards_or_duplicate_flight_rejected(self):
        self.assertFalse(sequence(PHASES[:6] + ["Rendezvous"] + PHASES[6:]))
        self.assertFalse(sequence(PHASES + PHASES))

    def test_clock_requires_a_continuous_finite_capture(self):
        self.assertTrue(clock([i * .5 for i in range(100)]))
        self.assertFalse(clock([0, 1]))
        self.assertFalse(clock([100] * 120))
        for tail in (-1, 60, math.nan, math.inf):
            self.assertFalse(clock([i * .5 for i in range(100)] + [tail]))

    def test_initial_clock_progress_cannot_hide_a_later_stall(self):
        values = [i * .5 for i in range(100)] + [49.5] * 40
        self.assertFalse(clock(values))
        elapsed = [i * .1 for i in range(600)]
        self.assertTrue(clock([math.floor(t / 2) * 2 for t in elapsed], elapsed))
        self.assertFalse(clock([math.floor(t / 2) * 2 if t < 50 else 48 for t in elapsed], elapsed))

    def test_partial_status_read_is_rejected_without_dropping_sample(self):
        self.assertEqual(123.5, native_time("status\nUT=123.5\r\nError=\r\n"))
        for status in ("", "UT=", "UT=broken"):
            times = [i * .5 for i in range(100)]
            times[50] = native_time(status)
            self.assertFalse(clock(times))

    def test_missing_evidence_cannot_be_accepted(self):
        with tempfile.TemporaryDirectory() as root:
            result = audit(Path(root))
            self.assertFalse(result["success"])
            self.assertFalse(result["fullLogisticsRoundTrip"])
            self.assertIn("error", result)

    def test_actual_phase_receipt_can_fill_polling_gap(self):
        def pod(index):
            return ET.fromstring(f"<Pod><Phase>{PHASES[index]}</Phase><PhaseStarted>{index}</PhaseStarted><OrderId>order</OrderId><CargoTransferred>false</CargoTransferred></Pod>")
        witnesses = [pod(i) for i in range(len(PHASES))]
        polled = [p for i, p in enumerate(witnesses) if i != 7]
        self.assertFalse(sequence([p.findtext("Phase") for p in mission_history(witnesses[0], witnesses[-1], polled)]))
        merged = mission_history(witnesses[0], witnesses[-1], polled + witnesses + list(reversed(witnesses)))
        self.assertTrue(sequence([p.findtext("Phase") for p in merged]))

    def test_phase_receipts_cannot_extend_capture_clock(self):
        first = ET.fromstring("<Pod><Phase>Ready</Phase><PhaseStarted>10</PhaseStarted></Pod>")
        last = ET.fromstring("<Pod><Phase>Ready</Phase><PhaseStarted>20</PhaseStarted></Pod>")
        outside = ET.fromstring("<Pod><Phase>Reserved</Phase><PhaseStarted>9</PhaseStarted></Pod>")
        self.assertEqual([], mission_history(first, last, [outside]))
        outside.find("PhaseStarted").text = "NaN"
        with self.assertRaises(ValueError):
            mission_history(first, last, [outside])


if __name__ == "__main__":
    unittest.main()
