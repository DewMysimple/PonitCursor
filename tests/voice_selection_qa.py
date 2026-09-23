"""Verify real voice selection, playback and persistence in the isolated QA app.
Requires installed en-US and en-GB SAPI voices; no mouse or clipboard operations.
"""
import ctypes as C
from ctypes import wintypes as W
import json
import xml.etree.ElementTree as ET
import desktop_qa as q

q.u.GetParent.argtypes = [W.HWND]
q.u.GetParent.restype = W.HWND
q.u.GetDlgCtrlID.argtypes = [W.HWND]


def item_text(combo, index):
    size = q.u.SendMessageW(combo, 0x149, index, 0)  # CB_GETLBTEXTLEN
    if size < 0:
        raise AssertionError("Invalid voice item")
    label = C.create_unicode_buffer(size + 1)
    q.u.SendMessageW(combo, 0x148, index, C.addressof(label))  # CB_GETLBTEXT
    return label.value


def selected_text(combo):
    return item_text(combo, q.u.SendMessageW(combo, 0x147, 0, 0))  # CB_GETCURSEL


def main():
    previous = q.u.GetForegroundWindow()
    path = q.BIN / "qa/settings.xml"
    original = path.read_bytes() if path.exists() else None
    app = None
    checks = []

    def check(name, condition):
        if not condition:
            raise AssertionError(name)
        checks.append(name)
        print("PASS:", name, flush=True)

    def start():
        nonlocal app
        app = q.start(q.BIN / "PointCursor.exe")
        hwnd = q.wait(lambda: q.window_for(app.pid))
        combo = next(h for h in q.windows(hwnd) if "COMBOBOX" in q.cls(h).upper())
        return hwnd, combo

    def exit_app(hwnd):
        button = next(h for h in q.windows(hwnd) if q.text(h) == "退出程序")
        q.u.SendMessageW(button, 0xF5, 0, 0)
        app.wait(timeout=5)
        check("QA app exits cleanly", app.returncode == 0)

    def preview(hwnd):
        before = q.pronunciation_count(hwnd, "hello")
        button = next(h for h in q.windows(hwnd) if q.text(h) == "试听 hello")
        q.u.SendMessageW(button, 0xF5, 0, 0)
        q.wait(lambda: q.pronunciation_count(hwnd, "hello") > before)

    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("<AppSettings><Rate>-1</Rate><Volume>85</Volume><CopyToSpeak>true</CopyToSpeak></AppSettings>", encoding="utf-8")
        hwnd, combo = start()
        choices = []
        for index in range(q.u.SendMessageW(combo, 0x146, 0, 0)):  # CB_GETCOUNT
            choices.append(item_text(combo, index))
        check("UI offers both American and British voices", any(v.startswith("美式 · ") for v in choices) and any(v.startswith("英式 · ") for v in choices))
        check("UI offers no other accents", all(v.startswith(("美式 · ", "英式 · ")) for v in choices))
        for index, label in enumerate(choices):
            changed = selected_text(combo) != label
            q.u.SendMessageW(combo, 0x14E, index, 0)  # CB_SETCURSEL
            q.u.SendMessageW(q.u.GetParent(combo), 0x111, (1 << 16) | (q.u.GetDlgCtrlID(combo) & 0xFFFF), combo)
            name = label.partition(" · ")[2]
            if changed:
                q.wait(lambda: path.exists() and ET.parse(path).findtext("Voice") == name)
            preview(hwnd)
            check("select, save and play: " + label, selected_text(combo) == label)
        selected = selected_text(combo)
        saved = ET.parse(path)
        check("voice changes preserve rate, volume and copy preferences",
              saved.findtext("Rate") == "-1" and saved.findtext("Volume") == "85"
              and saved.findtext("CopyToSpeak") == "true")
        exit_app(hwnd)
        hwnd, combo = start()
        check("restart retains the selected voice", selected_text(combo) == selected)
        preview(hwnd)
        check("restored voice plays after restart", True)
        exit_app(hwnd)
    finally:
        if app is not None and app.poll() is None:
            app.terminate()
            app.wait(timeout=5)
        if original is None:
            path.unlink(missing_ok=True)
        else:
            path.write_bytes(original)
        if previous:
            q.u.SetForegroundWindow(previous)
        (q.ROOT / "build/qa/voice-selection-results.json").write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
