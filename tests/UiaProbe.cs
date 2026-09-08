using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
class UiaProbe
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd, EnumProc fn, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder text,int max);
    [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);
    static void Show(AutomationElement item)
    {
        for(int i=0;i<10 && item!=null;i++)
        {
            Console.WriteLine(item.Current.ClassName+" "+item.Current.ControlType.ProgrammaticName+" pid="+item.Current.ProcessId+" text="+item.GetCurrentPropertyValue(AutomationElement.IsTextPatternAvailableProperty));
            item=TreeWalker.ControlViewWalker.GetParent(item);
        }
    }
    [MTAThread] static void Main(string[] args)
    {
        IntPtr hwnd=new IntPtr(long.Parse(args[0]));
        Console.WriteLine("BEFORE");Show(AutomationElement.FocusedElement);
        EnumChildWindows(hwnd,delegate(IntPtr child,IntPtr unused){
            var name=new System.Text.StringBuilder(256);GetClassName(child,name,256);Console.WriteLine("Native "+name);
            if(name.ToString().Contains("RenderWidget"))
            {
                Guid iid=new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");object obj;
                int hr=AccessibleObjectFromWindow(child,0xFFFFFFFC,ref iid,out obj);Console.WriteLine("AccessibleObject "+hr);
                if(obj!=null && Marshal.IsComObject(obj))Marshal.FinalReleaseComObject(obj);
            }
            return true;
        },IntPtr.Zero);
        Thread.Sleep(300);Console.WriteLine("AFTER");Show(AutomationElement.FocusedElement);
    }
}
