"""Opt-in Chrome tests on a local HTML fixture in a separate browser profile."""
import base64
import json
from pathlib import Path
import subprocess
import time
import desktop_qa as q

def evaluate(expression):
    p=subprocess.run(['node',str(q.ROOT/'tests/cdp_eval.mjs'),'browser'],input=expression,encoding='utf-8',capture_output=True,timeout=12)
    if p.returncode:raise RuntimeError(p.stderr)
    return json.loads(p.stdout)

def main():
    previous=q.u.GetForegroundWindow();proc=switcher=None;results=[]
    page=q.ROOT/'build/qa/browser.html'
    page.write_text('<!doctype html><meta charset="utf-8"><title>PointCursor Browser QA</title><style>body{font:28px sans-serif;padding:40px}</style><p id="words">hello world</p><input id="secret" type="password" value="secret"><p>Synthetic local page. No network resources.</p>',encoding='utf-8')
    try:
        proc=q.start(Path('C:/Program Files/Google/Chrome/Application/chrome.exe'),['--user-data-dir='+str(q.ROOT/'build/qa/chrome-profile'),'--no-first-run','--no-default-browser-check','--disable-background-networking','--remote-debugging-address=127.0.0.1','--remote-debugging-port=9238','--new-window',page.as_uri()])
        hwnd=q.wait(lambda:next((h for h in q.windows() if 'PointCursor Browser QA' in q.text(h)),None),10)
        switcher=q.start(q.BIN/'PointCursor.Fixture.exe'); switch_hwnd=q.wait(lambda:q.window_for(switcher.pid,True))
        q.foreground(hwnd);time.sleep(.4)
        for name,end,expected,switch_away in [('word',5,'hello',False),('word-after-window-switch',5,'hello',True),('multiple-words',11,None,False),('password',0,None,False),('password-after-window-switch',0,None,True)]:
            q.foreground(hwnd)
            if name.startswith('password'):evaluate("(()=>{const r=document.createRange();r.setStart(document.getElementById('words').firstChild,0);r.setEnd(document.getElementById('words').firstChild,5);window.getSelection().removeAllRanges();window.getSelection().addRange(r);const p=document.getElementById('secret');p.focus();p.select();return true})()")
            else:evaluate("(()=>{const r=document.createRange();r.setStart(document.getElementById('words').firstChild,0);r.setEnd(document.getElementById('words').firstChild,"+str(end)+");window.getSelection().removeAllRanges();window.getSelection().addRange(r);return true})()")
            target='secret' if name.startswith('password') else 'words'
            point=evaluate("(()=>{const r=document.getElementById('"+target+"').getBoundingClientRect(),b=(outerWidth-innerWidth)/2,t=outerHeight-innerHeight-b;return{x:screenX+b+r.left+Math.min(10,r.width/2),y:screenY+t+r.top+r.height/2}})()")
            time.sleep(.15)
            if switch_away:q.foreground(switch_hwnd)
            p=subprocess.run([str(q.BIN/'PointCursor.Reader.exe')],input=f"1|{hwnd}|{round(point['x'])}|{round(point['y'])}|selection\n",encoding='utf-8',capture_output=True,timeout=3,creationflags=subprocess.CREATE_NO_WINDOW)
            fields=p.stdout.strip().split('|');word=base64.b64decode(fields[2]).decode() if len(fields)>2 and fields[1]=='word' else None
            result={'case':name,'status':fields[1] if len(fields)>1 else p.stdout,'word':word,'passed':word==expected};results.append(result);print(json.dumps(result),flush=True)
            assert word==expected
            if name.startswith('password'):assert fields[1]=='blocked'
        if hwnd:q.u.PostMessageW(hwnd,0x10,0,0)
        proc.wait(timeout=5)
    finally:
        if proc and proc.poll() is None:proc.terminate()
        if switcher and switcher.poll() is None:switcher.terminate()
        if previous:q.u.SetForegroundWindow(previous)
        (q.ROOT/'build/qa/browser-results.json').write_text(json.dumps(results,indent=2),encoding='utf-8')

if __name__=='__main__':main()
