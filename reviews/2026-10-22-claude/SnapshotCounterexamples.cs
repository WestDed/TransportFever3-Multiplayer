using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MPFever
{
    static class Review
    {
        static int failed;
        static void Require(bool ok,string label)
        {Console.WriteLine((ok?"PASS ":"FAIL ")+label);if(!ok)failed++;}
        static List<Msg> Parts(string root)
        {
            Directory.CreateDirectory(root);string name=Path.Combine(root,"source.sav");
            var bytes=new byte[SnapshotLimits.ChunkBytes*2+7];new Random(17).NextBytes(bytes);File.WriteAllBytes(name,bytes);
            var frames=new List<Msg>();using(var source=new SnapshotSource(name,1))source.SendTo(m=>{frames.Add(m);return true;},"Host",()=>false);
            return frames;
        }
        static int Main()
        {
            string root=Path.Combine(Path.GetTempPath(),"tf3-review-"+Guid.NewGuid().ToString("N"));
            try
            {
                var frames=Parts(root);
                string cancel=Path.Combine(root,"cancel");
                var receiver=new SnapshotReceiver(cancel,resumable:true);receiver.Begin(frames[0].Payload);receiver.Add(frames[1].Payload);
                receiver.Dispose(); // EndSession calls this production API.
                Console.WriteLine("OBSERVATION: Dispose leaves partial="+File.Exists(Path.Combine(cancel,"download.part"))+"; EndSession currently calls this destructive API");

                string verify=Path.Combine(root,"verify");
                using(var seed=new SnapshotReceiver(verify,resumable:true)) {seed.Begin(frames[0].Payload);seed.Add(frames[1].Payload);seed.Suspend();}
                using(var retry=new SnapshotReceiver(verify,resumable:true))
                    try {retry.Begin(frames[0].Payload,()=>true);}catch(OperationCanceledException) { }
                Require(File.Exists(Path.Combine(verify,"download.part")) && File.Exists(Path.Combine(verify,"download.meta")),"Cancelled verification preserves its previous checkpoint");

                string handoff=Path.Combine(root,"handoff");CompletedSnapshot completed=null;
                using(var first=new SnapshotReceiver(handoff,resumable:true))
                {
                    first.Begin(frames[0].Payload);foreach(var frame in frames.Skip(1))completed=first.Add(frame.Payload);
                    using(var next=new SnapshotReceiver(handoff,resumable:true))
                    {
                        next.Begin(frames[0].Payload);next.Add(frames[1].Payload);
                        try {completed.Dispose();}catch(IOException e){Console.WriteLine("OLD DISPOSE EXCEPTION "+e.Message);}
                        Require(File.Exists(Path.Combine(handoff,"download.part")),"Disposing the old completed object preserves the new download's file");
                        next.Suspend();
                    }
                }

                var recovery=new RecoveryGate();bool overridden;
                recovery.Freeze();recovery.Failure(0);
                string host=recovery.Resume(false,true,out overridden);
                string peer=recovery.Resume(false,false,out overridden);
                Console.WriteLine("OBSERVATION: failed recovery, host resume allowed="+(host==null)+", subsequent peer resume allowed="+(peer==null)+", failure cleared="+!recovery.Failed);
                Console.WriteLine("Review regressions failed: "+failed+"; these are counterexamples, not game tests.");return failed==0?0:1;
            }
            finally {Directory.Delete(root,true);}
        }
    }
}
