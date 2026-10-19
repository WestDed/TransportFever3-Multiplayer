using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MPFever
{
    static partial class OfflineTests
    {
        static Dictionary<object,object> Receipt(SnapshotManifest m) => ProtocolSchema.Table(LuaLit.Parse("{[\"id\"]="+m.Id+",[\"transfer\"]="+LuaLit.Quote(m.Transfer)+"}"),"receipt");
        static void SnapshotCacheTests()
        {
            string root=Path.Combine(Path.GetTempPath(),"mpf-cache-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                byte[] data=Enumerable.Range(0,SnapshotLimits.ChunkBytes+17).Select(i=>(byte)(i%251)).ToArray();
                string source=Path.Combine(root,"host.sav"),local=Path.Combine(root,"different-name.sav"),bad=Path.Combine(root,"same-name.sav");
                File.WriteAllBytes(source,data);File.WriteAllBytes(local,data);File.WriteAllBytes(bad,new byte[data.Length]);
                var cache=new SnapshotCache(Path.Combine(root,"cache"));
                using(var snapshot=new SnapshotSource(source,1))
                {
                    using(var found=cache.Find(snapshot.Manifest,new[]{bad,local},()=>false))
                    {
                        Check(found!=null,"different filename is accepted only after exact content verification");
                        string committed=found.CommitTo(Path.Combine(root,"save"),"load");
                        Check(File.ReadAllBytes(committed).SequenceEqual(data),"local hit is staged and atomically committed");
                        cache.Store(found);
                    }
                    string orphan=Path.Combine(root,"cache",".mpfever-old.tmp");File.WriteAllText(orphan,"orphan");File.SetLastWriteTimeUtc(orphan,DateTime.UtcNow.AddDays(-2));
                    using(var found=cache.Find(snapshot.Manifest,new string[0],()=>false))Check(found!=null,"persistent cache survives a new lookup without original saves");
                    Check(!File.Exists(orphan),"crash-left cache staging is cleaned after one day");
                    // A changed local original must not change the already staged load.
                    using(var found=cache.Find(snapshot.Manifest,new[]{local},()=>false))
                    {
                        File.WriteAllBytes(local,new byte[data.Length]);
                        string committed=found.CommitTo(Path.Combine(root,"save"),"staged");
                        Check(File.ReadAllBytes(committed).SequenceEqual(data),"verified private staging avoids original-file TOCTOU");
                    }
                    using(var restarted=new SnapshotSource(source,1))
                    using(var found=cache.Find(restarted.Manifest,new string[0],()=>false))
                        Check(found!=null && restarted.Manifest.Transfer!=snapshot.Manifest.Transfer,"exact cache reused across new host transfer token and reused id=1");
                    var smallDir=Path.Combine(root,"bounded");Directory.CreateDirectory(smallDir);
                    File.WriteAllBytes(Path.Combine(smallDir,new string('f',64)+".sav"),data);File.SetLastWriteTimeUtc(Path.Combine(smallDir,new string('f',64)+".sav"),DateTime.UtcNow.AddDays(-1));
                    var small=new SnapshotCache(smallDir,data.Length);
                    using(var found=cache.Find(snapshot.Manifest,new string[0],()=>false))small.Store(found);
                    Check(new DirectoryInfo(smallDir).GetFiles("*.sav").Sum(f=>f.Length)<=data.Length,"completed cache evicts old snapshots to its byte budget");
                    string cachePath=Path.Combine(root,"cache",snapshot.Manifest.Sha256+".sav");File.WriteAllBytes(cachePath,new byte[data.Length]);
                    using(var found=cache.Find(snapshot.Manifest,new[]{bad},()=>false))Check(found==null,"corrupt cache and same-sized wrong save are misses");
                    Check(File.ReadAllBytes(bad).All(b=>b==0),"candidate lookup never modifies a user's original save");
                    Reject<OperationCanceledException>(()=>cache.Find(snapshot.Manifest,new[]{bad},()=>true),"cancelled scan stops before hashing");
                    Check(!Directory.GetFiles(Path.Combine(root,"cache"),"*.tmp").Any(),"cache miss/cancellation leaves no loadable temporary file");
                    var peer=new Peer();var other=new Peer();var decision=new SnapshotDecision(peer,snapshot.Manifest);
                    Check(!decision.Reply(other,Receipt(snapshot.Manifest),false) && !decision.Need.HasValue,"old/different peer object cannot acknowledge a snapshot");
                    var stale=Receipt(snapshot.Manifest);stale["transfer"]=new string('f',32);
                    Check(!decision.Reply(peer,stale,false),"receipt bound to exact transfer token");
                    Check(decision.Reply(peer,Receipt(snapshot.Manifest),false),"exact peer and transfer can acknowledge cache hit");
                    int offers=0,bulk=0;
                    SnapshotExchange.Send(snapshot,"Host",decision,m=> {offers++;return true;},m=> {bulk++;return true;},()=>false);
                    Check(offers==1 && bulk==0,"production exchange sends no file frames for cache hit");
                    var missing=new SnapshotDecision(peer,snapshot.Manifest);missing.Reply(peer,Receipt(snapshot.Manifest),true);
                    var messages=new List<Msg>();SnapshotExchange.Send(snapshot,"Host",missing,m=>true,m=> {messages.Add(m);return true;},()=>false);
                    Check(messages.Count==snapshot.Manifest.Parts+1 && messages[0].Kind=="resync_begin","cache miss uses existing complete transfer protocol");
                    using(var receiver=new SnapshotReceiver(Path.Combine(root,"receiver")))
                    {
                        receiver.Begin(messages[0].Payload);CompletedSnapshot completed=null;
                        foreach(var m in messages.Skip(1)) {completed=receiver.Add(m.Payload);if(completed==null)Check(receiver.ReceivedBytes==SnapshotLimits.ChunkBytes,"download progress counts verified part bytes");}
                        using(completed)Check(completed!=null && File.ReadAllBytes(completed.CommitTo(Path.Combine(root,"save"),"download")).SequenceEqual(data),"cache miss still verifies SHA and loads complete exact file");
                    }
                    Reject<IOException>(()=>SnapshotExchange.Send(snapshot,"Host",new SnapshotDecision(peer,snapshot.Manifest),m=>true,m=>true,()=>false,probeMilliseconds:25),"unanswered cache offer has a bounded timeout");
                    Reject<IOException>(()=>SnapshotExchange.Send(snapshot,"Host",missing,m=>true,m=>true,()=>true),"cancelled exchange never sends bulk");
                    RealCacheExchange(snapshot,cache,local,data);
                }
            }
            finally {Directory.Delete(root,true);}
        }
        static void RealCacheExchange(SnapshotSource snapshot,SnapshotCache cache,string local,byte[] data)
        {
            File.WriteAllBytes(local,data);
            using(var relay=new Relay())using(var client=new Client())
            {
                Peer peer=null;SnapshotDecision decision=null;int parts=0;
                relay.Joined+=p=> {peer=p;decision=new SnapshotDecision(p,snapshot.Manifest);};
                relay.Received+=(p,m)=> {if(m.Kind=="resync_received")decision.Reply(p,ProtocolSchema.ValidateClient(m),false);};
                client.Received+=m=>
                {
                    if(m.Kind=="resync_file")parts++;
                    if(m.Kind!="resync_offer")return;
                    using(var found=cache.Find(SnapshotManifest.Parse(m.Payload),new[]{local},()=>false))
                    {
                        if(found==null)throw new Exception("fixture exact snapshot missing");
                        client.Send(Msg.Make("resync_received","guest","{[\"id\"]="+snapshot.Manifest.Id+",[\"transfer\"]="+LuaLit.Quote(snapshot.Manifest.Transfer)+"}"));
                    }
                };
                relay.Start(0);client.Connect("127.0.0.1",relay.Port,"cache");Wait(()=>peer!=null && decision!=null,"real TCP snapshot cache peer connected");
                SnapshotExchange.Send(snapshot,"Host",decision,m=>relay.SendBulk(peer,m,false),m=>relay.SendBulk(peer,m),()=>false,probeMilliseconds:5000);
                Check(parts==0 && decision.Need==false,"real TCP cache offer skips all snapshot chunks after verified local match");
            }
        }
    }
}
