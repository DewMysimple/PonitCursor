"""Opt-in mouse and Ctrl+C end-to-end tests in the isolated Obsidian QA vault."""
import ctypes as C
from ctypes import wintypes as W
import json
import time
import desktop_qa as q
from obsidian_qa import evaluate

def select(line, start, end):
    return evaluate("""(()=>{
      const line=LINE,start=START,end=END,v=app.workspace.activeLeaf.view;
      if(v.getState().mode==='preview'){
        const walker=document.createTreeWalker(v.contentEl.querySelector('.markdown-preview-view'),NodeFilter.SHOW_TEXT);
        let n;while(n=walker.nextNode())if(n.textContent.trim()===(line===2?'hello world':"well-known don't English")){
          const r=document.createRange();r.setStart(n,start);r.setEnd(n,end);window.getSelection().removeAllRanges();window.getSelection().addRange(r);break;
        }
      }else{v.editor.focus();v.editor.setSelection({line,ch:start},{line,ch:end});}
      const r=window.getSelection().getRangeAt(0).getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height,scale:devicePixelRatio};
    })()""".replace('LINE',str(line)).replace('START',str(start)).replace('END',str(end)))

def chord(*codes):
    for code in codes:q.key(code)
    for code in reversed(codes):q.key(code,True)

def clear_selection(mode):
    if mode == 'reading':
        evaluate("window.getSelection().removeAllRanges();true")
    else:
        evaluate("app.workspace.activeLeaf.view.editor.setCursor({line:0,ch:0});true")

def main():
    previous=q.u.GetForegroundWindow();cursor=W.POINT();q.u.GetCursorPos(C.byref(cursor))
    app=None;aw=None;snapshot=None;sequence=None;results=[]
    hwnd=next(h for h in q.windows() if 'ObsidianTestVault' in q.text(h))
    try:
        app=q.start(q.BIN/'PointCursor.exe');aw=q.wait(lambda:q.window_for(app.pid,True));q.u.ShowWindow(aw,9)
        pause=next(h for h in q.windows(aw) if q.text(h)=='暂停')
        snapshot=q.clipboard_snapshot()
        for mode,state in [('live-preview',{'mode':'source','source':False}),('source',{'mode':'source','source':True}),('reading',{'mode':'preview'})]:
            mode_results_start=len(results)
            q.u.SendMessageW(pause,0xF5,0,0);q.u.SendMessageW(pause,0xF5,0,0)
            evaluate("(async()=>{await app.workspace.activeLeaf.setViewState({type:'markdown',state:"+json.dumps({'file':'Pronunciation.md',**state})+"});return true;})()")
            q.foreground(hwnd);time.sleep(.15)
            def coordinates(r):
                pt=W.POINT(0,0);q.u.ClientToScreen.argtypes=[W.HWND,C.POINTER(W.POINT)];q.u.ClientToScreen(hwnd,C.byref(pt))
                return pt.x+r['x']*r['scale'],pt.y+(r['y']+r['height']/2)*r['scale']
            if mode != 'reading':
                # Run this first so it also covers a cold selection-reader process.
                # Finish a native drag and invoke the user's Obsidian shortcut before
                # PointCursor can rely on the live selection staying unchanged.
                original=evaluate("app.workspace.activeLeaf.view.editor.getValue()")
                r=select(2,0,5);x,y=coordinates(r);clear_selection(mode);time.sleep(.1)
                hello_count=q.pronunciation_count(aw,'hello')
                q.drag(x+1,y,x+r['width']-1,y)
                chord(0x11,0x10,0x48)
                q.wait(lambda:'==hello==' in evaluate("app.workspace.activeLeaf.view.editor.getLine(2)"),3)
                q.wait(lambda:q.pronunciation_count(aw,'hello')>hello_count,3)
                results.append({'mode':mode,'case':'drag-then-highlight','passed':True})
                chord(0x11,0x5A)
                q.wait(lambda:evaluate("app.workspace.activeLeaf.view.editor.getValue()")==original,3)
            r=select(2,0,5);x,y=coordinates(r);clear_selection(mode);time.sleep(.05)
            clock=time.monotonic();q.click(x+8,y,True)
            q.wait(lambda:any(value.startswith('已发音 · hello') for value in q.status(aw)),3)
            results.append({'mode':mode,'case':'double-click','passed':True,'ms':round((time.monotonic()-clock)*1000)})
            r=select(2,6,11);x,y=coordinates(r)
            # Clear the script-created selection before exercising native mouse dragging.
            clear_selection(mode);time.sleep(.1);q.drag(x+1,y,x+r['width']-1,y)
            q.wait(lambda:any(value.startswith('已发音 · world') for value in q.status(aw)),3)
            results.append({'mode':mode,'case':'drag','passed':True})
            if snapshot is not None:
                q.key(0x1B);q.key(0x1B,True);select(4,11,16)
                q.key(0x11);q.key(0x43);q.key(0x43,True);q.key(0x11,True)
                q.wait(lambda:any(value.startswith("已发音 · don't") for value in q.status(aw)),3)
                sequence=q.u.GetClipboardSequenceNumber()
                results.append({'mode':mode,'case':'Ctrl+C','passed':True})
            print(json.dumps(results[mode_results_start:],ensure_ascii=True),flush=True)
    except Exception:
        print('DIAGNOSTIC:',q.status(aw),evaluate('window.getSelection().toString()'),q.text(q.u.GetForegroundWindow()),flush=True)
        raise
    finally:
        if sequence is not None:q.restore_clipboard(snapshot,sequence,hwnd)
        if app and app.poll() is None:
            if aw:
                exit_button=next((h for h in q.windows(aw) if q.text(h)=='退出程序'),None)
                if exit_button:q.u.SendMessageW(exit_button,0xF5,0,0)
            try:app.wait(timeout=3)
            except Exception:app.terminate()
        q.u.SetCursorPos(cursor.x,cursor.y)
        if previous:q.u.SetForegroundWindow(previous)
        (q.ROOT/'build/qa/obsidian-live-results.json').write_text(json.dumps(results,indent=2),encoding='utf-8')

if __name__=='__main__':main()
