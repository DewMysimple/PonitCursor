using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PointCursor
{
    internal sealed class InputNotice
    {
        public string Kind;
        public IntPtr Window;
        public int StartX, StartY, X, Y;
        public uint Sequence;
        public long Time;
    }

    internal sealed class InputMonitor : IDisposable
    {
        public const int NoticeMessage = 0x8001;
        private readonly IntPtr receiver;
        private readonly ConcurrentQueue<InputNotice> pending = new ConcurrentQueue<InputNotice>();
        private readonly Native.HookProc mouseCallback, keyCallback;
        private IntPtr mouseHook, keyHook;
        private Native.Point down, lastClick;
        private long lastClickTime;
        private IntPtr lastClickWindow;
        private bool held, dragged, cDown;
        public InputMonitor(IntPtr receiver)
        {
            this.receiver = receiver;
            mouseCallback = Mouse; keyCallback = Keyboard;
            IntPtr module = Native.GetModuleHandle(null);
            mouseHook = Native.SetWindowsHookEx(14, mouseCallback, module, 0);
            keyHook = Native.SetWindowsHookEx(13, keyCallback, module, 0);
            if (mouseHook == IntPtr.Zero || keyHook == IntPtr.Zero) { int error = Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error); }
        }
        private void Send(string kind, int startX, int startY, int x, int y)
        {
            pending.Enqueue(new InputNotice { Kind = kind, StartX = startX, StartY = startY, X = x, Y = y, Window = Native.GetForegroundWindow(), Sequence = Native.GetClipboardSequenceNumber(), Time = Native.Now });
            Native.PostMessage(receiver, NoticeMessage, IntPtr.Zero, IntPtr.Zero);
        }
        public bool TryTake(out InputNotice notice) { return pending.TryDequeue(out notice); }
        private IntPtr Mouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int type = message.ToInt32();
                if (type == 0x201 || type == 0x202 || (type == 0x200 && held))
                {
                    Native.MouseData value = (Native.MouseData)Marshal.PtrToStructure(data, typeof(Native.MouseData));
                    if (type == 0x201)
                    { held = true; dragged = false; down = value.Point; }
                    else if (type == 0x200)
                    {
                        if (Math.Abs(value.Point.X - down.X) >= SystemInformation.DragSize.Width / 2 || Math.Abs(value.Point.Y - down.Y) >= SystemInformation.DragSize.Height / 2) dragged = true;
                    }
                    else if (held)
                    {
                        held = false;
                        // Some virtual pointers coalesce move events; the release position
                        // must also establish whether a drag occurred.
                        if (Math.Abs(value.Point.X - down.X) >= SystemInformation.DragSize.Width / 2 || Math.Abs(value.Point.Y - down.Y) >= SystemInformation.DragSize.Height / 2) dragged = true;
                        long now = Native.Now;
                        IntPtr window = Native.GetForegroundWindow();
                        bool twice = !dragged && lastClickWindow == window && now - lastClickTime <= Native.GetDoubleClickTime()
                            && Math.Abs(value.Point.X - lastClick.X) <= SystemInformation.DoubleClickSize.Width / 2
                            && Math.Abs(value.Point.Y - lastClick.Y) <= SystemInformation.DoubleClickSize.Height / 2;
                        if (dragged || twice) Send("selection", down.X, down.Y, value.Point.X, value.Point.Y);
                        lastClick = value.Point; lastClickTime = dragged || twice ? 0 : now; lastClickWindow = window;
                    }
                }
                else if (type == 0x204 || type == 0x207 || type == 0x20A) lastClickTime = 0;
            }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }
        private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                var value = (Native.KeyData)Marshal.PtrToStructure(data, typeof(Native.KeyData));
                int type = message.ToInt32();
                bool isDown = type == 0x100 || type == 0x104;
                if (value.Key == 0x43)
                {
                    if (isDown && !cDown && Native.GetAsyncKeyState(0x11) < 0 && Native.GetAsyncKeyState(0x12) >= 0 && Native.GetAsyncKeyState(0x10) >= 0) Send("copy", 0, 0, 0, 0);
                    cDown = isDown;
                }
            }
            return Native.CallNextHookEx(keyHook, code, message, data);
        }
        public void Dispose()
        {
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            mouseHook = keyHook = IntPtr.Zero;
        }
    }
}
