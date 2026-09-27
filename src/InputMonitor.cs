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

    // Raw mouse input is queued, unlike a low-level hook which Windows can silently
    // remove after a timeout. Keep this message loop independent of the UI/providers.
    internal sealed class InputMonitor : IDisposable
    {
        public const int NoticeMessage = 0x8001;
        private readonly IntPtr receiver;
        private readonly object sync = new object();
        private readonly Native.HookProc keyCallback;
        private readonly Thread thread;
        private readonly SelectionGesture gesture;
        private IntPtr keyHook;
        private uint threadId;
        private InputNotice pending;
        private bool cDown;
        private volatile bool disposed;

        public InputMonitor(IntPtr receiver)
        {
            this.receiver = receiver;
            keyCallback = Keyboard;
            gesture = new SelectionGesture(SystemInformation.DragSize.Width / 2, SystemInformation.DragSize.Height / 2,
                SystemInformation.DoubleClickSize.Width / 2, SystemInformation.DoubleClickSize.Height / 2, Native.GetDoubleClickTime());
            Exception failure = null;
            using (var ready = new ManualResetEvent(false))
            {
                thread = new Thread(delegate() {
                    MouseWindow window = null;
                    System.Windows.Forms.Timer renew = null;
                    try
                    {
                        threadId = Native.GetCurrentThreadId();
                        Native.Message message;
                        Native.PeekMessage(out message, IntPtr.Zero, 0, 0, 0); // Create the queue before signalling ready.
                        window = new MouseWindow(Mouse);
                        RenewKeyboard();
                        if (keyHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                        // Ctrl+C must capture the sequence BEFORE the destination copies.
                        // Keep that tiny pre-dispatch hook, and renew its registration.
                        renew = new System.Windows.Forms.Timer { Interval = 30000 };
                        renew.Tick += delegate { RenewKeyboard(); }; renew.Start();
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { ready.Set(); }
                    try
                    {
                        if (failure != null) return;
                        Native.Message message;
                        while (Native.GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                        {
#if POINTCURSOR_QA
                            if (message.Id == 0x8004) { Thread.Sleep(1500); continue; }
#endif
                            Native.TranslateMessage(ref message); Native.DispatchMessage(ref message);
                        }
                    }
                    finally
                    {
                        if (renew != null) renew.Dispose();
                        if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
                        if (window != null) window.Dispose();
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

        private void RenewKeyboard()
        {
            IntPtr next = Native.SetWindowsHookEx(13, keyCallback, Native.GetModuleHandle(null), 0);
            if (next == IntPtr.Zero) return;
            if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
            keyHook = next;
            cDown = Native.GetAsyncKeyState(0x43) < 0;
        }
        private void Mouse(uint buttons, Native.Point point, uint time)
        {
            if (disposed) return;
            // After a session/thread stall the old coordinates may now belong to
            // a different window. Discard that backlog; accept the next fresh gesture.
            if (unchecked((uint)Native.Now - time) > 500) { gesture.Reset(); return; }
            if ((buttons & 0xFFFC) != 0) { gesture.Reset(); return; }
            if ((buttons & 1) != 0)
                gesture.Down(Native.GetAncestor(Native.WindowFromPoint(point), 2), point.X, point.Y, time);
            gesture.Move(point.X, point.Y);
            if ((buttons & 2) != 0)
            {
                GestureKind kind = gesture.Up(point.X, point.Y);
                if (kind != GestureKind.None)
                    Send(new InputNotice { Kind = "selection", Gesture = kind, Window = gesture.Window,
                        StartX = gesture.StartX, StartY = gesture.StartY, X = point.X, Y = point.Y, Time = Native.Now });
            }
        }
#if POINTCURSOR_QA
        public void StallForTest() { Native.PostThreadMessage(threadId, 0x8004, UIntPtr.Zero, IntPtr.Zero); }
#endif
        private sealed class MouseWindow : NativeWindow, IDisposable
        {
            private readonly Action<uint, Native.Point, uint> receive;
            public MouseWindow(Action<uint, Native.Point, uint> receive)
            {
                this.receive = receive;
                CreateHandle(new CreateParams { Caption = "PointCursor raw mouse", Parent = new IntPtr(-3) });
                if (!Register(0x100, Handle)) { DestroyHandle(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            }
            private static bool Register(uint flags, IntPtr target)
            {
                return Native.RegisterRawInputDevices(new[] { new Native.RawInputDevice { Page = 1, Usage = 2, Flags = flags, Target = target } },
                    1, (uint)Marshal.SizeOf(typeof(Native.RawInputDevice)));
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0xFF)
                {
                    Native.RawMouseInput input; uint size = (uint)Marshal.SizeOf(typeof(Native.RawMouseInput));
                    uint read = Native.GetRawInputData(message.LParam, 0x10000003, out input, ref size, 24);
                    if (read == size && input.Type == 0)
                    {
                        uint point = Native.GetMessagePos();
                        receive(input.Buttons & 0xFFFF, new Native.Point { X = (short)point, Y = (short)(point >> 16) }, Native.GetMessageTime());
                    }
                }
                // DefWindowProc must release the Windows raw input packet.
                base.WndProc(ref message);
            }
            public void Dispose() { Register(1, IntPtr.Zero); DestroyHandle(); }
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
