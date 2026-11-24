"""Exercise the specified executable on a private Windows desktop."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import uuid

root = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("scenario", choices=("native", "modal", "browser", "numbers"))
parser.add_argument("--executable", type=Path, default=root.parent / "out" / "Windows Voice Access Full Numbers.exe")
parser.add_argument("--chrome", type=Path, default=Path(os.environ["ProgramFiles"]) / "Google/Chrome/Application/chrome.exe")
args = parser.parse_args()
executable = args.executable.resolve(strict=True)
trial = Path(tempfile.mkdtemp(prefix="windows-voice-access-full-numbers-test-"))
# The fixture references the assembly locally but launches the requested executable in place.
reference = trial / "Windows Voice Access Full Numbers.exe"
shutil.copy2(executable, reference)
os.environ["FWN_TEST_EXECUTABLE"] = str(executable)
framework = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319"
references = ["System.Windows.Forms.dll", "System.Drawing.dll", reference]
references += [framework / "WPF" / (lib + ".dll") for lib in ("WindowsBase", "UIAutomationClient", "UIAutomationTypes")]
subprocess.run([framework / "csc.exe", "/nologo", "/target:winexe", "/out:" + str(trial / "Fixture.exe"),
                *["/r:" + str(lib) for lib in references], root / "Fixture.cs"], cwd=trial, check=True)

class Startup(c.Structure):
    _fields_ = [("cb", w.DWORD), ("reserved", w.LPWSTR), ("desktop", w.LPWSTR), ("title", w.LPWSTR),
                ("x", w.DWORD), ("y", w.DWORD), ("xsize", w.DWORD), ("ysize", w.DWORD),
                ("xcount", w.DWORD), ("ycount", w.DWORD), ("fill", w.DWORD), ("flags", w.DWORD),
                ("show", w.WORD), ("reservedSize", w.WORD), ("reservedBytes", c.c_void_p),
                ("stdin", w.HANDLE), ("stdout", w.HANDLE), ("stderr", w.HANDLE)]
class Process(c.Structure):
    _fields_ = [("handle", w.HANDLE), ("thread", w.HANDLE), ("pid", w.DWORD), ("tid", w.DWORD)]
u = c.WinDLL("user32", use_last_error=True)
k = c.WinDLL("kernel32", use_last_error=True)
u.CreateDesktopW.argtypes = [w.LPCWSTR, w.LPCWSTR, c.c_void_p, w.DWORD, w.DWORD, c.c_void_p]
u.CreateDesktopW.restype = w.HANDLE
u.CloseDesktop.argtypes = [w.HANDLE]
k.CreateProcessW.argtypes = [w.LPCWSTR, w.LPWSTR, c.c_void_p, c.c_void_p, w.BOOL,
                            w.DWORD, c.c_void_p, w.LPCWSTR, c.POINTER(Startup), c.POINTER(Process)]
k.CloseHandle.argtypes = [w.HANDLE]
k.TerminateProcess.argtypes = [w.HANDLE,w.UINT]
k.WaitForSingleObject.argtypes = [w.HANDLE,w.DWORD]
k.GetExitCodeProcess.argtypes = [w.HANDLE,c.POINTER(w.DWORD)]
name = "WindowsVoiceAccessFullNumbersTest" + uuid.uuid4().hex[:10]
desktop = u.CreateDesktopW(name, None, None, 0, 0xf01ff, None)
if not desktop:
    raise c.WinError(c.get_last_error())
children = []
def launch(args, path=None):
    s = Startup()
    s.cb = c.sizeof(s)
    s.desktop = "winsta0\\" + name
    proc = Process()
    exe = str(path or trial / "Fixture.exe")
    cmd = c.create_unicode_buffer(subprocess.list2cmdline([exe, *args]))
    if not k.CreateProcessW(exe, cmd, None, None, False, 0, None, str(trial), c.byref(s), c.byref(proc)):
        raise c.WinError(c.get_last_error())
    k.CloseHandle(proc.thread)
    children.append(proc.handle)
    return proc.handle
passed = False
try:
    browser = args.scenario == "browser"
    if browser:
        exe = args.chrome.resolve(strict=True)
        launch(["--user-data-dir=" + str(trial / "temporary-chrome"), "--no-first-run",
                "--no-default-browser-check", "--disable-background-mode", "--disable-sync",
                "--window-position=60,100", "--window-size=850,620", "--app=" + (root / "popup-test.html").as_uri()], exe)
        time.sleep(3.5)
    else:
        launch([args.scenario])
        for _ in range(120):
            if (trial / "fixture-ready.txt").exists(): break
            time.sleep(0.05)
    choices = ("Browser background action", "Browser notification action") if browser else ("Background action", "Notification action", "cancel")
    if args.scenario == "modal": choices = ("modal-check",)
    if args.scenario == "numbers":
        choices = ("entry-digits", "entry-words", "entry-spelled", "entry-stream", "entry-prefix", "entry-manual", "entry-invalid")
    for target in choices:
        p = launch(["modal-check"] if target=="modal-check" else ["check", target])
        if k.WaitForSingleObject(p, 35000) != 0: raise RuntimeError("Checker timeout")
        exit_code = w.DWORD()
        if not k.GetExitCodeProcess(p, c.byref(exit_code)): raise c.WinError(c.get_last_error())
        if exit_code.value:
            errors = trial / "fixture-errors.txt"
            raise RuntimeError(errors.read_text(errors="replace") if errors.exists() else f"Fixture exited {exit_code.value}")
    for file in sorted(trial.glob("*.txt")): print(file.name, file.read_text(errors="replace")[:1100])
    if len((trial / "fixture-pass.txt").read_text().splitlines()) != len(choices):
        raise RuntimeError("Fixture did not complete all expected checks")
    passed = True
finally:
    # Close only windows on the disposable test desktop. The user's desktop is never switched.
    callback = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
    u.PostMessageW.argtypes = [w.HWND,w.UINT,w.WPARAM,w.LPARAM]
    def close_window(hwnd, unused):
        u.PostMessageW(hwnd, 0x10, 0, 0)
        return True
    u.EnumDesktopWindows.argtypes = [w.HANDLE, callback, w.LPARAM]
    u.EnumDesktopWindows(desktop, callback(close_window), 0)
    if children: k.WaitForSingleObject(children[0], 2000)
    for h in children:
        if k.WaitForSingleObject(h, 0) != 0: k.TerminateProcess(h, 0)
        k.WaitForSingleObject(h, 1000)
        k.CloseHandle(h)
    u.CloseDesktop(desktop)
    if passed:
        try:
            shutil.rmtree(trial)
        except OSError:
            print(f"Temporary test files remain in {trial}")
    else:
        print(f"Failure details in {trial}")
