"""Opt-in idle recovery in the isolated Obsidian vault; no clipboard or note writes.

The same hidden PointCursor process stays alive through each idle period. The
first gesture after each period must work without preview, restart or reselecting.
"""
import argparse
import ctypes as C
from ctypes import wintypes as W
import hashlib
import json
import os
import time
import desktop_qa as q
from obsidian_qa import evaluate


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--idle-seconds', type=int, default=330)
    parser.add_argument('--cycles', type=int, default=2)
    parser.add_argument('--first-gesture', choices=['double-click', 'drag'], default='double-click')
    parser.add_argument('--mode', choices=['live-preview', 'source', 'reading'], default='live-preview')
    args = parser.parse_args()
    previous = q.u.GetForegroundWindow(); cursor = W.POINT(); q.u.GetCursorPos(C.byref(cursor))
    note = q.ROOT / 'build/qa/ObsidianTestVault/Pronunciation.md'
    digest = hashlib.sha256(note.read_bytes()).hexdigest()
    hwnd = next(h for h in q.windows() if 'ObsidianTestVault' in q.text(h))
    pid = W.DWORD(); q.u.GetWindowThreadProcessId(hwnd, C.byref(pid))
    environment = dict(os.environ, POINTCURSOR_QA_SOURCE_PID=str(pid.value))
    app = None; results = []
    state = {'mode': 'preview'} if args.mode == 'reading' else {'mode': 'source', 'source': args.mode == 'source'}
    try:
        evaluate("(async()=>{const leaf=app.workspace.getLeavesOfType('markdown')[0]||app.workspace.getLeaf();await leaf.setViewState({type:'markdown',state:" + json.dumps({'file': 'Pronunciation.md', **state}) + "});app.workspace.setActiveLeaf(leaf,{focus:true});return true;})()")
        app = q.start(q.BIN / 'PointCursor.exe', env=environment)
        aw = q.wait(lambda: q.window_for(app.pid, True))
        q.wait(lambda: q.u.IsWindowVisible(aw)); time.sleep(.2)
        q.u.ShowWindow(aw, 0)
        last_word = None

        def gesture(word, drag=False):
            nonlocal last_word
            # A rapid second double click at the same position is a native
            # triple/quadruple click, which can select a whole paragraph.
            if word == last_word: time.sleep(q.u.GetDoubleClickTime() / 1000 + .05)
            q.foreground(hwnd); q.key(0x1B); q.key(0x1B, True)
            time.sleep(.2)  # Allow the restored native window's layout to settle.
            # Read geometry without creating a DOM/editor selection first. Dragging
            # a preselected word can start native drag-and-drop and alter the fixture.
            r = q.wait(lambda: evaluate("""(()=>{
              const view=app.workspace.getLeavesOfType('markdown').find(l=>l.view.file?.name==='Pronunciation.md').view;
              const root=view.contentEl.querySelector(view.getState().mode==='preview'?'.markdown-preview-view':'.cm-content');
              if(!root)return null;
              const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);let n;
              while(n=walker.nextNode())if(n.textContent.includes('hello world')){
                const start=n.textContent.indexOf(WORD),range=document.createRange();
                range.setStart(n,start);range.setEnd(n,start+WORD.length);
                const r=range.getBoundingClientRect();return r.width&&r.height?{x:r.x,y:r.y,width:r.width,height:r.height,scale:devicePixelRatio}:null;
              }
              return null;
            })()""".replace('WORD', json.dumps(word))), 3)
            pt = W.POINT(); q.u.ClientToScreen.argtypes = [W.HWND, C.POINTER(W.POINT)]
            q.u.ClientToScreen(hwnd, C.byref(pt))
            x, y = pt.x + r['x'] * r['scale'], pt.y + (r['y'] + r['height'] / 2) * r['scale']
            before = q.pronunciation_count(aw, word); clock = time.monotonic()
            sequence = q.u.GetClipboardSequenceNumber()
            if drag: q.drag(x + 1, y, x + r['width'] * r['scale'] - 1, y)
            else: q.click(x + 8, y, True)
            q.wait(lambda: q.pronunciation_count(aw, word) > before, 4)
            assert q.u.GetForegroundWindow() == hwnd, 'Desktop focus changed during the gesture'
            assert q.u.GetClipboardSequenceNumber() == sequence, 'Clipboard changed during gesture'
            assert evaluate('window.getSelection().toString().trim()') == word, 'Native gesture selected a different range'
            assert hashlib.sha256(note.read_bytes()).hexdigest() == digest, 'Synthetic note changed'
            last_word = word
            return round((time.monotonic() - clock) * 1000)

        gesture('hello')
        for cycle in range(args.cycles):
            q.u.ShowWindow(hwnd, 6)  # Obsidian background, PointCursor hidden in tray.
            if previous: q.u.SetForegroundWindow(previous)
            started = time.monotonic()
            print(f'IDLE cycle={cycle + 1} seconds={args.idle_seconds} pid={app.pid}', flush=True)
            while time.monotonic() - started < args.idle_seconds:
                assert app.poll() is None, 'Resident exited during idle'
                time.sleep(min(1, max(.01, args.idle_seconds - (time.monotonic() - started))))
            elapsed = round(time.monotonic() - started, 2)
            # Following checks finish on hello, so the next drag starts on unselected world.
            is_drag = (cycle + (args.first_gesture == 'drag')) % 2 == 1
            milliseconds = gesture('world', drag=is_drag)
            result = {'mode': args.mode, 'cycle': cycle + 1, 'idle_seconds': elapsed,
                      'gesture': 'drag' if is_drag else 'double-click', 'first_gesture_ms': milliseconds, 'passed': True}
            results.append(result); print(json.dumps(result), flush=True)
            # Further selections must keep working, including repeating the same word.
            for word in ['hello', 'world', 'world', 'hello']: gesture(word)
            results.append({'cycle': cycle + 1, 'four_following_gestures': True})
        assert hashlib.sha256(note.read_bytes()).hexdigest() == digest, 'Synthetic note changed'
        results.append({'note_unchanged': True, 'clipboard_unchanged': True})
    except Exception:
        print('DIAGNOSTIC:', q.text(aw) if app else '', q.status(aw) if app else [], q.text(q.u.GetForegroundWindow()), flush=True)
        raise
    finally:
        if app and app.poll() is None:
            exit_button = next((h for h in q.windows(aw) if q.text(h) == '退出程序'), None)
            if exit_button: q.u.SendMessageW(exit_button, 0xF5, 0, 0)
            try: app.wait(timeout=4)
            except Exception: app.terminate(); app.wait(timeout=3)
        q.u.SetCursorPos(cursor.x, cursor.y)
        if previous: q.u.SetForegroundWindow(previous)
        (q.ROOT / 'build/qa' / ('idle-' + args.mode + '-results.json')).write_text(json.dumps(results, indent=2), encoding='utf-8')


if __name__ == '__main__': main()
