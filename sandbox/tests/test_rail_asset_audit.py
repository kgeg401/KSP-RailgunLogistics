"""Acceptance-reader regressions against a retained native geometry probe."""
from pathlib import Path
import shutil
import re
import tempfile
import unittest
from audit_rail_assets import audit


class RailAssetEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        fixture = Path(__file__).parent / "native" / "rail-assets"
        for source in fixture.iterdir():
            shutil.copy2(source, self.root / source.name)

    def tearDown(self):
        self.temporary.cleanup()

    def change(self, name, before, after):
        path = self.root / name
        text = path.read_text()
        self.assertIn(before, text)
        path.write_text(text.replace(before, after))

    def test_complete_native_probe_passes(self):
        report = audit(self.root)
        self.assertTrue(report["success"], report)
        self.assertFalse(report["physicsVerified"])

    def test_face_intrusion_fails_even_with_success_flag(self):
        path = self.root / "RailAssets-audit.txt"
        path.write_text(re.sub(r"bareMinimumTriangleRadius=[^\n]+", "bareMinimumTriangleRadius=2.05", path.read_text()))
        self.assertFalse(audit(self.root)["checks"]["fullTriangleClearance"])

    def test_invalid_radius_fails(self):
        self.change("RailAssets-audit.txt", "minimumTriangleRadius=3.8499999046325684", "minimumTriangleRadius=Infinity")
        self.assertFalse(audit(self.root)["checks"]["fullTriangleClearance"])

    def test_reversed_surface_fails(self):
        self.change("RailAssets-audit.txt", "reversed=0", "reversed=1")
        self.assertFalse(audit(self.root)["checks"]["finiteNondegenerateOutwardGeometry"])

    def test_false_steady_cache_fails(self):
        self.change("RailAssets-audit.txt", "steadyViewerUsesCache=True", "steadyViewerUsesCache=False")
        self.assertFalse(audit(self.root)["checks"]["steadyViewerUsesCache"])

    def test_growing_pool_fails(self):
        path = self.root / "RailAssets-cache.tsv"
        lines = path.read_text().splitlines()
        fields = lines[-1].split("\t")
        fields[3], fields[4] = str(15 - int(fields[2])), "15"
        lines[-1] = "\t".join(fields)
        path.write_text("\n".join(lines) + "\n")
        self.assertFalse(audit(self.root)["checks"]["boundedChunkPool"])
        self.assertFalse(audit(self.root)["checks"]["noGrowthAfterWarmup"])

    def test_truncated_cache_probe_fails(self):
        path = self.root / "RailAssets-cache.tsv"
        path.write_text("\n".join(path.read_text().splitlines()[:11]) + "\n")
        self.assertFalse(audit(self.root)["checks"]["repeatedEndToEndCoverage"])


if __name__ == "__main__":
    unittest.main()
