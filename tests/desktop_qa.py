"""Opt-in interactive QA. Uses only the synthetic fixture; restores foreground/cursor.
Run after build.ps1 -Test. Requires Windows desktop and Python 3, no third-party packages.
"""
import ctypes as C
from ctypes import wintypes as W
import json
from pathlib import Path
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
BIN = ROOT / "build" / "tests"
u = C.WinDLL("user32", use_last_error=True)
u.SetProcessDPIAware()
k = C.WinDLL("kernel32", use_last_error=True)
CALLBACK = C.WINFUNCTYPE(W.BOOL, W.HWND, W.LPARAM)
u.GetForegroundWindow.restype = W.HWND
u.SendMessageW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM]
u.SendMessageW.restype = W.LPARAM
u.PostMessageW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM]
u.SetForegroundWindow.argtypes = [W.HWND]
u.ShowWindow.argtypes = [W.HWND, C.c_int]
u.GetWindowTextW.argtypes = [W.HWND, W.LPWSTR, C.c_int]
u.GetClassNameW.argtypes = [W.HWND, W.LPWSTR, C.c_int]
u.GetWindowRect.argtypes = [W.HWND, C.POINTER(W.RECT)]
u.GetWindowThreadProcessId.argtypes = [W.HWND, C.POINTER(W.DWORD)]
u.EnumChildWindows.argtypes = [W.HWND, CALLBACK, W.LPARAM]
u.EnumWindows.argtypes = [CALLBACK, W.LPARAM]
u.IsWindowVisible.argtypes = [W.HWND]
u.WindowFromPoint.argtypes = [W.POINT]; u.WindowFromPoint.restype = W.HWND
u.GetClipboardData.argtypes = [W.UINT]; u.GetClipboardData.restype = W.HANDLE
u.SetClipboardData.argtypes = [W.UINT, W.HANDLE]; u.SetClipboardData.restype = W.HANDLE
u.OpenClipboard.argtypes = [W.HWND]
k.GlobalSize.argtypes = [W.HANDLE]; k.GlobalSize.restype = C.c_size_t
k.GlobalLock.argtypes = [W.HANDLE]; k.GlobalLock.restype = C.c_void_p
k.GlobalUnlock.argtypes = [W.HANDLE]
k.GlobalAlloc.argtypes = [W.UINT, C.c_size_t]; k.GlobalAlloc.restype = W.HANDLE

def text(hwnd):
    b = C.create_unicode_buffer(1024); u.GetWindowTextW(hwnd, b, len(b)); return b.value

def cls(hwnd):
    b = C.create_unicode_buffer(256); u.GetClassNameW(hwnd, b, len(b)); return b.value

def windows(parent=None):
    found = []
    @CALLBACK
    def collect(hwnd, _):
        found.append(hwnd); return True
    (u.EnumChildWindows(parent, collect, 0) if parent else u.EnumWindows(collect, 0))
    return found

def window_for(pid, include_hidden=False):
    for hwnd in windows():
        value = W.DWORD(); u.GetWindowThreadProcessId(hwnd, C.byref(value))
        if value.value == pid and (u.IsWindowVisible(hwnd) or (include_hidden and cls(hwnd).startswith("WindowsForms"))) and text(hwnd): return hwnd

def wait(fn, seconds=5):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        result = fn()
        if result: return result
        time.sleep(.04)
    raise AssertionError("Timed out: " + str(fn))

def start(path, args=(), env=None):
    si = subprocess.STARTUPINFO(); si.dwFlags |= subprocess.STARTF_USESHOWWINDOW; si.wShowWindow = 0
    return subprocess.Popen([str(path), *args], startupinfo=si, env=env)

def foreground(hwnd):
    u.ShowWindow(hwnd, 9)
    key(0x12); key(0x12, True)
    u.SetForegroundWindow(hwnd)
    wait(lambda: u.GetForegroundWindow() == hwnd)

def click(x, y, twice=False):
    u.SetCursorPos(int(x), int(y))
    for _ in range(2 if twice else 1):
        u.mouse_event(2, 0, 0, 0, 0); time.sleep(.04); u.mouse_event(4, 0, 0, 0, 0); time.sleep(.06)

def drag(x1, y1, x2, y2):
    u.SetCursorPos(int(x1), int(y1)); u.mouse_event(2, 0, 0, 0, 0)
    for step in range(1, 7):
        u.SetCursorPos(int(x1 + (x2 - x1) * step / 6), int(y1 + (y2 - y1) * step / 6)); time.sleep(.015)
    u.mouse_event(4, 0, 0, 0, 0)

def key(code, up=False): u.keybd_event(code, 0, 2 if up else 0, 0)

def rect(hwnd):
    value = W.RECT(); u.GetWindowRect(hwnd, C.byref(value)); return value

def status(hwnd):
    return [text(child) for child in windows(hwnd) if text(child).startswith(("已发音", "此处未能", "已暂停", "准备好了", "无法确认"))]

def clipboard_snapshot():
    if not u.OpenClipboard(None): return None
    try:
        result = []; fmt = 0
        while True:
            fmt = u.EnumClipboardFormats(fmt)
            if not fmt: break
            if fmt not in (1, 7, 8, 13, 16, 17) and fmt < 0xC000: return None
            handle = u.GetClipboardData(fmt); size = k.GlobalSize(handle)
            if not size or size > 16 * 1024 * 1024: return None
            ptr = k.GlobalLock(handle)
            if not ptr: return None
            try: result.append((fmt, C.string_at(ptr, size)))
            finally: k.GlobalUnlock(handle)
        return result
    finally: u.CloseClipboard()

def restore_clipboard(snapshot, sequence, owner):
    if snapshot is None or u.GetClipboardSequenceNumber() != sequence: return
    if not u.OpenClipboard(owner): return
    try:
        u.EmptyClipboard()
        for fmt, data in snapshot:
            handle = k.GlobalAlloc(0x42, len(data)); ptr = k.GlobalLock(handle)
            C.memmove(ptr, data, len(data)); k.GlobalUnlock(handle); u.SetClipboardData(fmt, handle)
    finally: u.CloseClipboard()

def main():
    previous = u.GetForegroundWindow(); cursor = W.POINT(); u.GetCursorPos(C.byref(cursor))
    app = fixture = None; checks = []; snapshot = None; copy_sequence = None; fw = None
    def check(name, condition):
        if not condition: raise AssertionError(name + ": " + repr(status(aw)))
        checks.append(name); print("PASS:", name, flush=True)
    try:
        app = start(BIN / "PointCursor.exe"); aw = wait(lambda: window_for(app.pid, True)); u.ShowWindow(aw, 4)
        fixture = start(ROOT / "build" / "tests" / "PointCursor.Fixture.exe"); fw = wait(lambda: window_for(fixture.pid, True)); u.ShowWindow(fw, 4)
        foreground(fw); time.sleep(.5)
        rich = next(h for h in windows(fw) if "RichEdit" in cls(h))
        coordinates = wait(lambda: next((text(h) for h in windows(fw) if text(h).startswith('QA coordinates|')), None))
        positions = {int(v[0]):(int(v[1]),int(v[2])) for v in (p.split(',') for p in coordinates.split('|')[1:])}
        def position(index):
            area=rect(rich); px,py=positions[index]; return area.left+px,area.top+py+10
        x,y=position(0);x+=10
        before_clipboard = u.GetClipboardSequenceNumber()
        clock = time.monotonic(); click(x, y, True)
        wait(lambda: "已发音 · hello" in status(aw), 4)
        check("RichTextBox double-click reads hello", True)
        checks.append("First pronunciation status in %.0f ms" % ((time.monotonic() - clock) * 1000))
        check("automatic selection leaves clipboard unchanged", u.GetClipboardSequenceNumber() == before_clipboard)
        # Start just inside the next word; the exact boundary belongs to the previous
        # highlighted trailing space and would initiate RichEdit drag-and-drop instead.
        foreground(fw); time.sleep(.15); start_pos=position(6); end_pos=position(11); drag(start_pos[0]+3,start_pos[1],end_pos[0]+3,end_pos[1])
        wait(lambda: "已发音 · world" in status(aw), 4)
        check("RichTextBox drag reads world", True)
        foreground(fw); click(x, y, True); wait(lambda: "已发音 · hello" in status(aw))
        check("reselecting previous word reads again", True)
        pause = next(h for h in windows(aw) if text(h) == "暂停")
        u.SendMessageW(pause, 0xF5, 0, 0)
        world_x,world_y=position(6);click(world_x+10,world_y,True); time.sleep(1)
        check("pause suppresses automatic playback", any(s.startswith("已暂停") for s in status(aw)))
        u.SendMessageW(pause, 0xF5, 0, 0)
        # A selection made without a mouse gesture exercises only Ctrl+C fallback.
        snapshot = clipboard_snapshot()
        if snapshot is not None:
            foreground(fw); click(x, y); key(0x1B); key(0x1B, True)
            u.SendMessageW(rich, 0xB1, 6, 11)
            key(0x11); key(0x43); key(0x43, True); key(0x11, True)
            wait(lambda: "已发音 · world" in status(aw))
            copy_sequence = u.GetClipboardSequenceNumber()
            check("explicit Ctrl+C fallback reads selected word", True)
            time.sleep(.2)
            check("fallback does not rewrite clipboard", u.GetClipboardSequenceNumber() == copy_sequence)
        else: checks.append("SKIP clipboard interaction: cannot losslessly snapshot current formats")
        password = next(h for h in windows(fw) if "EDIT" in cls(h).upper() and h != rich)
        area2 = rect(password); baseline = status(aw)
        click(area2.left + 20, area2.top + 10, True); time.sleep(1.2)
        check("password selection ignored", status(aw) == baseline)
        exit_button = next(h for h in windows(aw) if text(h) == "退出程序")
        u.SendMessageW(exit_button, 0xF5, 0, 0); app.wait(timeout=5)
        check("exit releases app", app.returncode == 0)
    except Exception:
        print("DIAGNOSTIC:", status(aw) if app else [], "foreground", text(u.GetForegroundWindow()), flush=True)
        if fixture and fw:
            print("FIXTURE:", [(cls(h), text(h)[:80], u.SendMessageW(h, 0xB0, 0, 0) if 'RichEdit' in cls(h) else 0) for h in windows(fw)], flush=True)
        raise
    finally:
        if copy_sequence is not None: restore_clipboard(snapshot, copy_sequence, fw)
        for proc in (app, fixture):
            if proc and proc.poll() is None:
                hwnd = window_for(proc.pid)
                if hwnd: u.PostMessageW(hwnd, 0x10, 0, 0)
                try: proc.wait(timeout=2)
                except subprocess.TimeoutExpired: proc.terminate()
        u.SetCursorPos(cursor.x, cursor.y)
        if previous: u.SetForegroundWindow(previous)
        (ROOT / "build" / "qa" / "desktop-results.json").write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding="utf-8")

if __name__ == "__main__": main()
