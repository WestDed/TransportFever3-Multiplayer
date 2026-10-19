using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static void LobbyPreparationTests()
        {
            var manifest=new SnapshotManifest {Id=1,Bytes=1,Parts=1,Transfer=new string('1',32),Sha256=new string('a',64)};
            var one=new Peer();var two=new Peer();var preparation=new LobbyPreparation(manifest,4,new[]{one,two});
            Check(!preparation.AllCommitted && !preparation.AllReady,"initial gate waits for every client before launch and simulation");
            var ready=Receipt(manifest);ready.Add("phase","ready");
            var need=Receipt(manifest);need.Add("have","1");
            Check(preparation.Reply(one,need,"resync_need",1800) && !preparation.AllCommitted,"complete have mask alone cannot grant file receipt or game launch");
            Check(!preparation.Reply(one,Receipt(manifest),"hello",1800),"hello alone cannot open initial engine barrier");
            Check(!preparation.Reply(one,ready,"load_status",1800),"ready without verified file cannot start engine barrier");
            Check(!preparation.Reply(new Peer(),Receipt(manifest),"resync_received",1800),"same-name replacement socket cannot commit prior connection's save");
            var wrong=Receipt(manifest);wrong["transfer"]=new string('2',32);
            Check(!preparation.Reply(one,wrong,"resync_received",1800),"old snapshot receipt cannot launch new preparation");
            Check(preparation.Reply(one,Receipt(manifest),"resync_received",1800) && !preparation.AllCommitted,"one committed file cannot launch two-client lobby");
            Check(preparation.Reply(two,Receipt(manifest),"resync_received",1800) && preparation.AllCommitted && !preparation.AllReady,"all committed files allow launch but never simulation before engine readiness");
            preparation.Reply(one,ready,"load_status",1800);
            Check(!preparation.AllReady,"one ready engine cannot start the other engine's world");
            preparation.Reply(two,ready,"load_status",1800);Check(preparation.AllReady,"exact ready receipts open the engine barrier");
            Check(preparation.Present(new[]{two,one}) && !preparation.Present(new[]{one}) && !preparation.Present(new[]{one,new Peer()}),"startup is bound to complete authenticated socket membership");
            Check(!preparation.Expired(DateTime.UtcNow.AddHours(3)),"completed engines are not waiting on stale liveness watches");
            var solo=new LobbyPreparation(manifest,1,new Peer[0]);Check(solo.AllCommitted && solo.AllReady && solo.Present(new Peer[0]),"host-only lobby passes empty remote barrier");
            var room=new LobbyRoom("test",new string('a',64),"save","separate");room.Join("one");
            room.Ready("one",room.Id,room.Revision,"test",room.Content,true);int revision=room.Revision;
            Check(room.Prepare(revision) && !room.Ready("one",room.Id,revision,"test",room.Content,false),"preparation freezes readiness while game stays closed");
            Check(room.AbortPreparation() && room.Revision>revision && !room.CanLaunch,"failed transfer invalidates readiness and returns to waiting");
            Check(!room.SetSnapshot(revision,manifest.Transfer) && !room.LaunchPrepared(revision,manifest.Transfer),"old preparation worker cannot launch after cancellation");
            Check(!room.AbortPreparation(),"aborting an already waiting room is idempotent");
            // Exercise production file exchange, room transitions and readiness
            // over real sockets. No GameLink or TF3 process is involved.
            string root=Path.Combine(Path.GetTempPath(),"mpf-preflight-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string file=Path.Combine(root,"host.sav");File.WriteAllBytes(file,new byte[SnapshotLimits.ChunkBytes+3]);
                using(var source=new SnapshotSource(file,1))
                using(var relay=new Relay())using(var client=new Client())
                using(var receiver=new SnapshotReceiver(Path.Combine(root,"spool")))
                {
                    LobbyPreparation live=null;var liveRoom=new LobbyRoom("test",new string('a',64),"save","separate");
                    Exception failure=null;bool launched=false,downloaded=false;int bytes=0;
                    relay.Joined+=p=>
                    {
                        liveRoom.Join(p.Name);liveRoom.Ready(p.Name,liveRoom.Id,liveRoom.Revision,"test",liveRoom.Content,true);
                        liveRoom.Prepare(liveRoom.Revision);liveRoom.SetSnapshot(liveRoom.Revision,source.Manifest.Transfer);
                        live=new LobbyPreparation(source.Manifest,liveRoom.Revision,new[]{p});
                        relay.Send(p,Msg.Make("lobby_state","Host",liveRoom.Payload()));
                        new Thread(()=>
                        {
                            try
                            {
                                SnapshotExchange.Send(source,"Host",live.Decision(p),m=>relay.SendBulk(p,m,false),m=>relay.SendBulk(p,m),()=>false);
                                Wait(()=>live.AllCommitted,"real TCP prelaunch waits for verified receipt");
                                if(!liveRoom.LaunchPrepared(live.Revision,source.Manifest.Transfer))throw new Exception("commit failed");
                                relay.Send(p,Msg.Make("lobby_state","Host",liveRoom.Payload()));
                            }
                            catch(Exception e) {failure=e;}
                        }){IsBackground=true}.Start();
                    };
                    relay.Received+=(p,m)=> {if(m.Kind.StartsWith("resync_") || m.Kind=="load_status")live.Reply(p,ProtocolSchema.ValidateClient(m),m.Kind,1800);};
                    client.Received+=m=>
                    {
                        if(m.Kind=="resync_offer")client.Send(Msg.Make("resync_need","guest","{[\"id\"]=1,[\"transfer\"]=\""+source.Manifest.Transfer+"\"}"));
                        if(m.Kind=="resync_begin")receiver.Begin(m.Payload);
                        if(m.Kind=="resync_file")
                        {
                            bytes++;
                            using(var done=receiver.Add(m.Payload))if(done!=null)
                            {
                                done.CommitTo(Path.Combine(root,"client"),"common");downloaded=true;
                                client.Send(Msg.Make("resync_received","guest","{[\"id\"]=1,[\"transfer\"]=\""+done.Manifest.Transfer+"\"}"));
                            }
                        }
                        if(m.Kind=="lobby_state")
                        {
                            var view=LobbyView.Parse(m.Payload);
                            if(view.Phase=="preparing" && downloaded)throw new Exception("unexpected initial state ordering");
                            if(view.Phase=="launching") {if(!downloaded)throw new Exception("launch before disk verification");launched=true;}
                        }
                    };
                    relay.Start(0);client.Connect("127.0.0.1",relay.Port,"guest");Wait(()=>launched || failure!=null,"real TCP common-file launch arrives after verification");
                    if(failure!=null)throw failure;
                    Check(bytes==2 && !live.AllReady,"prelaunch transfers each chunk once and does not mistake launcher receipt for engine readiness");
                    client.Send(Msg.Make("load_status","guest","{[\"id\"]=1,[\"transfer\"]=\""+source.Manifest.Transfer+"\",[\"phase\"]=\"ready\"}"));
                    Wait(()=>live.AllReady,"real TCP engine-ready receipt opens production start gate");
                }
            }
            finally {Directory.Delete(root,true);}
        }
    }
}
