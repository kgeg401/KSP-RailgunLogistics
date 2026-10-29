"""Bounded Windows native-test observer. No vessel/physics edits or process kills.

Only explicit isolated Railgun-Reentry-Test / Railgun-RailOnly-Test saves are
controlled. A process handle binds the run to the exact existing game instance.
"""
import argparse
import ctypes
from ctypes import wintypes
import datetime
import json
import os
from pathlib import Path
import re
import time


def run(arguments):
    root = arguments.game_root.resolve()
    out = arguments.evidence.resolve()
    save = root / "saves" / ("Railgun-Reentry-Test" if arguments.mode == "return" else "Railgun-RailOnly-Test")
    if not save.is_dir() or not (root / "KSP_x64.exe").is_file():
        raise ValueError("Select an existing KSP root with the isolated native fixture")
    out.mkdir(parents=True, exist_ok=True)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.ReadFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
    process = kernel.OpenProcess(0x1000 | 0x100000, False, arguments.pid)
    if not process:
        raise ctypes.WinError(ctypes.get_last_error())
    start = time.monotonic()
    began = time.time()
    started = arguments.resume
    outcome = ""
    interrupted = False
    latest = ""
    last_fresh = None
    captured = set()
    if arguments.resume:
        captured = {int(path.name[6:]) for path in out.glob("below-*") if path.name[6:].isdigit()}

    def event(name, **values):
        record = {"utc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "elapsed": round(time.monotonic() - start, 2), "event": name, **values}
        with (out / "events.jsonl").open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(record) + "\n")
        print(json.dumps(record), flush=True)

    def alive():
        return kernel.WaitForSingleObject(process, 0) == 258

    def shared_bytes(path):
        handle = kernel.CreateFileW(str(path), 0x80000000, 7, None, 3, 0x80, None)
        if handle == ctypes.c_void_p(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            chunks = []
            buffer = ctypes.create_string_buffer(65536)
            count = wintypes.DWORD()
            while True:
                if not kernel.ReadFile(handle, buffer, len(buffer), ctypes.byref(count), None):
                    raise ctypes.WinError(ctypes.get_last_error())
                if not count.value:
                    return b"".join(chunks)
                chunks.append(buffer.raw[:count.value])
        finally:
            kernel.CloseHandle(handle)

    def copy_evidence(destination):
        destination.mkdir(parents=True, exist_ok=True)
        for name in ["persistent.sfs", "RailgunReusableFleet.xml", "RailgunSandbox.status.txt", "RecoveryTelemetry.tsv", "LandingEvidence.tsv", "RailLaunchEvidence-v2.tsv"]:
            path = save / name
            if path.exists():
                for attempt in range(30):
                    try:
                        (destination / name).write_bytes(shared_bytes(path))
                        break
                    except OSError:
                        if attempt == 29:
                            raise
                        time.sleep(.05)

    def command(value):
        path = save / "RailgunSandbox.command"
        deadline = time.monotonic() + 20
        while path.exists() and alive() and time.monotonic() < deadline:
            time.sleep(.1)
        if not alive():
            raise RuntimeError("The bound test game exited before the command")
        if path.exists():
            raise TimeoutError("An earlier native command has not completed")
        temporary = save / ("RailgunSandbox-observer-" + str(os.getpid()) + ".tmp")
        temporary.write_text(value, encoding="ascii")
        temporary.rename(path)
        while path.exists() and alive() and time.monotonic() < deadline:
            time.sleep(.1)
        if path.exists():
            raise TimeoutError(value)
        event("command", value=value)

    def checkpoint(name):
        command("snapshot")
        time.sleep(.5)
        copy_evidence(out / name)
        event("checkpoint", checkpoint=name)

    try:
        image = ctypes.create_unicode_buffer(32768)
        length = wintypes.DWORD(len(image))
        if not kernel.QueryFullProcessImageNameW(process, 0, image, ctypes.byref(length)):
            raise ctypes.WinError(ctypes.get_last_error())
        if Path(image.value).resolve() != (root / "KSP_x64.exe").resolve():
            raise ValueError("The supplied PID does not belong to this exact KSP root")
        event("observer-start", pid=arguments.pid, mode=arguments.mode, resume=arguments.resume)
        next_report = 0
        while time.monotonic() - start < arguments.timeout:
            if not alive():
                interrupted = True
                event("process-exited-early", status=latest)
                break
            status_file = save / "RailgunSandbox.status.txt"
            if status_file.exists() and status_file.stat().st_mtime >= began - 10:
                status = shared_bytes(status_file).decode("utf-8-sig")
                if status != latest:
                    latest = status
                    last_fresh = time.monotonic()
                    with (out / "status.jsonl").open("a", encoding="utf-8") as stream:
                        stream.write(json.dumps({"elapsed": time.monotonic() - start, "status": status}) + "\n")
                if not started and arguments.mode == "return" and "Pod 1: Rendezvous" in status and "sit=ORBITING" in status and "packed=False parts=29" in status:
                    command("reentry-test1")
                    started = True
                if not started and arguments.mode == "launch" and "Pod 1:" in status:
                    if "Active=KRL Reusable Pod 1" not in status:
                        command("watch1")
                    started = True
                final_phases = ["Lost", "Stranded", "RecoveryTestComplete"] + (["Rendezvous"] if arguments.mode == "launch" else [])
                if started and any("Pod 1: " + phase in status for phase in final_phases):
                    outcome = next(phase for phase in final_phases if "Pod 1: " + phase in status)
                    event("outcome", phase=outcome, status=status)
                    checkpoint("outcome")
                    command("save-and-exit")
                    for _ in range(200):
                        if not alive():
                            break
                        time.sleep(.1)
                    break
                if started and arguments.mode == "return":
                    match = re.search(r"vessel=KRL Reusable Pod 1 .*? alt=([0-9.eE+\-]+)", status)
                    if match and "vz=-" in status:
                        altitude = float(match.group(1))
                        for height in [90000, 15000, 7000, 1000, 200]:
                            if altitude < height and height not in captured:
                                captured.add(height)
                                checkpoint("below-" + str(height))
            if time.monotonic() >= next_report:
                brief = " | ".join(line for line in latest.splitlines() if line.startswith(("Pod 1:", "  vessel=KRL Reusable Pod 1", "Error=")) or "LiquidFuel:" in line)
                event("progress", status=brief[:1600], freshSecondsAgo=None if last_fresh is None else round(time.monotonic() - last_fresh, 1))
                next_report = time.monotonic() + 45
            if (out / "stop.request").exists():
                interrupted = True
                event("bounded-stop")
                break
            time.sleep(.4)
        else:
            interrupted = True
            event("timeout")
        if interrupted and started and alive():
            checkpoint("stopped")
            command("save-and-exit")
            for _ in range(200):
                if not alive():
                    break
                time.sleep(.1)
    except Exception as error:
        interrupted = True
        event("observer-error", error=str(error))
    finally:
        copy_evidence(out)
        (out / "run-summary.json").write_text(json.dumps({"pid": arguments.pid, "mode": arguments.mode, "resume": arguments.resume, "outcome": outcome, "interrupted": interrupted, "gameStillRunning": alive(), "seconds": time.monotonic() - start}, indent=2), encoding="utf-8")
        kernel.CloseHandle(process)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-root", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--mode", choices=["return", "launch"], required=True)
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--timeout", type=int, default=3000)
    parsed = parser.parse_args()
    if parsed.timeout < 1 or parsed.timeout > 3600:
        parser.error("Timeout must be within this one-hour run")
    run(parsed)
