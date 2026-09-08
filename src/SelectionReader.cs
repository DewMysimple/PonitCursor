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
                    if (parts.Length != 5) { Console.WriteLine("error|"); continue; }
                    var window = new IntPtr(Int64.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                    int x = Int32.Parse(parts[2]), y = Int32.Parse(parts[3]);
                    string result = Read(window, x, y, parts[4] == "guard");
                    // Chromium may initialize its accessibility tree asynchronously on
                    // the first query. One bounded retry also refreshes stale hit testing.
                    if (result == "unavailable|" && parts[4] != "guard" && Native.GetForegroundWindow() == window)
                    { System.Threading.Thread.Sleep(60); result = Read(window, x, y, false); }
                    Console.WriteLine(parts[0] + "|" + result);
                }
                catch (Exception) { Console.WriteLine("error|"); }
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

        private static string Read(IntPtr window, int x, int y, bool guardOnly)
        {
            if (window == IntPtr.Zero || Native.GetForegroundWindow() != window) return "stale|";
            uint pid = Native.ProcessOf(window);
            AutomationElement focused = AutomationElement.FocusedElement;
            if (!Belongs(focused, pid)) focused = null;
            if (focused != null && IsPassword(focused, pid)) return "blocked|";
            if (LegacySelection.ProtectedFocus(window)) return "blocked|";
            if (guardOnly) return focused == null ? "unavailable|" : "safe|";
            AutomationElement hit = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (!Belongs(hit, pid)) return "unavailable|";
            if (IsPassword(hit, pid)) return "blocked|";
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
                                    string word = WordRules.Normalize(text);
                                    if (Native.GetForegroundWindow() != window) return "stale|";
                                    return word == null ? "ignored|" : "word|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(word));
                                }
                            }
                        }
                    }
                    current = TreeWalker.ControlViewWalker.GetParent(current);
                }
            }
            return LegacySelection.Read(window, x, y);
        }
    }
}
