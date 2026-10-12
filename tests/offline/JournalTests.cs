using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static Msg JournalAction(string peer,int seq,int co=2)
        {
            var payload=Act(peer,co).Replace("[\"oseq\"]=1,","[\"oseq\"]="+seq+",").Replace(LuaLit.Quote(peer+":1"),LuaLit.Quote(peer+":"+seq));
            return Msg.Make("act",peer,payload);
        }
        static List<OrderedCommand> ReadJournal(CommandJournal journal,string epoch)
        {
            var result=new List<OrderedCommand>();
            while(true) { var window=journal.Replay(epoch,result.Count).ToArray();if(window.Length==0)return result;result.AddRange(window); }
        }
        static void JournalTests()
        {
            string root=Path.Combine(Path.GetTempPath(),"mpfever-journal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string epoch=new string('a',32),otherEpoch=new string('b',32),hash=new string('c',64),path=Path.Combine(root,"events.bin");
            try
            {
                using(var journal=new CommandJournal(path,epoch))
                {
                    Reject<InvalidDataException>(()=>journal.Accept(epoch,JournalAction("Peer#1",1)),"journal requires trusted company binding");
                    journal.Bind("Peer#1",2);
                    var accepted=journal.Accept(epoch,JournalAction("Peer#1",1));
                    Check(accepted.Sequence==1 && accepted.Epoch==epoch && new FileInfo(path).Length>40,"first accepted command is durably framed");
                    long length=new FileInfo(path).Length;
                    var retry=journal.Accept(epoch,JournalAction("Peer#1",1));
                    Check(retry.Sequence==1 && journal.LastSequence==1 && new FileInfo(path).Length==length,"duplicate returns existing sequence without a second append");
                    var conflict=JournalAction("Peer#1",1);conflict.Payload=conflict.Payload.Replace("makeVehicleSellCmd","makeVehicleReverseCmd");
                    Reject<InvalidDataException>(()=>journal.Accept(epoch,conflict),"conflicting duplicate uid rejected");
                    Reject<InvalidDataException>(()=>journal.Accept(otherEpoch,JournalAction("Peer#1",2)),"old epoch cannot append");
                    Reject<InvalidDataException>(()=>journal.Accept(epoch,JournalAction("Peer#1",2,3)),"foreign company cannot append");
                    Reject<InvalidDataException>(()=>journal.Bind("Peer#1",3),"cannot silently rebind active journal identity");
                    Check(journal.LastSequence==1 && new FileInfo(path).Length==length,"rejected commands leave journal unchanged");
                    Check(journal.Freeze()==1,"freeze returns immutable cutover frontier");
                    Reject<InvalidDataException>(()=>journal.Accept(epoch,JournalAction("Peer#1",2)),"frozen journal admits no new actions");
                    Check(journal.Accept(epoch,JournalAction("Peer#1",1)).Sequence==1,"lost acknowledgement can be retried while frozen");
                    Reject<InvalidDataException>(()=>journal.Replay(otherEpoch,0).ToArray(),"replay checks epoch");
                    Reject<InvalidDataException>(()=>journal.Replay(epoch,2).ToArray(),"replay rejects unknown future frontier");
                    Reject<InvalidDataException>(()=>journal.Replay(epoch,0,65).ToArray(),"replay window bounded");
                }
                using(var reopened=new CommandJournal(path,epoch))
                {
                    reopened.Bind("Peer#1",2);
                    Check(reopened.LastSequence==1 && reopened.Accept(epoch,JournalAction("Peer#1",1)).Sequence==1,"restart recovers accepted sequence and duplicate index");
                    Check(ReadJournal(reopened,epoch).Single().Payload==JournalAction("Peer#1",1).Payload,"restart replay retains exact command payload");
                }
                Reject<InvalidDataException>(()=>{using(var wrong=new CommandJournal(path,otherEpoch)){}},"journal cannot be opened with another epoch");
                var complete=File.ReadAllBytes(path);
                using(var append=new FileStream(path,FileMode.Append))append.Write(new byte[]{1,2,3},0,3);
                using(var recovered=new CommandJournal(path,epoch))
                    Check(recovered.LastSequence==1 && recovered.RecoveredTailBytes==3 && new FileInfo(path).Length==complete.Length,"torn final length prefix recovers only uncommitted tail");
                // Valid record prefix plus partial body, never an accepted full record.
                using(var append=new FileStream(path,FileMode.Append))append.Write(complete,40,20);
                using(var recovered=new CommandJournal(path,epoch))
                    Check(recovered.LastSequence==1 && recovered.RecoveredTailBytes==20,"torn final body retains durable prefix");
                var corrupt=(byte[])complete.Clone();corrupt[80]^=1;File.WriteAllBytes(path,corrupt);
                Reject<InvalidDataException>(()=>{using(var broken=new CommandJournal(path,epoch)){}},"complete hash corruption fails closed");
                Check(File.ReadAllBytes(path).SequenceEqual(corrupt),"corruption is not silently truncated");
                foreach(var invalid in new[]{int.MaxValue,-1,0})
                {
                    File.WriteAllBytes(path,complete);
                    using(var append=new FileStream(path,FileMode.Append))
                    using(var writer=new BinaryWriter(append))writer.Write(invalid);
                    Reject<InvalidDataException>(()=>{using(var broken=new CommandJournal(path,epoch)){}},"invalid length rejected before allocation");
                }
                File.WriteAllBytes(path,complete.Concat(complete.Skip(40)).ToArray());
                Reject<InvalidDataException>(()=>{using(var broken=new CommandJournal(path,epoch)){}},"duplicated complete record cannot replay as new history");
                using(var tiny=new CommandJournal(Path.Combine(root,"tiny.bin"),epoch,100))
                {
                    tiny.Bind("Peer#1",2);
                    Reject<InvalidDataException>(()=>tiny.Accept(epoch,JournalAction("Peer#1",1)),"disk budget checked before append");
                    Check(tiny.LastSequence==0 && new FileInfo(Path.Combine(root,"tiny.bin")).Length==40,"budget refusal does not assign a sequence");
                }
                string concurrent=Path.Combine(root,"concurrent.bin");
                using(var journal=new CommandJournal(concurrent,epoch))
                {
                    journal.Bind("Alpha#1",2);journal.Bind("Beta#2",3);
                    var errors=new List<Exception>();var workers=new List<Thread>();
                    foreach(var peer in new[]{"Alpha#1","Beta#2"})
                    {
                        string actor=peer;int co=peer.StartsWith("Alpha")?2:3;
                        var worker=new Thread(()=>{try{for(int i=1;i<=50;i++)journal.Accept(epoch,JournalAction(actor,i,co));}catch(Exception e){lock(errors)errors.Add(e);}});
                        workers.Add(worker);worker.Start();
                    }
                    Check(workers.All(w=>w.Join(10000)) && errors.Count==0,"concurrent authors commit without collision");
                    var first=ReadJournal(journal,epoch);var second=ReadJournal(journal,epoch);
                    Check(first.Count==100 && first.Select(c=>c.Sequence).SequenceEqual(Enumerable.Range(1,100).Select(i=>(long)i)),"central sequence is contiguous across authors");
                    Check(first.Select(c=>c.Uid).SequenceEqual(second.Select(c=>c.Uid)),"every replay consumer receives the same order");
                    Check(first.Select(c=>c.Peer+":"+c.Uid).Distinct().Count()==100,"one committed record per author uid");
                }
                using(var recovered=new CommandJournal(concurrent,epoch))
                {
                    var history=ReadJournal(recovered,epoch);
                    Check(recovered.LastSequence==100 && history.Count==100,"concurrent history survives process reopen");
                    var cursor=new ReplayCursor(epoch);var effects=new List<string>();
                    Reject<InvalidDataException>(()=>cursor.Apply(history[1],c=>effects.Add(c.Uid)),"replay gap cannot apply dependent command");
                    Reject<InvalidDataException>(()=>cursor.Apply(new OrderedCommand(otherEpoch,1,"A","A:1",2,"{}"),c=>effects.Add(c.Uid)),"stale replay never mutates effects");
                    cursor.Apply(history[0],c=>effects.Add(c.Uid));
                    Check(!cursor.Apply(history[0],c=>effects.Add(c.Uid)) && effects.Count==1,"replayed duplicate invokes no second effect");
                    Reject<InvalidDataException>(()=>cursor.Apply(new OrderedCommand(epoch,1,history[0].Peer,history[0].Uid,2,"changed"),c=>effects.Add(c.Uid)),"conflicting replay sequence refused");
                    var faulted=new ReplayCursor(epoch);
                    Reject<IOException>(()=>faulted.Apply(history[0],c=>{throw new IOException("callback failed");}),"failed application marks replay cursor faulted");
                    Reject<InvalidDataException>(()=>faulted.Apply(history[0],c=>effects.Add(c.Uid)),"ambiguous effect cannot be retried without snapshot recovery");
                    for(int i=1;i<=20;i++)
                    {
                        var reconnected=new ReplayCursor(epoch,50);var applied=new List<long>();
                        foreach(var c in history.Skip(45))reconnected.Apply(c,x=>applied.Add(x.Sequence));
                        foreach(var c in history.Skip(45))reconnected.Apply(c,x=>applied.Add(x.Sequence));
                        Check(applied.SequenceEqual(Enumerable.Range(51,50).Select(x=>(long)x)) && reconnected.Frontier==100,"offline snapshot frontier plus duplicate replay cycle "+i);
                    }
                }
                var barrier=new RecoveryBarrier(otherEpoch,100,hash,new[]{"Host","Client"});
                Reject<InvalidDataException>(()=>new RecoveryBarrier(otherEpoch,100,hash,new[]{"Host","Host"}),"duplicate peers cannot shrink recovery quorum");
                Reject<InvalidDataException>(()=>new RecoveryBarrier(otherEpoch,100,hash,new string[0]),"empty quorum cannot authorize resume");
                Reject<InvalidDataException>(()=>barrier.Resume(),"recovery never resumes without ready peers");
                foreach(var invalid in new Action[]{()=>barrier.Ready("intruder",otherEpoch,100,hash),()=>barrier.Ready("Host",epoch,100,hash),()=>barrier.Ready("Host",otherEpoch,99,hash),()=>barrier.Ready("Host",otherEpoch,100,new string('d',64))})
                    Reject<InvalidDataException>(invalid,"stale or incorrect recovery acknowledgement");
                barrier.Ready("Host",otherEpoch,100,hash);barrier.Ready("Host",otherEpoch,100,hash);
                Check(!barrier.CanResume,"duplicate host acknowledgement cannot substitute client");
                barrier.Ready("Client",otherEpoch,100,hash);Check(barrier.CanResume,"matching peer quorum permits resume");
                barrier.Resume();Reject<InvalidDataException>(()=>barrier.Resume(),"one resume transition only");
                var failed=new RecoveryBarrier(otherEpoch,100,hash,new[]{"Host","Client"});failed.Ready("Host",otherEpoch,100,hash);failed.Fail();
                Reject<InvalidDataException>(()=>failed.Ready("Client",otherEpoch,100,hash),"late acknowledgement cannot undo failed recovery");
                Reject<InvalidDataException>(()=>failed.Resume(),"failed recovery stays frozen");
            }
            finally { Directory.Delete(root,true); }
        }
    }
}
