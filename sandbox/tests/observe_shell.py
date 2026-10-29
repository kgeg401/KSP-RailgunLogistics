"""Bounded, exact-process observer for the isolated native shell flight.

Observes an already running lab. Never launches a game, changes physics, kills
a process or dispatches a pod. Only the terminal outcome requests a native save;
--exit-after explicitly requests a normal save-and-exit after evidence capture.
An interrupted/timed-out observer leaves the running game alone.
"""
import argparse
import ctypes
from ctypes import wintypes
import datetime
import hashlib
import json
from pathlib import Path
import time


def observe(args):
    root = args.game_root.resolve()
    save = root / "saves" / "Railgun-Shell-Test"
    if not save.is_dir():
        raise ValueError("The isolated Railgun-Shell-Test save does not exist")
    output = args.evidence.resolve()
    output.mkdir(parents=True, exist_ok=False)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.ReadFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
    handle = kernel.OpenProcess(0x1000 | 0x100000, False, args.pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    began = time.monotonic()

    def alive():
        return kernel.WaitForSingleObject(handle, 0) == 258

    def read(path):
        file = kernel.CreateFileW(str(path), 0x80000000, 7, None, 3, 0x80, None)
        if file == ctypes.c_void_p(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            chunks = []
            buffer, count = ctypes.create_string_buffer(65536), wintypes.DWORD()
            while True:
                if not kernel.ReadFile(file, buffer, len(buffer), ctypes.byref(count), None):
                    raise ctypes.WinError(ctypes.get_last_error())
                if count.value == 0:
                    return b"".join(chunks)
                chunks.append(buffer.raw[:count.value])
        finally:
            kernel.CloseHandle(file)

    def command(value):
        target = save / "RailgunSandbox.command"
        end = time.monotonic() + 30
        while target.exists() and alive() and time.monotonic() < end:
            time.sleep(.1)
        if target.exists() or not alive():
            raise RuntimeError("The bound game cannot accept the terminal save command")
        temporary = save / "RailgunShell-observer-command.tmp"
        temporary.write_text(value, encoding="ascii")
        temporary.rename(target)
        while target.exists() and alive() and time.monotonic() < end:
            time.sleep(.1)
        if target.exists():
            raise TimeoutError("Native command was not consumed")

    try:
        name, length = ctypes.create_unicode_buffer(32768), wintypes.DWORD(32768)
        if not kernel.QueryFullProcessImageNameW(handle, 0, name, ctypes.byref(length)):
            raise ctypes.WinError(ctypes.get_last_error())
        if Path(name.value).resolve() != (root / "KSP_x64.exe").resolve():
            raise ValueError("PID does not belong to the selected isolated game")
        last, previous_phase, next_brief = "", "", 0
        outcome = "timeout"
        while time.monotonic() - began < args.timeout_seconds:
            if not alive():
                outcome = "unexpected_exit"
                break
            try:
                status = read(save / "RailgunSandbox.status.txt").decode("utf-8-sig")
            except OSError:
                time.sleep(.25)
                continue
            elapsed = time.monotonic() - began
            if status != last:
                with (output / "status.jsonl").open("a", encoding="utf-8") as stream:
                    stream.write(json.dumps({"utc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "elapsed": elapsed, "status": status}) + "\n")
                last = status
            phase_line = next((line for line in status.splitlines() if line.startswith("Pod 1:")), "")
            phase = phase_line.split(" / ")[0]
            if elapsed >= next_brief or phase != previous_phase:
                print(json.dumps({"elapsed": round(elapsed, 1), "phase": phase_line, "error": next((line for line in status.splitlines() if line.startswith("Error=")), "missing")}), flush=True)
                previous_phase, next_brief = phase, elapsed + 45
            if phase in {"Pod 1: Lost", "Pod 1: Stranded", "Pod 1: Rendezvous"} or any(line.startswith("Error=") and line != "Error=" for line in status.splitlines()):
                error_line = next((line for line in status.splitlines() if line.startswith("Error=")), "missing")
                outcome = phase if error_line == "Error=" else "native_error"
                command("snapshot")
                time.sleep(1)
                break
            time.sleep(.5)
        names = ["persistent.sfs", "RailgunReusableFleet.xml", "RailgunSandbox.status.txt", "ShellLoading-v2.tsv", "RailLaunchEvidence-v2.tsv", "LaunchShellEvidence.tsv", "RailAssets-audit.txt", "RailAssets-cache.tsv", "LoaderMeshes-audit.txt", "LoaderClearance-audit.txt", "RailAssets-profile.txt", "RailPayload-geometry.txt", "RailCamera-state.txt", "RailAssets-diagnostics.txt"]
        for file_name in names:
            path = save / file_name
            if path.exists():
                (output / file_name).write_bytes(read(path))
        (output / "KSP.log").write_bytes(read(root / "KSP.log"))
        receipt = {"pid": args.pid, "outcome": outcome, "elapsed": time.monotonic() - began, "fullLogisticsRoundTrip": False, "multiplayerVerified": False,
                   "clientSHA256": hashlib.sha256(read(root / "GameData/RailgunLogistics/Plugins/RailgunLogistics.dll")).hexdigest()}
        (output / "observer-result.json").write_text(json.dumps(receipt, indent=2))
        print(json.dumps(receipt), flush=True)
        if args.exit_after and outcome not in {"timeout", "unexpected_exit"} and alive():
            command("save-and-exit")
    finally:
        kernel.CloseHandle(handle)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--game-root", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--timeout-seconds", type=int, default=1200)
    parser.add_argument("--exit-after", action="store_true")
    arguments = parser.parse_args()
    if not 1 <= arguments.timeout_seconds <= 1800:
        parser.error("Use a bounded capture between 1 and 1800 seconds")
    observe(arguments)
