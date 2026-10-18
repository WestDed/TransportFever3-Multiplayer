using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static void LobbyTests()
        {
            string content=new string('a',64);var room=new LobbyRoom("test",content,"Карта","separate");
            room.Join("one");int old=room.Revision;room.Join("two");
            Check(!room.Ready("one",room.Id,old,"test",content,true),"lobby rejects stale membership readiness");
            Check(!room.Ready("foreign",room.Id,room.Revision,"test",content,true),"nonmember cannot ready a lobby");
            Check(!room.Ready("one",new string('b',32),room.Revision,"test",content,true),"readiness bound to unique room");
            Reject<InvalidDataException>(()=>room.Ready("one",room.Id,room.Revision,"old",content,true),"mismatched launcher cannot ready");
            Reject<InvalidDataException>(()=>room.Ready("one",room.Id,room.Revision,"test",new string('b',64),true),"mismatched mod/native cannot ready");
            room.Ready("one",room.Id,room.Revision,"test",content,true);
            Check(!room.CanLaunch && !room.Launch(room.Revision),"one ready peer cannot start an unready group");
            room.Ready("two",room.Id,room.Revision,"test",content,true);
            var view=LobbyView.Parse(room.Payload());
            Check(view.Members.Count==2 && view.Members.Values.All(v=>v) && view.Save=="Карта" && view.Mode=="separate","lobby state survives production data decoder");
            ProtocolSchema.ValidateClient(Msg.Make("lobby_ready","one",view.ReadyPayload("test",content,true)));
            Check(true,"lobby readiness passes production schema");
            Check(!room.Launch(old),"stale host start cannot launch");
            room.Leave("two");Check(!room.CanLaunch,"disconnect invalidates remaining readiness");
            room.Ready("one",room.Id,room.Revision,"test",content,true);
            Check(room.Launch(room.Revision) && !room.Launch(room.Revision),"host launches an all-ready room exactly once");
            Check(!room.Ready("one",room.Id,room.Revision,"test",content,false),"late unready cannot mutate committed start");
            Reject<InvalidDataException>(()=>room.Join("late"),"membership frozen at launch");
            foreach(var kind in new[]{"lobby_state","resync_offer"})
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make(kind,"one")),"client cannot forge "+kind);
            Reject<InvalidDataException>(()=>LobbyView.Parse(room.Payload().Replace("[\"phase\"]=\"launching\"","[\"phase\"]=\"arbitrary\"")),"malformed lobby phase rejected");
            // Actual sockets exchange the production room representation and readiness.
            using(var relay=new Relay())using(var client=new Client())
            {
                var live=new LobbyRoom("test",content,"save","shared");LobbyView received=null;
                relay.Joined+=p=> { live.Join(p.Name);relay.Send(p,Msg.Make("lobby_state","Host",live.Payload())); };
                relay.Received+=(p,m)=>
                {
                    if(m.Kind!="lobby_ready")return;
                    var t=ProtocolSchema.ValidateClient(m);
                    live.Ready(p.Name,(string)t["room"],(int)(double)t["revision"],(string)t["version"],(string)t["content"],(bool)t["ready"]);
                    relay.Send(p,Msg.Make("lobby_state","Host",live.Payload()));
                };
                client.Received+=m=> {if(m.Kind=="lobby_state")received=LobbyView.Parse(m.Payload());};
                relay.Start(0);client.Connect("127.0.0.1",relay.Port,"guest");
                Wait(()=>received!=null,"real TCP lobby available before any game process");
                client.Send(Msg.Make("lobby_ready","guest",received.ReadyPayload("test",content,true)));
                Wait(()=>received.Members.Values.All(v=>v),"real TCP readiness returns authoritative ready state");
                Check(live.CanLaunch,"real connected lobby gates host start");
            }
            string root=Path.Combine(Path.GetTempPath(),"mpf-lobby-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                var mod=Path.Combine(root,"mod");Directory.CreateDirectory(mod);var dll=Path.Combine(root,"winhttp.dll");
                File.WriteAllText(Path.Combine(mod,"a.lua"),"first");File.WriteAllText(dll,"native");
                var hash=LobbyFiles.Fingerprint(mod,dll);
                File.WriteAllText(Path.Combine(mod,"a.lua"),"second");
                Check(hash!=LobbyFiles.Fingerprint(mod,dll),"lobby content fingerprint detects changed Lua bytes");
                File.WriteAllText(Path.Combine(mod,"a.lua"),"first");File.WriteAllText(dll,"other");
                Check(hash!=LobbyFiles.Fingerprint(mod,dll),"lobby content fingerprint detects changed native bytes");
                File.Delete(dll);Reject<IOException>(()=>LobbyFiles.Fingerprint(mod,dll),"incomplete package cannot enter lobby");
            }
            finally {Directory.Delete(root,true);}
        }
    }
}
