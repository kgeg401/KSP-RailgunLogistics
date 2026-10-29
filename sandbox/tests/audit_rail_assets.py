"""Read-only acceptance of the native rail geometry/cache probe.

The source of the measurements is Unity's instantiated meshes, not a second
Python geometry implementation. This checks the full report and repeated cache
samples instead of accepting a single success flag.
"""
from pathlib import Path
import csv
import json
import math
import re
import sys


def audit(directory):
    root = Path(directory)
    checks = {}
    result = {"success": False, "checks": checks, "physicsVerified": False}
    try:
        text = (root / "RailAssets-audit.txt").read_text(encoding="utf-8-sig")
        values = dict(line.split("=", 1) for line in text.splitlines() if "=" in line and "Mesh=" not in line)
        mesh_lines = [line for line in text.splitlines() if "Mesh=" in line]
        checks["completeNativeReport"] = values.get("success") == "True" and values.get("schema") == "2" and values.get("units") == "metres" and bool(mesh_lines)
        checks["bothRailVariantsAndLoader"] = all(any(line.startswith(prefix) for line in mesh_lines) for prefix in ("railMesh=", "loaderMesh=", "bareMesh=", "supportMesh="))
        mesh_stats = [dict(re.findall(r"(vertices|triangles|invalid|degenerate|reversed)=(\d+)", line)) for line in mesh_lines]
        checks["finiteNondegenerateOutwardGeometry"] = all(set(m) == {"vertices", "triangles", "invalid", "degenerate", "reversed"} and int(m["vertices"]) > 0 and int(m["triangles"]) > 0 and all(int(m[k]) == 0 for k in ("invalid", "degenerate", "reversed")) for m in mesh_stats)
        shell_radius, bare_radius = float(values["minimumTriangleRadius"]), float(values["bareMinimumTriangleRadius"])
        shell_bore, bare_bore = float(values["shellBore"]), float(values["bareBore"])
        checks["fullTriangleClearance"] = all(math.isfinite(n) for n in (shell_radius, bare_radius, shell_bore, bare_bore)) and abs(shell_bore - 3.85) < .001 and abs(bare_bore - 2.15) < .001 and shell_radius >= shell_bore - .001 and bare_radius >= bare_bore - .001
        checks["pinsEngageAndClear"] = values.get("lockingPinsEngageCollar") == "True" and values.get("lockingPinsClearCollar") == "True"
        checks["steadyViewerUsesCache"] = values.get("steadyViewerUsesCache") == "True"
        checks["supportRebuildReleasesOldMeshes"] = values.get("bareSupportRebuildKeepsMeshCount") == "True"
        with (root / "RailAssets-cache.tsv").open() as stream:
            samples = list(csv.DictReader(stream, delimiter="\t"))
        laps = {int(row["lap"]) for row in samples}
        checks["repeatedEndToEndCoverage"] = len(samples) >= 40 and laps == {0, 1, 2, 3} and all(any(int(r["lap"]) == lap and float(r["along"]) >= 100000 for r in samples) for lap in laps)
        checks["boundedChunkPool"] = bool(samples) and all(0 < int(r["activeChunks"]) <= 14 and int(r["activeChunks"]) + int(r["pooledChunks"]) == int(r["createdChunks"]) <= 14 for r in samples)
        checks["boundedOwnedMeshes"] = bool(samples) and all(0 < int(r["ownedMeshes"]) <= 7 for r in samples)
        warm = next(r for r in reversed(samples) if r["lap"] == "0")
        checks["noGrowthAfterWarmup"] = all(r["createdChunks"] == warm["createdChunks"] and r["ownedMeshes"] == warm["ownedMeshes"] for r in samples if int(r["lap"]) > 0)
        result.update(samples=len(samples), meshReports=len(mesh_stats), minimumShellRadius=float(values["minimumTriangleRadius"]), minimumBareRadius=float(values["bareMinimumTriangleRadius"]))
        result["success"] = all(checks.values())
    except (OSError, ValueError, KeyError, StopIteration) as error:
        result["error"] = str(error)
    return result


if __name__ == "__main__":
    report = audit(sys.argv[1])
    print(json.dumps(report, indent=2))
    raise SystemExit(0 if report["success"] else 1)
