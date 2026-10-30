"""Collect native evidence from the explicitly started private two-client test.

This collector never creates/moves a vessel, changes fuel, changes the clock,
starts/stops a process or operates the shared server. --dispatch writes one
ordinary delivery request to client A. Use --diagnostic for any resumed flight
or edited initial receiver placement; those runs cannot pass full acceptance.
"""
import argparse
import ctypes
from ctypes import wintypes
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import time
import xml.etree.ElementTree as ET

from audit_roundtrip import audit


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for flag in ("server", "client-a", "client-b", "output"):
        parser.add_argument("--" + flag, required=True, type=Path)
    for flag in ("server-pid", "a-pid", "b-pid"):
        parser.add_argument("--" + flag, required=True, type=int)
    parser.add_argument("--private-root", type=Path, default=Path("D:/GAMES/KSP-Railgun-Previews"))
    parser.add_argument("--minutes", type=int, default=120)
    parser.add_argument("--diagnostic", action="store_true")
    parser.add_argument("--dispatch", action="store_true")
    parser.add_argument("--until-returning", action="store_true", help="Diagnostic only: finish ten seconds after acknowledged undocking")
    args = parser.parse_args()
    if os.name != "nt" or not 1 <= args.minutes <= 240:
        parser.error("Use Windows and a bounded observation of 1 to 240 minutes")
    if args.until_returning and not args.diagnostic:
        parser.error("A delivery-only stop cannot be used for full-trip acceptance")
    private = args.private_root.resolve()
    for path in (args.server, args.client_a, args.client_b, args.output):
        if not path.resolve().is_relative_to(private) or path.resolve() == private:
            parser.error("Every test path must be inside the explicit private test root")
    pids = {"server": args.server_pid, "A": args.a_pid, "B": args.b_pid}
    if len(set(pids.values())) != 3:
        parser.error("Three distinct native processes are required")
    roots = {"A": args.client_a, "B": args.client_b}
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handles = {}
    for role, pid in pids.items():
        handle = kernel.OpenProcess(0x1000, False, pid)
        if not handle:
            parser.error("Cannot inspect native " + role + " process")
        handles[role] = handle
        size = wintypes.DWORD(32768)
        text = ctypes.create_unicode_buffer(size.value)
        if not kernel.QueryFullProcessImageNameW(handle, 0, text, ctypes.byref(size)):
            parser.error("Cannot verify native process path")
        expected = args.server / "Server.exe" if role == "server" else roots[role] / "KSP_x64.exe"
        if Path(text.value).resolve() != expected.resolve():
            parser.error("The process does not belong to the specified private game/server")
    args.output.mkdir(parents=True, exist_ok=False)

    def snapshot(destination):
        destination.mkdir()
        (destination / "Vessels").mkdir()
        fleet = (args.server / "Universe/RailgunReusable/Fleet.xml").read_bytes()
        ET.fromstring(fleet)
        (destination / "Fleet.xml").write_bytes(fleet)
        for path in (args.server / "Universe/Vessels").glob("*.txt"):
            shutil.copy2(path, destination / "Vessels" / path.name)
        return fleet

    def event(kind, **data):
        with (args.output / "events.jsonl").open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(dict(utc=utc(), event=kind, **data)) + "\n")

    initial = snapshot(args.output / "initial")
    first_pods = ET.fromstring(initial).findall("Slots/FleetSlot/Pod")
    if not args.diagnostic and (len(first_pods) != 2 or any(p.findtext("Phase") != "Ready" or p.findtext("OrderId", "") for p in first_pods)):
        raise ValueError("Fresh acceptance must begin before dispatch with both pods Ready")
    hashes = {role: hashlib.sha256((root / "GameData/RailgunLogistics/Plugins/RailgunLogistics.dll").read_bytes()).hexdigest() for role, root in roots.items()}
    if len(set(hashes.values())) != 1:
        raise ValueError("Both native clients must use the same candidate")
    manifest = dict(mode="diagnostic" if args.diagnostic else "fresh-native", diagnostic=args.diagnostic, port=8817,
                    processIds=pids, clientSHA256=hashes, startedUtc=utc(), initialFleetSHA256=hashlib.sha256(initial).hexdigest(), helperStatusRequired=True)
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    event("observer-start", diagnostic=args.diagnostic, processIds=pids)
    (args.output / "stages").mkdir()
    start = time.monotonic()
    previous = None
    count = 0
    next_brief = 0
    dispatched = False
    outcome = "timeout"
    finished_at = None
    returned_at = None
    reported_peer_failures = set()
    try:
        while time.monotonic() - start < args.minutes * 60:
            alive = {}
            for role, handle in handles.items():
                code = wintypes.DWORD()
                if kernel.GetExitCodeProcess(handle, ctypes.byref(code)) and code.value == 259:
                    alive[role] = pids[role]
            if alive != pids:
                event("process-exited-early", aliveProcesses=alive)
                outcome = "interrupted"
                break
            try:
                fleet = ET.parse(args.server / "Universe/RailgunReusable/Fleet.xml").getroot()
                current = fleet.findall("Slots/FleetSlot/Pod")
                statuses = {role: (root / "saves/LunaMultiplayer/RailgunSandbox.status.txt").read_text(encoding="utf-8-sig") for role, root in roots.items()}
                peer_states = {role: (root / "Railgun-MP-Test/status.txt").read_text(encoding="utf-8-sig") for role, root in roots.items()}
                status_age = {role: max(0, time.time() - (root / "saves/LunaMultiplayer/RailgunSandbox.status.txt").stat().st_mtime) for role, root in roots.items()}
                for role in roots:
                    if (not peer_states[role].startswith("network=Running ") or status_age[role] > 6) and role not in reported_peer_failures:
                        event("native-peer-failure", client=role, nativeStatus=peer_states[role], statusAgeSeconds=status_age[role])
                        reported_peer_failures.add(role)
                state = tuple((p.findtext("Phase"), p.findtext("CargoTransferred"), p.findtext("FlightsCompleted")) for p in current)
                record = dict(utc=utc(), elapsed=time.monotonic() - start, aliveProcesses=alive, statuses=statuses, phases=state, peerStates=peer_states, statusAgeSeconds=status_age)
                with (args.output / "status.jsonl").open("a", encoding="utf-8") as stream:
                    stream.write(json.dumps(record) + "\n")
                if state != previous:
                    snapshot(args.output / "stages" / (str(count).zfill(4) + "-" + "-".join(p[0] for p in state)))
                    count += 1
                    event("phase", phases=state)
                    previous = state
                if args.dispatch and not dispatched and all(p[0] == "Ready" for p in state):
                    if "SharedAuthority: ready=True" in statuses["A"] and "Active=Railgun Multiplayer Test Station" in statuses["A"]:
                        command = args.client_a / "saves/LunaMultiplayer/RailgunSandbox.command"
                        if command.exists():
                            raise ValueError("An unconsumed command is already present")
                        event("command", client="A", value="dispatch1")
                        command.write_text("dispatch1", encoding="ascii")
                        dispatched = True
                if record["elapsed"] >= next_brief:
                    print(json.dumps(dict(elapsed=round(record["elapsed"], 1), phases=state,
                        detail={role: re.findall(r"(?m)^Pod 1: (.*)$", status) for role, status in statuses.items()}, peerStates=peer_states, statusAgeSeconds=status_age)), flush=True)
                    next_brief = record["elapsed"] + 45
                if any(p[0] in {"Lost", "Stranded"} for p in state):
                    outcome = "native-failure"
                    break
                if args.until_returning and current[0].findtext("CargoTransferred") == "true" and current[0].findtext("Phase") in {"Returning", "Landing"}:
                    if returned_at is None:
                        returned_at = time.monotonic()
                    if time.monotonic() - returned_at >= 10:
                        event("bounded-stop", reason="diagnostic undocking observed")
                        outcome = "diagnostic-undocking-observed"
                        break
                if int(current[0].findtext("FlightsCompleted")) > int(first_pods[0].findtext("FlightsCompleted")):
                    if finished_at is None:
                        finished_at = time.monotonic()
                    if time.monotonic() - finished_at >= 4:
                        outcome = "native-completion-awaiting-audit"
                        break
            except (OSError, ET.ParseError) as error:
                event("read-gap", error=str(error))
            # Undocking can legitimately last only one guidance tick. Sample
            # faster than that tick instead of delaying the physical departure
            # merely to make the evidence collector see it.
            time.sleep(.1)
        if outcome == "timeout":
            event("timeout")
        snapshot(args.output / "final")
        for role, root in roots.items():
            destination = args.output / ("client-" + role)
            destination.mkdir()
            shutil.copytree(root / "Railgun-MP-Test/roundtrip", destination / "roundtrip")
            for name in ("LandingEvidence.tsv", "RecoveryTelemetry.tsv", "AirbrakeEvidence.tsv", "AirbrakeEvidence-v2.tsv", "RailgunSandbox.status.txt"):
                path = root / "saves/LunaMultiplayer" / name
                if path.exists():
                    shutil.copy2(path, destination / name)
            shutil.copy2(root / "KSP.log", destination / "KSP.log")
        result = audit(args.output)
        result.update(outcome=outcome, diagnostic=args.diagnostic, durationSeconds=time.monotonic() - start)
        if args.diagnostic:
            result["success"] = result["fullLogisticsRoundTrip"] = False
        (args.output / "acceptance.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(json.dumps(result, indent=2), flush=True)
    except Exception as error:
        event("observer-error", error=str(error))
        raise
    finally:
        for handle in handles.values():
            kernel.CloseHandle(handle)


if __name__ == "__main__":
    main()
