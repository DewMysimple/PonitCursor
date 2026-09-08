"""QA against a separately launched ObsidianTestVault debug profile, never a user vault."""
import base64
import ctypes as C
from ctypes import wintypes as W
import hashlib
import json
import subprocess
import time
import desktop_qa as q

def evaluate(expression):
    p = subprocess.run(['node', str(q.ROOT / 'tests' / 'cdp_eval.mjs')], input=expression, encoding='utf-8', capture_output=True, timeout=12)
    if p.returncode: raise RuntimeError(p.stderr)
    return json.loads(p.stdout)

def main():
    before = q.u.GetForegroundWindow(); cursor = W.POINT(); q.u.GetCursorPos(C.byref(cursor))
    note = q.ROOT / 'build' / 'qa' / 'ObsidianTestVault' / 'Pronunciation.md'
    digest = hashlib.sha256(note.read_bytes()).hexdigest()
    results = []
    hwnd = q.wait(lambda: next((h for h in q.windows() if 'ObsidianTestVault' in q.text(h)),None))
    cases=[('hello',2,0,2,5,'hello'),('world',2,6,2,11,'world'),('hyphen',4,0,4,10,'well-known'),('apostrophe',4,11,4,16,"don't"),('multiple-words',2,0,2,11,None),('cross-paragraph',2,0,4,10,None),('empty',2,0,2,0,None)]
    try:
        for name, state in [('live-preview', {'mode':'source','source':False}), ('source', {'mode':'source','source':True}), ('reading', {'mode':'preview'})]:
            evaluate("(async()=>{await app.workspace.activeLeaf.setViewState({type:'markdown',state:" + json.dumps({'file':'Pronunciation.md',**state}) + "});return app.workspace.activeLeaf.view.getState();})()")
            time.sleep(.2)
            for case,line1,ch1,line2,ch2,expected in cases:
                for attempt in range(3):
                    q.foreground(hwnd)
                    spec=json.dumps({'line1':line1,'ch1':ch1,'line2':line2,'ch2':ch2})
                    selected=evaluate("""(()=>{
                      const c=SPEC,view=app.workspace.activeLeaf.view;
                      if(view.getState().mode==='preview') {
                        const root=view.contentEl.querySelector('.markdown-preview-view');
                        const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT),nodes={};
                        let n;while(n=walker.nextNode()) {
                          if(n.textContent.trim()==='hello world')nodes[2]=n;
                          if(n.textContent.trim()==="well-known don't English")nodes[4]=n;
                        }
                        const r=document.createRange();r.setStart(nodes[c.line1],c.ch1);r.setEnd(nodes[c.line2],c.ch2);
                        window.getSelection().removeAllRanges();window.getSelection().addRange(r);
                      } else {view.editor.focus();view.editor.setSelection({line:c.line1,ch:c.ch1},{line:c.line2,ch:c.ch2});}
                      const s=window.getSelection(),r=s.rangeCount?s.getRangeAt(0).getBoundingClientRect():null;
                      return {text:s.toString(),x:r?r.x+Math.min(r.width/2,10):400,y:r?r.y+Math.min(r.height/2,10):250,scale:devicePixelRatio};
                    })()""".replace('SPEC',spec))
                    time.sleep(.15)
                    pt=W.POINT(0,0);q.u.ClientToScreen.argtypes=[W.HWND,C.POINTER(W.POINT)];q.u.ClientToScreen(hwnd,C.byref(pt))
                    x=round(pt.x+selected['x']*selected['scale']);y=round(pt.y+selected['y']*selected['scale'])
                    start=time.monotonic()
                    p=subprocess.run([str(q.BIN/'PointCursor.Reader.exe')],input=f'1|{hwnd}|{x}|{y}|selection\n',encoding='utf-8',capture_output=True,timeout=3,creationflags=subprocess.CREATE_NO_WINDOW)
                    fields=p.stdout.strip().split('|');word=base64.b64decode(fields[2]).decode() if len(fields)>2 and fields[1]=='word' else None
                    if q.u.GetForegroundWindow()==hwnd:break
                result={'mode':name,'case':case,'result':fields[1] if len(fields)>1 else p.stdout,'word':word,'ms':round((time.monotonic()-start)*1000),'passed':word==expected}
                results.append(result);print(json.dumps(result,ensure_ascii=True),flush=True)
                assert word==expected,f'{name}/{case}: {result}'
        assert hashlib.sha256(note.read_bytes()).hexdigest()==digest,'Synthetic note modified by selection'
        results.append({'noteUnchanged':True});print('PASS: synthetic note unchanged',flush=True)
    finally:
        q.u.SetCursorPos(cursor.x,cursor.y)
        if before:q.u.SetForegroundWindow(before)
        (q.ROOT/'build'/'qa'/'obsidian-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')

if __name__=='__main__':main()
