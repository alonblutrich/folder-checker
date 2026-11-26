using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace FolderScanner;

// Called only through EvidenceDeletion after registry checks and individual confirmation.
// Native behavior remains unverified; this candidate is for scoped application acceptance.
[SupportedOSPlatform("windows")]
public sealed class WindowsRecycleBackend(IScanEvidenceProvider provider,IntPtr owner):IReviewRecycleBackend
{
    const uint Flags=0x00182404;
    public RecycleReceipt RecycleOne(EvidenceSnapshot expected,Func<bool> stillMatches)
    {
        if(!NativeDesktopGuard.IsSupported())return new(false,false,false,null,"InteractiveDesktopUnavailable");
        return PreparedImplementation(expected,stillMatches);
    }

    private RecycleReceipt PreparedImplementation(EvidenceSnapshot expected,Func<bool> stillMatches)
    {
        if(owner==IntPtr.Zero||Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA||!stillMatches()||
            !expected.Matches(provider.Capture(expected.Path)))throw new IOException("Context or identity refused");
        IFileOperation? operation=null;IShellItem? item=null;
        var sink=new Sink(provider,expected,stillMatches);
        try
        {
            operation=(IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
            Check(operation.SetOperationFlags(Flags));Check(operation.SetOwnerWindow(owner));
            var iid=typeof(IShellItem).GUID;Check(SHCreateItemFromParsingName(expected.Path,IntPtr.Zero,ref iid,out item));
            Check(operation.DeleteItem(item,sink));int performed=operation.PerformOperations();
            Check(operation.GetAnyOperationsAborted(out bool aborted));
            bool missing=GetFileAttributesW(expected.Path)==uint.MaxValue&&Marshal.GetLastWin32Error() is 2 or 3;
            return new(performed>=0&&!aborted&&sink.HasSingleCallbackPair,aborted,missing,sink.RecycledIdentity);
        }
        finally
        {
            if(item!=null)Marshal.FinalReleaseComObject(item);
            if(operation!=null)Marshal.FinalReleaseComObject(operation);
        }
    }
    static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
    static string Name(IShellItem item)
    {
        Check(item.GetDisplayName(0x80058000,out var value));
        try{return Marshal.PtrToStringUni(value)??throw new IOException("Missing item path");}
        finally{Marshal.FreeCoTaskMem(value);}
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    sealed class Sink(IScanEvidenceProvider provider,EvidenceSnapshot expected,Func<bool> stillMatches):IFileOperationProgressSink
    {
        int preCalls,postCalls;public bool PreAccepted,PostSuccess;public ObjectIdentity? RecycledIdentity;
        public bool HasSingleCallbackPair=>preCalls==1&&postCalls==1&&PreAccepted&&PostSuccess;
        const int Reject=unchecked((int)0x80004004);
        public int PreDeleteItem(uint flags,IShellItem item)
        {
            try
            {
                if(++preCalls!=1||(flags&0x80)==0||!string.Equals(Name(item),expected.Path,StringComparison.OrdinalIgnoreCase)||
                    !stillMatches()||!expected.Matches(provider.Capture(expected.Path)))return Reject;
                PreAccepted=true;return 0;
            }
            catch{return Reject;}
        }
        public int PostDeleteItem(uint flags,IShellItem item,int hr,IShellItem? created)
        {
            try
            {
                if(++postCalls!=1||!PreAccepted||hr<0||created==null){PostSuccess=false;return 0;}
                string destination=Name(created);
                if(!destination.Split('\\').Any(x=>x.Equals("$Recycle.Bin",StringComparison.OrdinalIgnoreCase)))return 0;
                var recycled=provider.Capture(destination);
                if(recycled.File!=expected.File)return 0;
                RecycledIdentity=recycled.File;PostSuccess=true;
            }
            catch{PostSuccess=false;RecycledIdentity=null;}
            return 0;
        }
        public int StartOperations()=>0;public int FinishOperations(int hr)=>0;
        public int PreRenameItem(uint f,IShellItem i,string n)=>Reject;public int PostRenameItem(uint f,IShellItem i,string n,int h,IShellItem c)=>0;
        public int PreMoveItem(uint f,IShellItem i,IShellItem d,string n)=>Reject;public int PostMoveItem(uint f,IShellItem i,IShellItem d,string n,int h,IShellItem c)=>0;
        public int PreCopyItem(uint f,IShellItem i,IShellItem d,string n)=>Reject;public int PostCopyItem(uint f,IShellItem i,IShellItem d,string n,int h,IShellItem c)=>0;
        public int PreNewItem(uint f,IShellItem d,string n)=>Reject;public int PostNewItem(uint f,IShellItem d,string n,string t,uint a,int h,IShellItem c)=>0;
        public int UpdateProgress(uint a,uint b)=>0;public int ResetTimer()=>0;public int PauseTimer()=>0;public int ResumeTimer()=>0;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]static extern int SHCreateItemFromParsingName(string path,IntPtr bind,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IShellItem item);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFileAttributesW(string path);
}
