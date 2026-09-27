"""Exercise duplicate launches against the isolated QA instance; no mouse or clipboard use."""
import ctypes as C
from ctypes import wintypes as W
import json
import desktop_qa as q

q.u.IsIconic.argtypes = [W.HWND]
q.k.OpenProcess.argtypes = [W.DWORD, W.BOOL, W.DWORD]
q.k.OpenProcess.restype = W.HANDLE
q.k.WaitForSingleObject.argtypes = [W.HANDLE, W.DWORD]
q.k.GetExitCodeProcess.argtypes = [W.HANDLE, C.POINTER(W.DWORD)]
q.k.TerminateProcess.argtypes = [W.HANDLE, W.UINT]
q.k.CloseHandle.argtypes = [W.HANDLE]


receiver_for = q.receiver_for


def main():
    previous = q.u.GetForegroundWindow()
    processes = []
    results = []
    primary = fixture = None
    aw = None

    def check(name, passed):
        if not passed:
            raise AssertionError(name)
        results.append(name)
        print("PASS:", name, flush=True)

    def launch(args=()):
        process = q.start(q.BIN / "PointCursor.exe", args)
        processes.append(process)
        return process

    def activate():
        # Launch from the foreground fixture, just as Explorer would. The child
        # must hand its foreground permission to the resident before activating it.
        q.foreground(fw)
        q.key(0x1B); q.key(0x1B, True)  # Dismiss Alt menu mode in the synthetic fixture.
        q.time.sleep(.15)  # Let the fixture's synthetic activation input drain.
        pid = q.u.SendMessageW(fw, 0x8004, 0, 0)
        handle = q.k.OpenProcess(0x101001, False, pid)
        if not handle:
            raise C.WinError(C.get_last_error())
        try:
            if q.k.WaitForSingleObject(handle, 5000) != 0:
                q.k.TerminateProcess(handle, 1)
                raise AssertionError("Duplicate did not exit; a modal prompt may be open")
            code = W.DWORD()
            q.k.GetExitCodeProcess(handle, C.byref(code))
            check("duplicate exits without modal prompt", code.value == 0)
        finally:
            q.k.CloseHandle(handle)
        hwnd = q.wait(lambda: q.window_for(primary.pid), 5)
        try:
            q.wait(lambda: q.u.GetForegroundWindow() == hwnd and not q.u.IsIconic(hwnd))
        except AssertionError:
            foreground = q.u.GetForegroundWindow()
            print("DIAGNOSTIC:", "expected", hwnd, q.text(hwnd), q.cls(hwnd),
                  "actual", foreground, q.text(foreground), q.cls(foreground), flush=True)
            raise
        return hwnd

    try:
        fixture = q.start(q.BIN / "PointCursor.Fixture.exe")
        fw = q.wait(lambda: q.window_for(fixture.pid, True))
        primary = launch(("--quiet",))
        q.wait(lambda: receiver_for(primary.pid))
        check("quiet startup exposes no settings or prompt", not q.window_for(primary.pid))

        aw = activate()
        check("duplicate opens and focuses quiet resident", bool(aw))
        q.u.PostMessageW(aw, 0x10, 0, 0)
        q.wait(lambda: not q.u.IsWindowVisible(aw))
        check("close hides settings while resident stays alive", primary.poll() is None)
        aw = activate()
        check("duplicate restores and focuses hidden settings", bool(aw))

        q.u.ShowWindow(aw, 6)
        q.wait(lambda: q.u.IsIconic(aw))
        check("duplicate restores minimized settings", activate() == aw)
        check("duplicate brings visible background settings forward", activate() == aw)

        for _ in range(5):
            activate()
        visible_forms = []
        for hwnd in q.windows():
            owner = W.DWORD()
            q.u.GetWindowThreadProcessId(hwnd, C.byref(owner))
            if owner.value == primary.pid and q.u.IsWindowVisible(hwnd) and q.text(hwnd).startswith("PointCursor"):
                visible_forms.append(hwnd)
        check("repeated launches preserve one resident and one settings form",
              primary.poll() is None and visible_forms == [aw])
        exit_button = next(h for h in q.windows(aw) if q.text(h) == "退出程序")
        q.u.SendMessageW(exit_button, 0xF5, 0, 0)
        primary.wait(timeout=5)
        check("activated resident exits normally", primary.returncode == 0)

        # Two launches before the receiver exists must elect one owner and wake it.
        cold = [launch(("--quiet",)), launch(("--quiet",))]
        q.wait(lambda: sum(p.poll() is None for p in cold) == 1, 6)
        survivor = next(p for p in cold if p.poll() is None)
        check("simultaneous duplicate exits cleanly", all(p is survivor or p.returncode == 0 for p in cold))
        aw = q.wait(lambda: q.window_for(survivor.pid))
        check("simultaneous cold launches elect and reveal one resident", bool(aw))
        exit_button = next(h for h in q.windows(aw) if q.text(h) == "退出程序")
        q.u.SendMessageW(exit_button, 0xF5, 0, 0)
        survivor.wait(timeout=5)
    finally:
        for process in processes + ([fixture] if fixture else []):
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=5)
        if previous:
            q.u.SetForegroundWindow(previous)
        (q.ROOT / "build/qa/single-instance-results.json").write_text(
            json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
