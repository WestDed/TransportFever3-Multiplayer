using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MPFever
{
    // Slow links (a relayed tunnel took 21 minutes for 190 MB) must finish, and
    // the bytes on the wire should be as few as the save allows.
    static partial class OfflineTests
    {
        static void TransferResilienceTests()
        {
            string root=Path.Combine(Path.GetTempPath(),"mpf-slow-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                // A compressible save (most of a game save is repetitive) and an
                // incompressible one (an already compressed format).
                var compressible=new byte[SnapshotLimits.ChunkBytes*3+11];
                for(int i=0;i<compressible.Length;i++)compressible[i]=(byte)(i/97%7);
                var random=new byte[SnapshotLimits.ChunkBytes+5];new Random(5).NextBytes(random);
                string packedPath=Path.Combine(root,"packed.sav"),rawPath=Path.Combine(root,"raw.sav");
                File.WriteAllBytes(packedPath,compressible);File.WriteAllBytes(rawPath,random);
                var packedParts=new List<Msg>();var rawParts=new List<Msg>();
                using(var source=new SnapshotSource(packedPath,1))source.SendTo(m=>{packedParts.Add(m);return true;},"Host",()=>false);
                using(var source=new SnapshotSource(rawPath,1))source.SendTo(m=>{rawParts.Add(m);return true;},"Host",()=>false);
                long packedWire=packedParts.Skip(1).Sum(m=>(long)m.Payload.Length),rawWire=rawParts.Skip(1).Sum(m=>(long)m.Payload.Length);
                Check(packedParts.Skip(1).Take(3).All(m=>m.Payload.Contains("[\"z\"]=")) && packedWire*4<compressible.Length,
                    "compressible save travels compressed: "+packedWire+" encoded bytes for "+compressible.Length);
                Check(rawParts.Skip(1).All(m=>!m.Payload.Contains("[\"z\"]=")),"incompressible chunks are sent unchanged, never larger");
                foreach(var pair in new[]{Tuple.Create(packedParts,compressible),Tuple.Create(rawParts,random)})
                    using(var receiver=new SnapshotReceiver(Path.Combine(root,"rx")))
                    {
                        receiver.Begin(pair.Item1[0].Payload);CompletedSnapshot done=null;
                        foreach(var part in pair.Item1.Skip(1))done=receiver.Add(part.Payload);
                        using(done)Check(done!=null && File.ReadAllBytes(done.CommitTo(Path.Combine(root,"out"),"x")).SequenceEqual(pair.Item2),"compressed and raw parts rebuild the exact file and whole SHA-256");
                    }
                // Hostile compressed parts: a claimed size beyond the chunk, a stream
                // that inflates past the announced chunk, garbage and short output.
                string part0=packedParts[1].Payload;
                byte[] Deflate(byte[] data){using(var o=new MemoryStream()){using(var d=new DeflateStream(o,CompressionLevel.Optimal,true))d.Write(data,0,data.Length);return o.ToArray();}}
                string Forge(byte[] packed,int? claimed=null)
                {
                    int z=part0.IndexOf("[\"z\"]=",StringComparison.Ordinal),comma=part0.IndexOf(',',z);
                    string head=part0.Substring(0,z)+"[\"z\"]="+(claimed??packed.Length)+part0.Substring(comma);
                    int data=head.IndexOf("[\"data\"]=\"",StringComparison.Ordinal);
                    return head.Substring(0,data)+"[\"data\"]=\""+Convert.ToBase64String(packed)+"\"}";
                }
                var bomb=Deflate(new byte[SnapshotLimits.ChunkBytes*16]);
                var shortChunk=Deflate(new byte[100]);
                var garbage=new byte[300];new Random(9).NextBytes(garbage);
                foreach(var bad in new[]{Forge(bomb),Forge(shortChunk),Forge(garbage),Forge(shortChunk,SnapshotLimits.ChunkBytes+1),Forge(shortChunk,0)})
                    using(var receiver=new SnapshotReceiver(Path.Combine(root,"hostile")))
                    {
                        receiver.Begin(packedParts[0].Payload);
                        Reject<InvalidDataException>(()=>receiver.Add(bad),"hostile compressed part refused before it is written");
                        Check(receiver.Percent==0,"refused compressed part leaves no progress");
                    }

                // The resumable download no longer dies after 30 minutes while it is moving.
                var now=DateTime.UtcNow;string spool=Path.Combine(root,"partial");
                using(var receiver=new SnapshotReceiver(spool,()=>now,true))
                {
                    receiver.Begin(packedParts[0].Payload);receiver.Add(packedParts[1].Payload);
                    // a part every two minutes for 40 minutes (identical duplicates keep the stream alive)
                    for(int minute=2;minute<=40;minute+=2)
                    {
                        now=now.AddMinutes(2);
                        Check(!receiver.Expire(),"slow but moving download is kept after "+minute+" minutes");
                        receiver.Add(packedParts[1].Payload);
                    }
                    receiver.Add(packedParts[2].Payload);receiver.Add(packedParts[3].Payload);
                    Check(receiver.Percent==75,"slow download receives its parts past the old 30-minute limit");
                    now=now.AddSeconds(SnapshotLimits.ReceiveIdleSeconds+1);
                    Check(receiver.Expire(),"stalled download expires after the idle budget");
                    Check(File.Exists(Path.Combine(spool,"download.part")) && File.Exists(Path.Combine(spool,"download.meta")),
                        "stalled resumable download keeps its verified chunks for the retry");
                    Reject<InvalidDataException>(()=>receiver.Add(packedParts[4].Payload),"a part after expiry needs a new offer");
                }
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {
                    receiver.Begin(packedParts[0].Payload);
                    Check(receiver.HaveMask=="1110","retry resumes the chunks of the expired attempt instead of starting from zero");
                    using(var done=receiver.Add(packedParts[4].Payload))
                        Check(done!=null && File.ReadAllBytes(done.CommitTo(Path.Combine(root,"out"),"resumed")).SequenceEqual(compressible),"resumed slow download completes exactly");
                }
                now=DateTime.UtcNow;
                using(var receiver=new SnapshotReceiver(Path.Combine(root,"cap"),()=>now,true))
                {
                    receiver.Begin(packedParts[0].Payload);
                    now=now.AddSeconds(SnapshotLimits.TransferHardSeconds+1);
                    Check(receiver.Expire(),"absolute cap still bounds a trickling peer");
                }

                // Host side: progress, not wall time.
                long measure=0;var start=DateTime.UtcNow;
                var progress=new TransferProgress(()=>measure,start);
                for(int minute=1;minute<=90;minute++) {measure+=1000;Check(!progress.Expired(start.AddMinutes(minute)),"host keeps a moving transfer at minute "+minute);}
                Check(!progress.Expired(start.AddMinutes(90).AddSeconds(SnapshotLimits.TransferIdleSeconds-1)),"short silence within idle budget is tolerated");
                Check(progress.Expired(start.AddMinutes(90).AddSeconds(SnapshotLimits.TransferIdleSeconds)),"host stops a stalled transfer after the idle budget");
                measure=0;progress=new TransferProgress(()=>measure,start);
                for(int minute=1;minute*60<SnapshotLimits.TransferHardSeconds;minute++){measure++;progress.Expired(start.AddMinutes(minute));}
                measure++;Check(progress.Expired(start.AddSeconds(SnapshotLimits.TransferHardSeconds)),"host absolute cap ends an endless trickle");

                // BulkWritten counts snapshot bytes actually written, not status heartbeats.
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
                try
                {
                    using(var sender=new TcpClient("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port))
                    using(var rx=listener.AcceptTcpClient())
                    using(var outbox=new BoundedOutbox(sender.GetStream(),()=>sender.Close(),"Bulk counter"))
                    {
                        var reader=new BoundedLineReader(rx.GetStream());int lines=0;
                        new Thread(()=>{try{while(reader.ReadLine()!=null)Interlocked.Increment(ref lines);}catch(Exception){}}){IsBackground=true}.Start();
                        for(int i=0;i<5;i++)outbox.Enqueue(Msg.Make("chat","Host","{[\"text\"]=\"x"+i+"\"}"));
                        outbox.Enqueue(Msg.Make("session","Host","{[\"started\"]=true}"));
                        Wait(()=>Volatile.Read(ref lines)==6,"status traffic reaches the peer");
                        Check(outbox.BulkWritten==0,"status traffic is not counted as transfer progress");
                        long expected=packedParts.Sum(m=>(long)Wire.Encode(m).Length);
                        foreach(var m in packedParts)outbox.Enqueue(m,true);
                        Wait(()=>Volatile.Read(ref lines)==6+packedParts.Count && outbox.BulkWritten==expected,"written snapshot frames are counted exactly once");
                    }
                }
                finally {listener.Stop();}
            }
            finally {Directory.Delete(root,true);}
        }
    }
}
