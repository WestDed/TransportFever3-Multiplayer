using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static void SnapshotResumeTests()
        {
            string root=Path.Combine(Path.GetTempPath(),"mpf-resume-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string original=Path.Combine(root,"source.sav"),spool=Path.Combine(root,"partial");
                var data=new byte[SnapshotLimits.ChunkBytes*2+7];new Random(73).NextBytes(data);File.WriteAllBytes(original,data);
                var old=new List<Msg>();SnapshotManifest oldManifest;
                using(var source=new SnapshotSource(original,9))
                {
                    oldManifest=source.Manifest;source.SendTo(m=>{old.Add(m);return true;},"Host",()=>false);
                    using(var copy=source.Stage(Path.Combine(root,"stage"),()=>false))
                        Check(File.ReadAllBytes(copy.CommitTo(Path.Combine(root,"saves"),"lobby")).SequenceEqual(data),"prelaunch stages exact bytes without owning or deleting original save");
                    Check(File.Exists(original),"disposing staged copy preserves user's original save");
                    Reject<OperationCanceledException>(()=>source.Stage(Path.Combine(root,"stage"),()=>true),"cancelled prelaunch staging stops copy");
                    Check(!Directory.GetFiles(Path.Combine(root,"stage")).Any(),"cancelled staging leaves no temporary save");
                }
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {
                    receiver.Begin(old[0].Payload);receiver.Add(old[1].Payload);receiver.Add(old[3].Payload);
                    Check(receiver.HaveMask=="101" && receiver.Percent==66,"resume flags describe only fully written chunks including short last chunk");
                    receiver.Suspend();receiver.Suspend();
                }
                Check(File.Exists(Path.Combine(spool,"download.part")) && File.Exists(Path.Combine(spool,"download.meta")),"network suspension retains bounded data and metadata even after disposal");
                using(var source=new SnapshotSource(original,1))
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {
                    Check(source.Manifest.Transfer!=oldManifest.Transfer,"retry has new transfer token and may restart host counter");
                    receiver.Begin(source.Manifest.Payload());
                    Check(receiver.HaveMask=="101" && receiver.ReceivedBytes==SnapshotLimits.ChunkBytes+7,"retry rehashes and reuses exact snapshot across new token and id");
                    var request=Receipt(source.Manifest);request.Add("have",receiver.HaveMask);
                    var peer=new Peer();var decision=new SnapshotDecision(peer,source.Manifest);Check(decision.Reply(peer,request,true),"current authenticated peer negotiates resume mask");
                    int count=0;CompletedSnapshot done=null;
                    SnapshotExchange.Send(source,"Host",decision,m=>true,m=>
                    {
                        if(m.Kind=="resync_begin")receiver.Begin(m.Payload);else {count++;done=receiver.Add(m.Payload);}return true;
                    },()=>false);
                    using(done)Check(count==1 && done!=null && File.ReadAllBytes(done.CommitTo(Path.Combine(root,"loaded"),"resumed")).SequenceEqual(data),"production exchange sends only missing chunk and verifies whole resumed file");
                    Check(!Directory.GetFiles(spool).Any(),"successful resumed transfer removes partial and metadata");
                    foreach(string mask in new[]{"","10","1010","10x"})
                    {
                        var bad=Receipt(source.Manifest);bad["have"]=mask;
                        Reject<InvalidDataException>(()=>new SnapshotDecision(peer,source.Manifest).Reply(peer,bad,true),"manifest-bound resume length/content rejected: "+mask);
                    }
                    var shortPeer=new Peer();var shortRequest=Receipt(source.Manifest);shortRequest["have"]="110";
                    var shortDecision=new SnapshotDecision(shortPeer,source.Manifest);shortDecision.Reply(shortPeer,shortRequest,true);
                    long queued=0;SnapshotExchange.Send(source,"Host",shortDecision,m=>true,m=>true,()=>false,(bytes,seconds)=>queued=bytes);
                    Check(queued==7,"resume throughput counts actual short final chunk instead of 256 KiB");
                    var wrong=Receipt(source.Manifest);wrong["have"]="111";
                    Check(!new SnapshotDecision(peer,source.Manifest).Reply(new Peer(),wrong,true),"another socket cannot claim reusable parts");
                    wrong["transfer"]=oldManifest.Transfer;
                    Check(!new SnapshotDecision(peer,source.Manifest).Reply(peer,wrong,true),"old transfer cannot skip new file parts");
                    Reject<InvalidDataException>(()=>source.SendTo(m=>true,"Host",()=>false,new[]{true}),"source bounds resume mask independently of network schema");
                }
                // Persist and corrupt one chunk: only that chunk must be fetched again.
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {receiver.Begin(old[0].Payload);receiver.Add(old[1].Payload);receiver.Add(old[3].Payload);receiver.Suspend();}
                using(var f=new FileStream(Path.Combine(spool,"download.part"),FileMode.Open,FileAccess.Write))f.WriteByte((byte)(data[0]^1));
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {receiver.Begin(old[0].Payload);Check(receiver.HaveMask=="001","disk corruption invalidates exactly the corrupt persisted chunk");receiver.Suspend();}
                // A changed world may have the same byte size. SHA, not size/name, is the key.
                var changed=(byte[])data.Clone();changed[changed.Length-1]^=1;File.WriteAllBytes(original,changed);
                using(var source=new SnapshotSource(original,1))
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {receiver.Begin(source.Manifest.Payload());Check(receiver.HaveMask=="000","changed same-sized world never reuses stale partial chunks");}
                Check(!Directory.GetFiles(spool).Any(),"explicit cancel/dispose deletes active resumable transfer");
                foreach(var bad in new[]{new byte[]{0,1,2},new byte[140001]})
                {
                    File.WriteAllBytes(Path.Combine(spool,"download.part"),data);File.WriteAllBytes(Path.Combine(spool,"download.meta"),bad);
                    using(var receiver=new SnapshotReceiver(spool,resumable:true))
                    {receiver.Begin(old[0].Payload);Check(receiver.HaveMask=="000","truncated/oversized metadata safely becomes full-transfer miss");}
                }
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {receiver.Begin(old[0].Payload);receiver.Add(old[1].Payload);receiver.Suspend();}
                Reject<OperationCanceledException>(()=>
                {
                    using(var receiver=new SnapshotReceiver(spool,resumable:true))receiver.Begin(old[0].Payload,()=>true);
                },"cancelled partial disk verification cannot advertise unverified chunks");
                foreach(string payload in new[]{"{[\"id\"]=1,[\"transfer\"]=\""+oldManifest.Transfer+"\",[\"have\"]=\"001\"}","{[\"id\"]=1,[\"transfer\"]=\""+oldManifest.Transfer+"\"}"})
                    Check(ProtocolSchema.ValidateClient(Msg.Make("resync_need","guest",payload))!=null,"production schema accepts bounded new mask and legacy full request");
                foreach(string value in new[]{"\"\"","\"10x\"","true","{}","\""+new string('1',4097)+"\""})
                    Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("resync_need","guest","{[\"id\"]=1,[\"transfer\"]=\""+oldManifest.Transfer+"\",[\"have\"]="+value+"}")),"production schema refuses malformed or oversized resume mask");
                // Crash after the final checkpoint but before whole-file commit:
                // a subsequent process can finish locally, still checking SHA-256.
                Action<byte[]> checkpointAll=contents=>
                {
                    Directory.CreateDirectory(spool);File.WriteAllBytes(Path.Combine(spool,"download.part"),contents);
                    using(var writer=new BinaryWriter(File.Create(Path.Combine(spool,"download.meta"))))
                    {
                        writer.Write(0x4d504631);writer.Write(oldManifest.Bytes);writer.Write(oldManifest.Parts);
                        writer.Write(System.Text.Encoding.ASCII.GetBytes(oldManifest.Sha256));
                        for(int i=0;i<oldManifest.Parts;i++)
                        {
                            writer.Write((byte)1);
                            using(var sha=System.Security.Cryptography.SHA256.Create())
                                writer.Write(sha.ComputeHash(contents,i*SnapshotLimits.ChunkBytes,Math.Min(SnapshotLimits.ChunkBytes,contents.Length-i*SnapshotLimits.ChunkBytes)));
                        }
                    }
                };
                checkpointAll(data);
                Check(SnapshotReceiver.HasPartial(spool,oldManifest),"matching bounded partial header can prioritize resume lookup");
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {
                    receiver.Begin(old[0].Payload);
                    using(var done=receiver.CompleteIfReady())Check(done!=null && receiver.HaveMask==null,"fully persisted file can complete after restart without receiving a new chunk");
                }
                var forged=(byte[])data.Clone();forged[0]^=1;checkpointAll(forged);
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {
                    receiver.Begin(old[0].Payload);Check(receiver.HaveMask=="111","per-chunk metadata alone is not a whole-file proof");
                    Reject<InvalidDataException>(()=>receiver.CompleteIfReady(),"whole SHA-256 rejects tampered complete file even with consistent per-chunk metadata");
                }
                Check(!Directory.GetFiles(spool).Any(),"failed final hash deletes forged persisted data");
                checkpointAll(data);File.SetLastWriteTimeUtc(Path.Combine(spool,"download.meta"),DateTime.UtcNow.AddDays(-8));
                using(var receiver=new SnapshotReceiver(spool,resumable:true))
                {receiver.Begin(old[0].Payload);Check(receiver.HaveMask=="000","expired checkpoint is replaced on next use");}
                // Real TCP across two independent connections, preserving only the client's spool.
                File.WriteAllBytes(original,data);
                using(var source=new SnapshotSource(original,1))
                {
                    var messages=new List<Msg>();source.SendTo(m=>{messages.Add(m);return true;},"Host",()=>false);
                    using(var relay=new Relay())using(var client=new Client())using(var receiver=new SnapshotReceiver(spool,resumable:true))
                    {
                        bool received=false;
                        relay.Joined+=p=> {relay.Send(p,messages[0]);relay.Send(p,messages[1]);};
                        client.Received+=m=> {if(m.Kind=="resync_begin")receiver.Begin(m.Payload);else if(m.Kind=="resync_file") {receiver.Add(m.Payload);received=true;}};
                        relay.Start(0);client.Connect("127.0.0.1",relay.Port,"guest");Wait(()=>received,"real TCP delivers first part before disconnect");
                        client.Dispose();receiver.Suspend();
                    }
                    using(var relay=new Relay())using(var client=new Client())using(var receiver=new SnapshotReceiver(spool,resumable:true))
                    {
                        receiver.Begin(source.Manifest.Payload());Check(receiver.HaveMask=="100","new TCP session restores verified prior connection's chunk");
                        CompletedSnapshot done=null;int sent=0;Exception failure=null;
                        relay.Joined+=p=>new Thread(()=>
                        {
                            try {source.SendTo(m=> {if(m.Kind=="resync_file")Interlocked.Increment(ref sent);return relay.SendBulk(p,m);},"Host",()=>false,new[]{true,false,false});}
                            catch(Exception e) {failure=e;}
                        }){IsBackground=true}.Start();
                        client.Received+=m=> {if(m.Kind=="resync_begin")receiver.Begin(m.Payload);else if(m.Kind=="resync_file")done=receiver.Add(m.Payload);};
                        relay.Start(0);client.Connect("127.0.0.1",relay.Port,"guest");Wait(()=>done!=null || failure!=null,"real TCP reconnect completes remaining parts");
                        if(failure!=null)throw failure;
                        using(done)Check(sent==2 && File.ReadAllBytes(done.CommitTo(Path.Combine(root,"tcp"),"resume")).SequenceEqual(data),"real TCP resume sends two missing chunks and preserves exact file");
                    }
                }
            }
            finally {Directory.Delete(root,true);}
        }
    }
}
