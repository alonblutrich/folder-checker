using FolderScanner;
namespace ResonancePilot;
// One bounded sequence of synthetic snapshots, generated once. No files or engine calls.
internal sealed class DemoTelemetrySequence
{
    internal IReadOnlyList<ScanTelemetrySnapshot> Frames {get;}
    internal DemoTelemetrySequence()
    {
        var frames=new List<ScanTelemetrySnapshot>();var timeline=new List<TelemetrySample>();int previousAck=0;
        for(int i=0;i<120;i++){
            int discovered=i==0?0:5+i*3+i/7*2,completed=Math.Max(0,discovered-6-i%5),unknown=completed/31,errors=completed/47,findings=completed/43,ack=completed-unknown-errors;
            timeline.Add(new(i*.25,discovered,completed,ack,i==0?null:(ack-previousAck)/.25));previousAck=ack;
            var extensions=Partition(discovered,["txt/md","pdf","jpg/png","zip","exe/dll","ללא סיומת","אחר"],[6,3,4,2,1,1,3]);
            int missing=discovered/17;var sizes=Partition(discovered-missing,["0 B","<1 KiB","<64 KiB","<1 MiB","<16 MiB","16+ MiB"],[1,2,5,4,2,1]);long[] sampleSizes=[0,200,10_000,350_000,4_000_000,25_000_000];long bytes=sizes.Select((b,j)=>b.Count*sampleSizes[j]).Sum();
            frames.Add(new(TimeSpan.FromSeconds(i*.25),timeline.ToArray(),extensions,sizes,[new("NoThreatsReported",ack-findings),new("Threat",findings),new("Unknown",unknown),new("Error",errors)],discovered==0?null:bytes,discovered-missing,missing,discovered,completed,ack,findings,i/40,i==119,false,i==119,"DEMO · "+(i<30?"גילוי מבנה מדומה":i<85?"קליטת תוצאות מדומות":"סיכום רצף מדומה")));
        }
        Frames=frames.AsReadOnly();
    }
    static TelemetryBucket[] Partition(int count,string[] names,int[] weights){int total=weights.Sum(),used=0;var result=new TelemetryBucket[names.Length];for(int i=0;i<names.Length;i++){int n=i==names.Length-1?count-used:count*weights[i]/total;result[i]=new(names[i],n);used+=n;}return result;}
}
