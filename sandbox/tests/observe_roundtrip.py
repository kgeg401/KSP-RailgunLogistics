"""Collect native evidence from the explicitly started private two-client test.

This collector never creates/moves a vessel, changes fuel, changes the clock,
starts/stops a process or operates the shared server. --dispatch writes one
ordinary delivery request to the selected dispatcher (A by default). Use --diagnostic for any resumed flight
or edited initial receiver placement; those runs cannot pass full acceptance.
"""
import argparse
import csv
import ctypes
from ctypes import wintypes
import datetime
import hashlib
import json
import io
import os
from pathlib import Path
import re
import shutil
import time
import uuid
import xml.etree.ElementTree as ET

from audit_roundtrip import audit, require_fresh_depot, private_endpoint
from audit_coordinated_warp import audit as warp_audit
from shared_text import read_shared_bytes, read_shared_text
from audit_delivery import fields, module, native_parts, nodes


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def rendezvous_hold(status, identity, number=1):
    """A latched native safety hold for this exact subject, not an old peer line."""
    prefix=f"Pod {number}: Rendezvous / Rendezvous held:"
    marker=f" / id={identity} /"
    if not identity:
        return None
    for line in status.splitlines():
        if line.startswith(prefix) and marker in line:
            return line[len(f"Pod {number}: Rendezvous / "):].split(marker,1)[0]
    return None


def receiver_hardware_ready(saved, active_vessel, anchor, port):
    """A native rename cannot change the selected receiver's physical identity."""
    try:
        if not anchor or not port or anchor == port:
            return False
        root = fields(saved.splitlines())
        if uuid.UUID(root['pid']) != uuid.UUID(active_vessel) or root.get('sit') != 'ORBITING':
            return False
        orbit = list(nodes('ORBIT', saved.splitlines()))
        if len(orbit) != 1 or fields(orbit[0]).get('REF') != '1':
            return False
        parts = native_parts(saved)
        if anchor not in parts or port not in parts:
            return False
        # Do not dispatch to the pod itself or a still-joined assembly.
        if any(fields(n).get('name') == 'RailgunFlightPod' for p in parts.values() for n in nodes('MODULE', p)):
            return False
        return module(parts[port], 'ModuleDockingNode').get('state') == 'Ready'
    except (ValueError, KeyError, TypeError, AttributeError):
        return False


def original_receiver_selected(server, root, pod, now):
    """Fresh same-client native focus plus the retained original docking IDs."""
    trace = root/'Railgun-MP-Test/roundtrip/coast-warp.tsv'
    if not 0 <= now - trace.stat().st_mtime <= 3:
        return False
    raw = read_shared_bytes(trace, maximum=128*1024*1024)
    if not raw.endswith(b'\n'):
        return False
    lines = raw.decode('utf-8-sig').splitlines()
    if len(lines) < 2:
        return False
    try:
        latest = next(csv.DictReader(io.StringIO(lines[0]+'\n'+lines[-1]+'\n'), delimiter='\t'))
        selected = str(uuid.UUID(latest['activeVessel']))
        saved = read_shared_text(server/'Universe/Vessels'/(selected+'.txt'))
        return receiver_hardware_ready(saved, selected, int(pod.findtext('TargetAnchor', '0')), int(pod.findtext('TargetPort', '0')))
    except (ValueError, KeyError, StopIteration):
        return False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for flag in ("server", "client-a", "client-b", "output"):
        parser.add_argument("--" + flag, required=True, type=Path)
    for flag in ("server-pid", "a-pid", "b-pid"):
        parser.add_argument("--" + flag, required=True, type=int)
    parser.add_argument("--private-root", type=Path, default=Path("D:/GAMES/KSP-Railgun-Previews"))
    parser.add_argument("--port",type=int,choices=(8817,8818),default=8817,help="Explicit private loopback server port recorded in the run manifest")
    parser.add_argument("--minutes", type=int, default=120)
    parser.add_argument("--diagnostic", action="store_true")
    parser.add_argument("--dispatch", action="store_true")
    parser.add_argument("--dispatcher", choices=("A","B"), default="A")
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
        fleet = read_shared_bytes(args.server / "Universe/RailgunReusable/Fleet.xml")
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
    if not args.diagnostic:
        require_fresh_depot(args.output / "initial", first_pods)
    hashes = {role: hashlib.sha256((root / "GameData/RailgunLogistics/Plugins/RailgunLogistics.dll").read_bytes()).hexdigest() for role, root in roots.items()}
    if len(set(hashes.values())) != 1:
        raise ValueError("Both native clients must use the same candidate")
    probe_hashes={role:hashlib.sha256((root/"GameData/RailgunMPValidation/Plugins/RailgunMultiplayer.Validation.dll").read_bytes()).hexdigest() for role,root in roots.items()}
    if len(set(probe_hashes.values()))!=1:raise ValueError("Native evidence probes differ between peers")
    manifest = dict(mode="diagnostic" if args.diagnostic else "fresh-native", diagnostic=args.diagnostic, port=args.port,
                    processIds=pids, clientSHA256=hashes, startedUtc=utc(), initialFleetSHA256=hashlib.sha256(initial).hexdigest(), helperStatusRequired=True, dispatcher=args.dispatcher)
    connection=read_shared_bytes(args.server/"Config/ConnectionSettings.xml")
    (args.output/"server-connection.xml").write_bytes(connection)
    manifest["serverConnectionSHA256"]=hashlib.sha256(connection).hexdigest()
    manifest["probeSHA256"]=probe_hashes
    manifest["coastTraceSchema"]="native-coast-v2"
    counter=ctypes.c_longlong();frequency=ctypes.c_longlong()
    clock=ctypes.WinDLL('kernel32',use_last_error=True)
    clock.QueryPerformanceCounter.argtypes=[ctypes.POINTER(ctypes.c_longlong)]
    clock.QueryPerformanceFrequency.argtypes=[ctypes.POINTER(ctypes.c_longlong)]
    if not clock.QueryPerformanceCounter(ctypes.byref(counter)) or not clock.QueryPerformanceFrequency(ctypes.byref(frequency)) or counter.value<=0 or frequency.value<=0:
        raise OSError('Native monotonic mission boundary unavailable')
    manifest['coastClockSchema']='same-host-qpc-v1'
    manifest['captureStartQpcTicks']=counter.value
    manifest['captureQpcFrequency']=frequency.value
    if not private_endpoint(args.output,manifest):raise ValueError("Actual pinned server configuration does not match the private endpoint")
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
    guidance_held_since = None
    try:
        while time.monotonic() - start < args.minutes * 60:
            if (args.output / "stop.request").exists():
                event("bounded-stop", reason="Explicit observer stop requested")
                outcome = "operator-stop"
                break
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
                fleet = ET.fromstring(read_shared_bytes(args.server / "Universe/RailgunReusable/Fleet.xml"))
                current = fleet.findall("Slots/FleetSlot/Pod")
                statuses = {role: read_shared_text(root / "saves/LunaMultiplayer/RailgunSandbox.status.txt") for role, root in roots.items()}
                peer_states = {role: read_shared_text(root / "Railgun-MP-Test/status.txt") for role, root in roots.items()}
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
                if not dispatched and current[0].findtext('Phase') != 'Ready' and current[0].findtext('OrderId'):
                    # A separately authorized ordinary request may have been made
                    # outside this collector. Never issue another when it returns.
                    dispatched = True
                    event('existing-order-observed', order=current[0].findtext('OrderId'))
                if args.dispatch and not dispatched and all(p[0] == "Ready" for p in state):
                    selected = "Active=Railgun Multiplayer Test Station" in statuses[args.dispatcher]
                    # Completed deliveries retain their original target hardware even
                    # when native undocking has assigned the receiver a new name.
                    if not selected and current[0].findtext('FlightsCompleted') != '0':
                        selected = original_receiver_selected(args.server, roots[args.dispatcher], current[0], time.time())
                    if "SharedAuthority: ready=True" in statuses[args.dispatcher] and status_age[args.dispatcher] <= 3 and selected:
                        command = roots[args.dispatcher] / "saves/LunaMultiplayer/RailgunSandbox.command"
                        if command.exists():
                            raise ValueError("An unconsumed command is already present")
                        with command.open('x', encoding='ascii') as stream:
                            stream.write('dispatch1')
                        dispatched = True
                        event("command", client=args.dispatcher, value="dispatch1")
                if record["elapsed"] >= next_brief:
                    print(json.dumps(dict(elapsed=round(record["elapsed"], 1), phases=state,
                        detail={role: re.findall(r"(?m)^Pod 1: (.*)$", status) for role, status in statuses.items()}, peerStates=peer_states, statusAgeSeconds=status_age)), flush=True)
                    next_brief = record["elapsed"] + 45
                if any(p[0] in {"Lost", "Stranded"} for p in state):
                    outcome = "native-failure"
                    break
                holds={role:rendezvous_hold(statuses[role],current[0].findtext("VesselId")) for role in roots if status_age[role]<6}
                holds={role:reason for role,reason in holds.items() if reason}
                if current[0].findtext("Phase")=="Rendezvous" and holds:
                    if guidance_held_since is None:guidance_held_since=time.monotonic()
                    if time.monotonic()-guidance_held_since>=5:
                        event("native-guidance-held",reasons=holds,identity=current[0].findtext("VesselId"))
                        outcome="native-guidance-held"
                        break
                else:guidance_held_since=None
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
        warp_result=warp_audit(args.output)
        (args.output/"coordinated-warp-audit.json").write_text(json.dumps(warp_result,indent=2),encoding="utf-8")
        result["coordinatedNativeWarpEvidenceVerified"]=warp_result["coordinatedNativeWarpEvidenceVerified"]
        result["strictOneXRoundTripVerified"]=result["fullLogisticsRoundTrip"]
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
