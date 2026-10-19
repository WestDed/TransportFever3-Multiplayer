using System;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static void TransferDiagnosticsTests()
        {
            var rate=new TransferRate(0);
            var fast=rate.Take(64L*1024*1024,40);
            var slow=rate.Take(65L*1024*1024,50);
            var steady=rate.Take(66L*1024*1024,60);
            Check(fast.CurrentBps>slow.CurrentBps*10,"interval rate detects a real throughput cliff after an initial burst");
            Check(slow.CurrentBps==steady.CurrentBps && steady.AverageBps<slow.AverageBps,"steady slow transfer is distinguished from its still-falling cumulative average");
            var stalled=rate.Take(66L*1024*1024,70);
            Check(stalled.CurrentBps==0 && stalled.AverageBps>0,"no incoming bytes reports zero current speed rather than stale positive average");
            var resumed=new TransferRate(64L*1024*1024).Take(65L*1024*1024,10);
            Check(resumed.TotalBytes==1024*1024 && resumed.AverageBps==resumed.CurrentBps,"reused bytes never inflate either resume throughput rate");
            Check(rate.Take(66L*1024*1024,70).CurrentBps==0,"zero-duration observation has bounded zero rate");
            Reject<ArgumentOutOfRangeException>(()=>rate.Take(1,71),"new transfer counters cannot silently reuse old rate state");
            Reject<ArgumentOutOfRangeException>(()=>rate.Take(66L*1024*1024,69),"backward time cannot create negative throughput");
            foreach(double bad in new[]{double.NaN,double.PositiveInfinity})Reject<ArgumentOutOfRangeException>(()=>rate.Take(66L*1024*1024,bad),"nonfinite elapsed time refused");
            long tick=100;var operation=new OperationMeter(()=>tick,1000);
            Check(operation.Capture().Calls==0 && operation.Capture().Milliseconds==0,"idle operation has no invented I/O time");
            operation.Begin();tick+=700;
            var active=operation.Capture();Check(active.ActiveMilliseconds==700 && active.Milliseconds==700 && active.Calls==0,"in-flight socket or checkpoint stall is visible before operation returns");
            operation.End(4096);tick+=1000;
            var completed=operation.Capture();Check(completed.Milliseconds==700 && completed.ActiveMilliseconds==0 && completed.Calls==1 && completed.Bytes==4096,"completed I/O time and bytes exclude subsequent idle time");
            operation.Begin();tick+=300;operation.End(17);
            var next=operation.Capture();Check(next.Since(completed)==300 && next.Bytes==4113 && next.Calls==2,"consecutive diagnostic windows count their own I/O only");
            using(var relay=new Relay())using(var client=new Client())
            {
                string payload=null;
                relay.Joined+=peer=>relay.Send(peer,Msg.Make("chatmsg","Host","{[\"text\"]=\"unchanged\"}"));
                client.Received+=message=> {if(message.Kind=="chatmsg")payload=message.Payload;};
                relay.Start(0);client.Connect("127.0.0.1",relay.Port,"meter");
                Wait(()=>payload!=null && client.HandlerTiming.Capture().Calls>0,"actual TCP handler timing completes with measured dispatch");
                Check(payload=="{[\"text\"]=\"unchanged\"}" && client.SocketReadTiming.Capture().Bytes>0 && client.FrameReadTiming.Capture().Calls>0,"instrumentation preserves exact message and counts actual socket reads/frames");
                Wait(()=>relay.Peers()[0].Outbox.WriteTiming.Capture().Bytes>0,"actual bounded outbox records bytes written without changing queue semantics");
            }
        }
    }
}
