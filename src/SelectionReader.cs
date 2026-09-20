using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace PointCursor
{
    // Separate MTA process: an unresponsive third-party UIA provider can be terminated safely.
    internal static class SelectionReader
    {
        [MTAThread]
        public static void Main()
        {
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                try
                {
                    string[] parts = line.Split('|');
                    if (parts.Length != 5 && parts.Length != 7) { Console.WriteLine("error|"); continue; }
                    var window = new IntPtr(Int64.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                    int startX = Int32.Parse(parts[2]), startY = Int32.Parse(parts[3]);
                    int endX = parts.Length == 7 ? Int32.Parse(parts[4]) : startX;
                    int endY = parts.Length == 7 ? Int32.Parse(parts[5]) : startY;
                    string mode = parts[parts.Length - 1];
                    string result = Read(window, startX, startY, endX, endY, mode == "guard", mode == "gesture" || mode == "double", mode == "double");
                    // Chromium may initialize its accessibility tree asynchronously on
                    // the first query. Bounded retries also refresh stale hit testing.
                    for (int retry = 0; result == "unavailable|" && mode != "guard" && retry < 2; retry++)
                    { System.Threading.Thread.Sleep(retry == 0 ? 35 : 75); result = Read(window, startX, startY, endX, endY, false, mode == "gesture" || mode == "double", mode == "double"); }
                    Console.WriteLine(parts[0] + "|" + result);
                }
                catch (Exception) { Console.WriteLine(line.Split('|')[0] + "|unavailable|"); }
            }
        }

        private static bool Belongs(AutomationElement element, uint pid)
        { return element != null && element.Current.ProcessId == pid; }
        private static bool IsPassword(AutomationElement element, uint pid)
        {
            for (int i = 0; i < 12 && Belongs(element, pid); i++)
            {
                if (element.Current.IsPassword) return true;
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
            return false;
        }

        private static string EncodeWord(string text)
        {
            string word = WordRules.Normalize(text);
            return word == null ? "ignored|" : "word|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(word));
        }

        private static string EncodeGestureWord(string text, TextPatternRange range, TextPattern pattern, int x, int y)
        {
            string context = null;
            string enclosing = null;
            try
            {
                TextPatternRange line = range.Clone();
                line.ExpandToEnclosingUnit(TextUnit.Line);
                context = line.GetText(2049);
                TextPatternRange wordRange = pattern == null ? range.Clone() : pattern.RangeFromPoint(new System.Windows.Point(x, y));
                if (wordRange != null)
                {
                    wordRange.ExpandToEnclosingUnit(TextUnit.Word);
                    enclosing = wordRange.GetText(129);
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            string word = WordRules.ReconcileGesture(text, context, enclosing);
            return word == null ? "ignored|" : "word|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(word));
        }

        private static bool ReadGestureRange(TextPattern pattern, int startX, int startY, int endX, int endY, bool doubleClick, out string result)
        {
            result = null;
            try
            {
                TextPatternRange first = pattern.RangeFromPoint(new System.Windows.Point(startX, startY));
                if (first == null) return false;
                TextPatternRange range;
                if (doubleClick)
                {
                    range = first.Clone();
                    range.ExpandToEnclosingUnit(TextUnit.Word);
                }
                else
                {
                    TextPatternRange last = pattern.RangeFromPoint(new System.Windows.Point(endX, endY));
                    if (last == null) return false;
                    int order = first.CompareEndpoints(TextPatternRangeEndpoint.Start, last, TextPatternRangeEndpoint.Start);
                    if (order == 0) return false;
                    range = (order < 0 ? first : last).Clone();
                    TextPatternRange upper = order < 0 ? last : first;
                    range.MoveEndpointByRange(TextPatternRangeEndpoint.End, upper, TextPatternRangeEndpoint.Start);
                }
                string text = range.GetText(129);
                if (String.IsNullOrWhiteSpace(text)) return false;
                result = EncodeGestureWord(text, range, pattern, (startX + endX) / 2, (startY + endY) / 2);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (ElementNotAvailableException) { return false; }
        }

        private static AutomationElement DescendAtPoint(AutomationElement root, uint pid, int x, int y)
        {
            if (!Belongs(root, pid)) return null;
            AutomationElement current = root;
            var clock = Stopwatch.StartNew();
            int scanned = 0;
            for (int depth = 0; depth < 16 && clock.ElapsedMilliseconds < 300 && scanned < 160; depth++)
            {
                AutomationElement best = null;
                double bestArea = Double.MaxValue;
                AutomationElement child = TreeWalker.ControlViewWalker.GetFirstChild(current);
                while (child != null && scanned++ < 160 && clock.ElapsedMilliseconds < 300)
                {
                    try
                    {
                        if (Belongs(child, pid))
                        {
                            System.Windows.Rect bounds = child.Current.BoundingRectangle;
                            if (!bounds.IsEmpty && bounds.Contains(new System.Windows.Point(x, y)))
                            {
                                double area = bounds.Width * bounds.Height;
                                if (area < bestArea) { best = child; bestArea = area; }
                            }
                        }
                        child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                    }
                    catch (ElementNotAvailableException) { break; }
                }
                if (best == null) break;
                current = best;
            }
            return current;
        }

        private static AutomationElement ElementAtGesture(IntPtr window, uint pid, int x, int y)
        {
            try
            {
                AutomationElement hit = AutomationElement.FromPoint(new System.Windows.Point(x, y));
                if (Belongs(hit, pid)) return hit;
            }
            catch (ElementNotAvailableException) { }
            catch (ArgumentException) { }
            try
            {
                IntPtr child = Native.DescendantWindowAtPoint(window, x, y);
                if (child == IntPtr.Zero || Native.ProcessOf(child) != pid) child = window;
                return DescendAtPoint(AutomationElement.FromHandle(child), pid, x, y);
            }
            catch (ElementNotAvailableException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static string Read(IntPtr window, int startX, int startY, int endX, int endY, bool guardOnly, bool allowGestureRange, bool doubleClick)
        {
            if (window == IntPtr.Zero) return "stale|";
            bool isForeground = Native.GetForegroundWindow() == window;
            if (guardOnly && !isForeground) return "stale|";
            uint pid = Native.ProcessOf(window);
            if (pid == 0) return "stale|";
            // Chromium exposes its document directly through IA2. Avoid spending the
            // request budget walking UIA wrappers that have no TextPattern.
            if (isForeground && LegacySelection.ProtectedFocus(window)) return "blocked|";
            IntPtr renderer = guardOnly ? IntPtr.Zero : LegacySelection.RendererAtPoint(window, endX, endY);
            if (renderer != IntPtr.Zero)
            {
                string legacy = LegacySelection.Read(window, startX, startY, endX, endY, allowGestureRange, doubleClick, renderer);
                if (legacy != "unavailable|") return legacy;
            }
            AutomationElement focused = isForeground ? AutomationElement.FocusedElement : null;
            if (!Belongs(focused, pid)) focused = null;
            if (focused != null && IsPassword(focused, pid)) return "blocked|";
            if (guardOnly) return focused == null ? "unavailable|" : "safe|";
            AutomationElement hit = ElementAtGesture(window, pid, endX, endY);
            if (hit != null && IsPassword(hit, pid)) return "blocked|";
            string selected = ReadUia(hit, focused, pid, startX, startY, endX, endY, allowGestureRange, doubleClick);
            return selected ?? LegacySelection.Read(window, startX, startY, endX, endY, allowGestureRange, doubleClick);
        }

        private static string ReadUia(AutomationElement hit, AutomationElement focused, uint pid,
            int startX, int startY, int endX, int endY, bool allowGestureRange, bool doubleClick)
        {
            try
            {
                var visited = new HashSet<string>();
                var clock = Stopwatch.StartNew();
                // Query the selected control and its ancestors; never walk every desktop element.
                foreach (AutomationElement origin in new[] { hit, focused })
                {
                    AutomationElement current = origin;
                    for (int depth = 0; depth < 16 && Belongs(current, pid) && clock.ElapsedMilliseconds < 600; depth++)
                    {
                        if (current.Current.IsPassword) return "blocked|";
                        string id = String.Join(",", current.GetRuntimeId());
                        if (visited.Add(id))
                        {
                            object pattern;
                            if (current.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                            {
                                TextPatternRange[] ranges = ((TextPattern)pattern).GetSelection();
                                if (ranges != null && ranges.Length > 1) return "ignored|";
                                if (ranges != null && ranges.Length == 1)
                                {
                                    string text = ranges[0].GetText(129);
                                    if (!String.IsNullOrEmpty(text))
                                    {
                                        return allowGestureRange ? EncodeGestureWord(text, ranges[0], (TextPattern)pattern, (startX + endX) / 2, (startY + endY) / 2) : EncodeWord(text);
                                    }
                                }
                                string gesture;
                                if (allowGestureRange && ReadGestureRange((TextPattern)pattern, startX, startY, endX, endY, doubleClick, out gesture)) return gesture;
                            }
                        }
                        current = TreeWalker.ControlViewWalker.GetParent(current);
                    }
                }
            }
            // A stale UIA wrapper must not prevent the independently guarded IA2 path.
            catch (ElementNotAvailableException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            return null;
        }
    }
}
