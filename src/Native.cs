using System;
using System.Runtime.InteropServices;

namespace PointCursor
{
    internal static class Native
    {
        public delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MouseData { public Point Point; public uint Mouse, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] public struct KeyData { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
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
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardOwner();
        public static long Now { get { return (long)GetTickCount64(); } }
        public static uint ProcessOf(IntPtr window) { uint pid; GetWindowThreadProcessId(window, out pid); return pid; }

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
