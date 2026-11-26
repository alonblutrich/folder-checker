using System.Runtime.InteropServices;
using System.Text;
namespace FolderScanner;
internal static class NativeDesktopGuard
{
    // Observation only; no desktop switching, elevation or alternate execution context.
    internal static bool IsSupported()
    {
        if(!OperatingSystem.IsWindows()||!Environment.UserInteractive||Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)return false;
        string station=Name(GetProcessWindowStation()),desktop=Name(GetThreadDesktop(GetCurrentThreadId()));
        var input=OpenInputDesktop(0,false,1);if(input==IntPtr.Zero)return false;
        try{return station.Equals("WinSta0",StringComparison.OrdinalIgnoreCase)&&desktop.Equals("Default",StringComparison.OrdinalIgnoreCase)&&desktop.Equals(Name(input),StringComparison.OrdinalIgnoreCase);}
        finally{CloseDesktop(input);}
    }
    static string Name(IntPtr handle)
    {
        var text=new StringBuilder(256);
        return GetUserObjectInformationW(handle,2,text,512,out _)?text.ToString():"unavailable";
    }
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr OpenInputDesktop(uint flags,[MarshalAs(UnmanagedType.Bool)]bool inherit,uint access);
    [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformationW(IntPtr handle,int index,StringBuilder value,int length,out int needed);
}
