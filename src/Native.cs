using System;
using System.Runtime.InteropServices;

namespace PointCursor
{
    internal static class Native
    {
        public delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] public struct Message
        { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Point; public uint Private; }
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MouseData { public Point Point; public uint Mouse, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] public struct KeyData { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string caption);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool SetWindowText(IntPtr window, string text);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll")] public static extern IntPtr ChildWindowFromPointEx(IntPtr parent, Point point, uint flags);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr window);
        [DllImport("user32.dll")] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] public static extern UIntPtr GlobalSize(IntPtr memory);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out UIntPtr result);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] public static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardOwner();
        public static long Now { get { return (long)GetTickCount64(); } }
        public static uint ProcessOf(IntPtr window) { uint pid; GetWindowThreadProcessId(window, out pid); return pid; }
        public static IntPtr DescendantWindowAtPoint(IntPtr root, int x, int y)
        {
            IntPtr current = root;
            for (int depth = 0; depth < 16 && current != IntPtr.Zero; depth++)
            {
                Point point = new Point { X = x, Y = y };
                if (!ScreenToClient(current, ref point)) break;
                IntPtr child = ChildWindowFromPointEx(current, point, 0x0001 | 0x0002 | 0x0004);
                if (child == IntPtr.Zero || child == current) break;
                current = child;
            }
            return current;
        }

        public static string ReadShortClipboard(IntPtr owner, out bool busy)
        {
            busy = false;
            if (!IsClipboardFormatAvailable(13)) return null;
            if (!OpenClipboard(owner)) { busy = true; return null; }
            try
            {
                IntPtr memory = GetClipboardData(13);
                if (memory == IntPtr.Zero) return null;
                ulong bytes = GlobalSize(memory).ToUInt64();
                if (bytes < 2 || bytes > 260) return null;
                IntPtr ptr = GlobalLock(memory);
                if (ptr == IntPtr.Zero) return null;
                try
                {
                    string value = Marshal.PtrToStringUni(ptr, (int)(bytes / 2));
                    int end = value.IndexOf('\0');
                    return WordRules.Normalize(end >= 0 ? value.Substring(0, end) : value);
                }
                finally { GlobalUnlock(memory); }
            }
            finally { CloseClipboard(); }
        }
    }
}
