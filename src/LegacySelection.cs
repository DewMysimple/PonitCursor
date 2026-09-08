using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Accessibility;

namespace PointCursor
{
    // Chromium/Electron versions without a UIA TextPattern expose selected text via IA2.
    // ABI order follows the IAccessible2 IAccessibleText IDL. Only read methods are called.
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface AccessibleServiceProvider
    { [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result); }

    [ComImport, Guid("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface AccessibleText
    {
        [PreserveSig] int AddSelection(int start, int end);
        [PreserveSig] int Attributes(int offset, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string attributes);
        [PreserveSig] int CaretOffset(out int offset);
        [PreserveSig] int CharacterExtents(int offset, int coordinates, out int x, out int y, out int width, out int height);
        [PreserveSig] int SelectionCount(out int count);
        [PreserveSig] int OffsetAtPoint(int x, int y, int coordinates, out int offset);
        [PreserveSig] int Selection(int index, out int start, out int end);
        [PreserveSig] int Text(int start, int end, [MarshalAs(UnmanagedType.BStr)] out string text);
    }

    [ComImport, Guid("2118B599-733F-43D0-A569-0B31D125ED9A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface AccessibleSelectionContainer
    { [PreserveSig] int Selections(out IntPtr selections, out int count); }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccessibleSelection
    { public IntPtr Start; public int StartOffset; public IntPtr End; public int EndOffset; public byte StartIsActive; }

    internal static class LegacySelection
    {
        private delegate bool EnumProc(IntPtr window, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int maximum);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromPoint(Native.Point point, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible, [MarshalAs(UnmanagedType.Struct)] out object child);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr window, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object accessible);
        [DllImport("oleacc.dll")] private static extern int WindowFromAccessibleObject(IAccessible accessible, out IntPtr window);
        private static bool Belongs(IAccessible accessible, uint pid)
        {
            IntPtr window;
            return accessible != null && WindowFromAccessibleObject(accessible, out window) >= 0 && Native.ProcessOf(window) == pid;
        }
        private static bool Protected(IAccessible accessible, object child)
        {
            object value = accessible.get_accState(child ?? 0);
            return value is int && (((int)value & 0x20000000) != 0);
        }
        private static AccessibleText AsText(IAccessible accessible)
        {
            var direct = accessible as AccessibleText;
            if (direct != null) return direct;
            var provider = accessible as AccessibleServiceProvider;
            if (provider == null) return null;
            Guid service = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            Guid iid = new Guid("E89F726E-C4F4-4C19-BB19-B647D7FA8478");
            IntPtr pointer;
            int hr = provider.QueryService(ref service, ref iid, out pointer);
            if (hr < 0 || pointer == IntPtr.Zero) return null;
            try { return Marshal.GetObjectForIUnknown(pointer) as AccessibleText; }
            finally { Marshal.Release(pointer); }
        }
        private static bool ProtectedAncestry(IAccessible accessible, uint pid)
        {
            for (int i = 0; i < 16 && Belongs(accessible, pid); i++)
            {
                if (Protected(accessible, 0)) return true;
                accessible = accessible.accParent as IAccessible;
            }
            return false;
        }
        public static bool ProtectedFocus(IntPtr window)
        {
            try
            {
                bool blocked = false; int scanned = 0;
                EnumChildWindows(window, delegate(IntPtr candidate, IntPtr unused) {
                    var name = new System.Text.StringBuilder(128); GetClassName(candidate, name, name.Capacity);
                    if (name.ToString() == "Chrome_RenderWidgetHostHWND" && IsWindowVisible(candidate))
                    {
                        Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); object document;
                        if (AccessibleObjectFromWindow(candidate, 0xFFFFFFFC, ref iid, out document) >= 0)
                        {
                            var current = document as IAccessible;
                            for (int i = 0; i < 16 && current != null; i++)
                            {
                                if (Protected(current, 0)) { blocked = true; break; }
                                object focus = current.accFocus;
                                if (focus is int && (int)focus != 0)
                                {
                                    if (Protected(current, focus)) { blocked = true; break; }
                                    focus = current.get_accChild(focus);
                                }
                                var next = focus as IAccessible;
                                if (next == null || Object.ReferenceEquals(next, current)) break;
                                current = next;
                            }
                        }
                    }
                    return !blocked && ++scanned < 64;
                }, IntPtr.Zero);
                return blocked;
            }
            catch (COMException) { return true; }
            catch (ArgumentException) { return true; }
        }
        private static bool ParentSelectionMatches(IAccessible accessible, uint pid, string word)
        {
            for (int i = 0; i < 16 && Belongs(accessible, pid); i++, accessible = accessible.accParent as IAccessible)
            {
                AccessibleText text = AsText(accessible); int count, start, end;
                if (text == null || text.SelectionCount(out count) < 0 || count == 0) continue;
                if (count != 1 || text.Selection(0, out start, out end) < 0) return false;
                if (start > end) { int swap = start; start = end; end = swap; }
                if (start < 0 || (long)end - start > 128) return false;
                string selected;
                if (text.Text(start, end, out selected) < 0 || String.IsNullOrWhiteSpace(selected)) continue;
                if (selected.Trim() == "\uFFFC") continue;
                if (!String.Equals(WordRules.Normalize(selected), word, StringComparison.Ordinal)) return false;
            }
            return true;
        }
        private static string ReadDocumentSelection(IAccessible document, uint pid)
        {
            var provider = document as AccessibleServiceProvider;
            if (provider == null) return null;
            Guid service = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            Guid iid = typeof(AccessibleSelectionContainer).GUID;
            IntPtr pointer;
            if (provider.QueryService(ref service, ref iid, out pointer) < 0 || pointer == IntPtr.Zero) return null;
            AccessibleSelectionContainer container;
            try { container = (AccessibleSelectionContainer)Marshal.GetObjectForIUnknown(pointer); }
            finally { Marshal.Release(pointer); }
            IntPtr selections; int count;
            if (container.Selections(out selections, out count) < 0) return null;
            try
            {
                if (count != 1 || selections == IntPtr.Zero) return count == 0 ? "unavailable|" : "ignored|";
                var range = (AccessibleSelection)Marshal.PtrToStructure(selections, typeof(AccessibleSelection));
                if (range.Start == IntPtr.Zero || range.End == IntPtr.Zero) return "unavailable|";
                Guid unknown = new Guid("00000000-0000-0000-C000-000000000046");
                IntPtr first, last;
                Marshal.QueryInterface(range.Start, ref unknown, out first); Marshal.QueryInterface(range.End, ref unknown, out last);
                bool same;
                try { same = first != IntPtr.Zero && first == last; }
                finally { if (first != IntPtr.Zero) Marshal.Release(first); if (last != IntPtr.Zero) Marshal.Release(last); }
                // Reject cross-object ranges instead of reading a cropped word out of a sentence.
                if (!same) return "ignored|";
                int start = Math.Min(range.StartOffset, range.EndOffset), end = Math.Max(range.StartOffset, range.EndOffset);
                if (start < 0 || end <= start || (long)end - start > 128) return "ignored|";
                object item = Marshal.GetObjectForIUnknown(range.Start);
                var accessible = item as IAccessible;
                if (!Belongs(accessible, pid) || ProtectedAncestry(accessible, pid)) return "blocked|";
                string selected;
                if (((AccessibleText)item).Text(start, end, out selected) < 0) return "unavailable|";
                string word = WordRules.Normalize(selected);
                return word == null ? "ignored|" : "word|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(word));
            }
            finally
            {
                if (selections != IntPtr.Zero)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var range = (AccessibleSelection)Marshal.PtrToStructure(IntPtr.Add(selections, i * Marshal.SizeOf(typeof(AccessibleSelection))), typeof(AccessibleSelection));
                        if (range.Start != IntPtr.Zero) Marshal.Release(range.Start);
                        if (range.End != IntPtr.Zero) Marshal.Release(range.End);
                    }
                    Marshal.FreeCoTaskMem(selections);
                }
            }
        }
        public static string Read(IntPtr window, int x, int y)
        {
            try
            {
                uint pid = Native.ProcessOf(window);
                IAccessible hit; object child;
                if (AccessibleObjectFromPoint(new Native.Point { X = x, Y = y }, out hit, out child) < 0 || !Belongs(hit, pid)) return "unavailable|";
                // Electron's native Views overlay can mask the renderer in global hit testing.
                // Obtain the visible renderer's MSAA document directly, then hit-test within it.
                IntPtr renderer = IntPtr.Zero;
                int scanned = 0;
                EnumChildWindows(window, delegate(IntPtr candidate, IntPtr unused) {
                    var name = new System.Text.StringBuilder(128); GetClassName(candidate, name, name.Capacity);
                    Rect bounds;
                    if (name.ToString() == "Chrome_RenderWidgetHostHWND" && IsWindowVisible(candidate) && GetWindowRect(candidate, out bounds)
                        && x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom) { renderer = candidate; return false; }
                    return ++scanned < 64;
                }, IntPtr.Zero);
                if (renderer != IntPtr.Zero)
                {
                    Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); object document;
                    if (AccessibleObjectFromWindow(renderer, 0xFFFFFFFC, ref iid, out document) >= 0 && document is IAccessible)
                    {
                        hit = (IAccessible)document; child = 0;
                        string whole = ReadDocumentSelection(hit, pid);
                        if (whole != null) return Native.GetForegroundWindow() == window ? whole : "stale|";
                    }
                }
                if (Protected(hit, child)) return "blocked|";
                if (child is int && (int)child != 0) hit = hit.get_accChild(child) as IAccessible ?? hit;
                for (int depth = 0; depth < 16; depth++)
                {
                    object target = hit.accHitTest(x, y);
                    var deeper = target as IAccessible;
                    if (target is int && (int)target != 0) deeper = hit.get_accChild(target) as IAccessible;
                    if (deeper == null || Object.ReferenceEquals(deeper, hit) || !Belongs(deeper, pid)) break;
                    hit = deeper;
                    if (Protected(hit, 0)) return "blocked|";
                }
                if (ProtectedAncestry(hit, pid)) return "blocked|";
                var clock = Stopwatch.StartNew();
                for (int depth = 0; depth < 16 && Belongs(hit, pid) && clock.ElapsedMilliseconds < 500; depth++)
                {
                    if (Protected(hit, 0)) return "blocked|";
                    AccessibleText text = AsText(hit);
                    int count, start, end;
                    if (text != null && text.SelectionCount(out count) >= 0 && count > 0)
                    {
                        if (count != 1) return "ignored|";
                        if (text.Selection(0, out start, out end) >= 0)
                        {
                            if (start > end) { int swap = start; start = end; end = swap; }
                            if (start < 0 || (long)end - start > 128) return "ignored|";
                            string selected;
                            if (end > start && text.Text(start, end, out selected) >= 0)
                            {
                                // An embedded-object marker means the actual selection is in a descendant.
                                if (selected != null && selected.IndexOf('\uFFFC') < 0)
                                {
                                    string word = WordRules.Normalize(selected);
                                    if (word != null && !ParentSelectionMatches(hit.accParent as IAccessible, pid, word)) return "ignored|";
                                    if (Native.GetForegroundWindow() != window) return "stale|";
                                    return word == null ? "ignored|" : "word|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(word));
                                }
                            }
                        }
                    }
                    hit = hit.accParent as IAccessible;
                }
            }
            catch (COMException) { }
            catch (InvalidCastException) { }
            catch (ArgumentException) { }
            return "unavailable|";
        }
    }
}
