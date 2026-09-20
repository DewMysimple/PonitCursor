using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PointCursor
{
    internal sealed class InputNotice
    {
        public string Kind;
        public GestureKind Gesture;
        public IntPtr Window;
        public int StartX, StartY, X, Y;
        public uint Sequence;
        public long Time;
    }

    // Hooks own a dedicated message loop. Disk I/O, UI rendering and worker startup
    // must never delay it: Windows silently removes a low-level hook on timeout.
    internal sealed class InputMonitor : IDisposable
    {
        public const int NoticeMessage = 0x8001;
        private readonly IntPtr receiver;
        private readonly object sync = new object();
        private readonly Native.HookProc mouseCallback, keyCallback;
        private readonly Thread thread;
        private readonly SelectionGesture gesture;
        private IntPtr mouseHook, keyHook;
        private uint threadId;
        private InputNotice pending;
        private bool cDown;
        private volatile bool disposed;

        public InputMonitor(IntPtr receiver)
        {
            this.receiver = receiver;
            mouseCallback = Mouse; keyCallback = Keyboard;
            gesture = new SelectionGesture(SystemInformation.DragSize.Width / 2, SystemInformation.DragSize.Height / 2,
                SystemInformation.DoubleClickSize.Width / 2, SystemInformation.DoubleClickSize.Height / 2, Native.GetDoubleClickTime());
            Exception failure = null;
            using (var ready = new ManualResetEvent(false))
            {
                thread = new Thread(delegate() {
                    try
                    {
                        threadId = Native.GetCurrentThreadId();
                        Native.Message message;
                        Native.PeekMessage(out message, IntPtr.Zero, 0, 0, 0); // Create the queue before signalling ready.
                        IntPtr module = Native.GetModuleHandle(null);
                        mouseHook = Native.SetWindowsHookEx(14, mouseCallback, module, 0);
                        keyHook = Native.SetWindowsHookEx(13, keyCallback, module, 0);
                        if (mouseHook == IntPtr.Zero || keyHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { ready.Set(); }
                    try
                    {
                        if (failure != null) return;
                        Native.Message message;
                        while (Native.GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                        { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
                    }
                    finally
                    {
                        if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
                        if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
                    }
                }) { IsBackground = true, Name = "PointCursor input" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start(); ready.WaitOne();
            }
            if (failure != null) { thread.Join(); throw new InvalidOperationException("Input hooks could not start.", failure); }
        }
        private void Send(InputNotice notice)
        {
            // Only the newest complete intent matters; a busy UI cannot accumulate a backlog.
            bool notify;
            lock (sync) { notify = pending == null; pending = notice; }
            if (notify) Native.PostMessage(receiver, NoticeMessage, IntPtr.Zero, IntPtr.Zero);
        }
        public bool TryTake(out InputNotice notice)
        { lock (sync) { notice = pending; pending = null; return notice != null; } }

        private IntPtr Mouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !disposed)
            {
                int type = message.ToInt32();
                if (type == 0x201 || type == 0x202 || type == 0x200)
                {
                    var value = (Native.MouseData)Marshal.PtrToStructure(data, typeof(Native.MouseData));
                    if (type == 0x201) gesture.Down(Native.GetAncestor(Native.WindowFromPoint(value.Point), 2), value.Point.X, value.Point.Y, value.Time);
                    else if (type == 0x200) gesture.Move(value.Point.X, value.Point.Y);
                    else
                    {
                        // Foreground may already have changed after the physical release.
                        GestureKind kind = gesture.Up(value.Point.X, value.Point.Y);
                        if (kind != GestureKind.None)
                            Send(new InputNotice { Kind = "selection", Gesture = kind, Window = gesture.Window,
                                StartX = gesture.StartX, StartY = gesture.StartY, X = value.Point.X, Y = value.Point.Y,
                                Time = Native.Now });
                    }
                }
                else if (type == 0x204 || type == 0x207 || type == 0x20A) gesture.Reset();
            }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }
        private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !disposed)
            {
                var value = (Native.KeyData)Marshal.PtrToStructure(data, typeof(Native.KeyData));
                int type = message.ToInt32();
                bool isDown = type == 0x100 || type == 0x104;
                if (value.Key == 0x43)
                {
                    if (isDown && !cDown && Native.GetAsyncKeyState(0x11) < 0 && Native.GetAsyncKeyState(0x12) >= 0 && Native.GetAsyncKeyState(0x10) >= 0)
                        Send(new InputNotice { Kind = "copy", Window = Native.GetForegroundWindow(),
                            Sequence = Native.GetClipboardSequenceNumber(), Time = Native.Now });
                    cDown = isDown;
                }
            }
            return Native.CallNextHookEx(keyHook, code, message, data);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Native.PostThreadMessage(threadId, 0x12, UIntPtr.Zero, IntPtr.Zero);
            thread.Join(1500);
        }
    }
}
