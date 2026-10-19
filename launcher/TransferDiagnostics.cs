using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace MPFever
{
    // One owner per operation; readers never take the disk/network owner's lock.
    // Active time is included, so a stalled socket/flush is visible before it ends.
    sealed class OperationMeter
    {
        readonly Func<long> clock;
        readonly double frequency;
        long started,total,calls,bytes;
        int active;
        public OperationMeter(Func<long> clock=null,long frequency=0)
        {this.clock=clock??Stopwatch.GetTimestamp;this.frequency=frequency>0?frequency:Stopwatch.Frequency;}
        public void Begin() {Interlocked.Exchange(ref started,clock());Volatile.Write(ref active,1);}
        public void End(long count=0)
        {
            if(Interlocked.Exchange(ref active,0)==0)return;
            Interlocked.Add(ref total,Math.Max(0,clock()-Interlocked.Read(ref started)));
            Interlocked.Add(ref bytes,count);Interlocked.Increment(ref calls);
        }
        public OperationSample Capture()
        {
            long busy=Volatile.Read(ref active)==0?0:Math.Max(0,clock()-Interlocked.Read(ref started));
            return new OperationSample {Milliseconds=1000*(Interlocked.Read(ref total)+busy)/frequency,
                ActiveMilliseconds=1000*busy/frequency,Calls=Interlocked.Read(ref calls),Bytes=Interlocked.Read(ref bytes)};
        }
    }
    struct OperationSample
    {
        public double Milliseconds,ActiveMilliseconds;
        public long Calls,Bytes;
        public double Since(OperationSample old) => Math.Max(0,Milliseconds-old.Milliseconds);
        public static string Ms(double value) => value.ToString("0.0",CultureInfo.InvariantCulture);
    }
    // Payload throughput, not TCP/base64 bytes. Reused bytes are excluded from
    // both rates. Elapsed is supplied from a monotonic Stopwatch in production.
    sealed class TransferRate
    {
        readonly long reused;
        readonly double started;
        long lastBytes;
        double lastAt;
        public TransferRate(long reused,double started=0)
        {if(reused<0)throw new ArgumentOutOfRangeException(nameof(reused));this.reused=lastBytes=reused;this.started=lastAt=started;}
        public RateSample Take(long verified,double elapsed)
        {
            if(verified<lastBytes || elapsed<lastAt || double.IsNaN(elapsed) || double.IsInfinity(elapsed))throw new ArgumentOutOfRangeException("Transfer counters must be monotonic");
            var sample=new RateSample {IntervalBytes=verified-lastBytes,IntervalSeconds=elapsed-lastAt,
                TotalBytes=verified-reused,TotalSeconds=elapsed-started};lastBytes=verified;lastAt=elapsed;return sample;
        }
    }
    struct RateSample
    {
        public long IntervalBytes,TotalBytes;
        public double IntervalSeconds,TotalSeconds;
        public long CurrentBps => IntervalSeconds>0?(long)(IntervalBytes/IntervalSeconds):0;
        public long AverageBps => TotalSeconds>0?(long)(TotalBytes/TotalSeconds):0;
    }
}
