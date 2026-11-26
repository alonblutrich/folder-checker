using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace FolderScanner;

public sealed class WindowsScanEvidenceProvider : IScanEvidenceProvider
{
    public EvidenceSnapshot Capture(string path)
    {
        using var scope=WindowsLease.Acquire(path);
        return scope.Initial;
    }
    sealed class WindowsLease : IDisposable
    {
        readonly List<AncestorIdentity> parents=[];
        FileStream? stream;
        bool disposed;
        public EvidenceSnapshot Initial {get;private set;}=null!;
        const uint UnsafeAttributes=0x400|0x1000|0x40000|0x400000;
        static IOException Error(string action)=>new(action,new Win32Exception(Marshal.GetLastWin32Error()));
        public static WindowsLease Acquire(string path)
        {
            if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
            // Only canonical DOS drive paths, excluding device namespaces, streams and aliases.
            if(path.Length<4||!char.IsAsciiLetter(path[0])||path[1]!=':'||path[2]!='\\'||path[3..].Contains(':')||path.Contains('/'))
                throw new ArgumentException("Ordinary local DOS path required");
            string full=Path.GetFullPath(path);
            if(!string.Equals(path,full,StringComparison.OrdinalIgnoreCase)||
                path[3..].Split('\\').Any(p=>p.Length==0||p.EndsWith('.')||p.EndsWith(' ')))throw new ArgumentException("Noncanonical path");
            if(new DriveInfo(Path.GetPathRoot(full)!).DriveType!=DriveType.Fixed)throw new IOException("Not fixed local storage");
            var lease=new WindowsLease();
            try
            {
                var chain=new Stack<string>();
                for(string? p=Path.GetDirectoryName(full);p!=null;p=Path.GetDirectoryName(p))chain.Push(p);
                if(chain.Count>128)throw new IOException("Evidence ancestor limit");
                while(chain.Count>0)
                {
                    var p=chain.Pop();
                    // S1 bounded stable-folder contract: attributes only, no ancestor handles.
                    lease.parents.Add(ReadAncestor(p));
                }
                // Verified read handle for this capture only; no report-lifetime lock.
                var file=CreateFileW(full,0x80000000,1,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
                if(file.IsInvalid){file.Dispose();throw Error("Open file");}
                try{lease.stream=new FileStream(file,FileAccess.Read);}catch{file.Dispose();throw;}
                var before=Read(file,false,full);
                var hash=lease.Hash();
                if(before!=Read(file,false,full))throw new IOException("Metadata changed during capture");
                lease.Initial=new(full,before with{Sha256=hash},Array.AsReadOnly(lease.parents.ToArray()));
                if(!lease.Validate())throw new IOException("Evidence changed at capture");
                return lease;
            }
            catch{lease.Dispose();throw;}
        }
        string Hash()
        {
            stream!.Position=0;
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        static AncestorIdentity ReadAncestor(string path)
        {
            uint attributes=(uint)File.GetAttributes(path);
            if((attributes&UnsafeAttributes)!=0||(attributes&0x10)==0)throw new IOException("Unsafe ancestor attributes: "+path);
            return new(path,attributes);
        }
        public bool Validate()
        {
            if(disposed||stream==null)return false;
            foreach(var parent in parents)
            {
                if(ReadAncestor(parent.Path)!=parent)return false;
            }
            var file=Read(stream.SafeFileHandle,false,Initial.Path);
            return (file with{Sha256=Hash()})==Initial.File && Read(stream.SafeFileHandle,false,Initial.Path)==file;
        }
        static ObjectIdentity Read(SafeFileHandle handle,bool directory,string expectedPath)
        {
            if(GetFileType(handle)!=1)throw new IOException("Not a disk object");
            if(!GetFileInformationByHandle(handle,out var info))throw Error("Read attributes");
            if((info.Attributes&UnsafeAttributes)!=0||((info.Attributes&0x10)!=0)!=directory||(!directory&&info.Links!=1))
                throw new IOException("Unsafe attributes/type/link count");
            if(!GetFileInformationByHandleEx(handle,18,out var id,(uint)Marshal.SizeOf<IdInfo>()))throw Error("Read 128-bit identity");
            if(id.Volume==0||(id.High==0&&id.Low==0))throw new IOException("Missing stable file identity");
            var name=new StringBuilder(32768);uint length=GetFinalPathNameByHandleW(handle,name,(uint)name.Capacity,0);
            if(length==0||length>=name.Capacity)throw Error("Resolve held object name");
            var resolved=name.ToString();if(resolved.StartsWith(@"\\?\"))resolved=resolved[4..];
            if(!string.Equals(Path.TrimEndingDirectorySeparator(resolved),Path.TrimEndingDirectorySeparator(expectedPath),StringComparison.OrdinalIgnoreCase))
                throw new IOException("Held object path differs");
            return new(id.Volume,$"{id.High:X16}{id.Low:X16}","",info.Links,((long)info.SizeHigh<<32)|info.SizeLow);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            stream?.Dispose();stream=null;
            parents.Clear();
        }
        [StructLayout(LayoutKind.Sequential)]struct Info {public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
        [StructLayout(LayoutKind.Sequential)]struct IdInfo {public ulong Volume,Low,High;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandle(SafeFileHandle h,out Info info);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandleEx(SafeFileHandle h,int kind,out IdInfo info,uint size);
        [DllImport("kernel32.dll",SetLastError=true)]static extern uint GetFileType(SafeFileHandle h);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandleW(SafeFileHandle h,StringBuilder path,uint length,uint flags);
    }
}
