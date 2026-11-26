using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace FolderScanner;

public sealed class PathLease : IDisposable
{
    private readonly List<SafeFileHandle> handles = [];
    public static PathLease Acquire(string path, bool includeFile)
    {
        var lease = new PathLease();
        try
        {
            var chain = new Stack<string>();
            for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part)) chain.Push(part);
            while (chain.Count > 0)
            {
                var part = chain.Pop();
                bool directory = Directory.Exists(part);
                if (!directory && !includeFile) continue;
                // Open the reparse point itself. Deny delete/rename for every ancestor and writes for the file.
                var h = CreateFileW(part, directory ? 0u : 0x80000000u, directory ? 3u : 1u,
                    IntPtr.Zero, 3, 0x00200000u | (directory ? 0x02000000u : 0u), IntPtr.Zero);
                if (h.IsInvalid) { h.Dispose(); throw new IOException($"לא ניתן לנעול את זהות הנתיב (Win32 {Marshal.GetLastWin32Error()})"); }
                lease.handles.Add(h);
                if (!GetFileInformationByHandle(h, out var info)) throw new IOException("לא ניתן לאמת מאפייני אובייקט");
                if ((info.Attributes & (0x400u | 0x1000u)) != 0) throw new IOException("קישור או תוכן לא מקומי — דולג");
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    public void Dispose() { foreach (var h in handles.AsEnumerable().Reverse()) h.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo
    { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);
}

public static class EngineTrust
{
    public static bool Verify(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, ptr, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UIChoice = 2,
                UnionChoice = 1, File = ptr, StateAction = 0, ProviderFlags = 0x1000 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return certificate.GetNameInfo(X509NameType.SimpleName, false) == "Microsoft Windows Publisher";
        }
        catch { return false; }
        finally { Marshal.DestroyStructure<TrustFile>(ptr); Marshal.FreeHGlobal(ptr); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TrustFile
    { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr FileHandle, KnownSubject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    { public uint Size; public IntPtr Policy, Sip; public uint UIChoice, Revocation, UnionChoice; public IntPtr File; public uint StateAction; public IntPtr StateData, Url; public uint ProviderFlags, UIContext; public IntPtr SignatureSettings; }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
}
