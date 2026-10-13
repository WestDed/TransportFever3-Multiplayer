using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;

namespace MPFever
{
    static partial class OfflineTests
    {
        static BoundedLineReader RawChallenge(TcpClient socket, out Dictionary<object,object> challenge)
        {
            var reader=new BoundedLineReader(socket.GetStream());
            var message=Msg.Parse(reader.ReadLine(5000,Wire.MaxHandshakeBytes));
            if(message==null || message.Kind!="challenge")throw new Exception("Fixture did not receive ownership challenge");
            challenge=ProtocolSchema.Table(LuaLit.Parse(message.Payload),"challenge");
            return reader;
        }
        static byte[] SignedJoin(Dictionary<object,object> challenge, OwnerIdentity identity, string name, string resume=null)
        {
            string proof=identity.Sign((string)challenge["epoch"],(string)challenge["nonce"],(string)challenge["host"]);
            return Wire.Encode(Msg.Make("join",name,"{[\"protocol\"]="+Wire.Version+",[\"public\"]="+identity.PublicTable()+",[\"proof\"]="+LuaLit.Quote(proof)+(resume==null?"":",[\"resume\"]="+LuaLit.Quote(resume))+"}"));
        }
        static string Roster(string owner,string world) => "{[\"world\"]="+LuaLit.Quote(world)+",[\"list\"]={[1]={[\"id\"]=1,[\"key\"]=\"host\",[\"ready\"]=true},[2]={[\"id\"]=2,[\"key\"]=\"oldname\",[\"ready\"]=true"+(owner==null?"":",[\"owner\"]="+LuaLit.Quote(owner))+"}}}";
        static Dictionary<object,object> ClaimFor(PeerIdentity peer) => new Dictionary<object,object>{{"key",CompanyBindings.AsciiLower(peer.Name)},{"name",peer.Name},{"host",false},{"mode","separate"},{"start",2000000d},{"owner",peer.Owner}};
        static void OwnershipTests()
        {
            string root=Path.Combine(Path.GetTempPath(),"mpfever-owner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string clientPath=Path.Combine(root,"client.private-owner.lua"),hostPath=Path.Combine(root,"host.private-owner.lua");
                var key=OwnerIdentity.Open(clientPath);var hostKey=OwnerIdentity.Open(hostPath);
                var reopened=OwnerIdentity.Open(clientPath);
                Check(key.Owner==reopened.Owner && SnapshotLimits.Hex(key.Owner,64),"private profile restores identical permanent owner after process restart");
                var publicKey=ProtocolSchema.Table(LuaLit.Parse(key.PublicTable()),"public");
                Check(publicKey.Count==2 && publicKey.ContainsKey("n") && publicKey.ContainsKey("e"),"wire public profile contains no private key components");
                string epoch=Guid.NewGuid().ToString("N"),nonce=OwnerIdentity.NewNonce();
                string proof=reopened.Sign(epoch,nonce,hostKey.Owner);
                Check(OwnerIdentity.Verify(key.PublicTable(),proof,epoch,nonce,hostKey.Owner)==key.Owner,"reloaded private key proves the saved public owner");
                Reject<InvalidDataException>(()=>OwnerIdentity.Verify(key.PublicTable(),proof,epoch,OwnerIdentity.NewNonce(),hostKey.Owner),"signature cannot replay on a different connection nonce");
                Reject<InvalidDataException>(()=>OwnerIdentity.Verify(key.PublicTable(),proof,Guid.NewGuid().ToString("N"),nonce,hostKey.Owner),"signature cannot replay after host epoch restart");
                Reject<InvalidDataException>(()=>OwnerIdentity.Verify(key.PublicTable(),proof,epoch,nonce,OwnerIdentity.Create().Owner),"signature is scoped to challenged host identity");
                var foreignKey=OwnerIdentity.Create();
                Reject<InvalidDataException>(()=>OwnerIdentity.Verify(key.PublicTable(),foreignKey.Sign(epoch,nonce,hostKey.Owner),epoch,nonce,hostKey.Owner),"copying public owner cannot prove another private key");
                string broken=Path.Combine(root,"broken.private-owner.lua");File.WriteAllText(broken,"{",Wire.Utf8);
                Reject<InvalidDataException>(()=>OwnerIdentity.Open(broken),"corrupt profile is refused, never silently regenerated");
                Check(File.ReadAllText(broken)=="{","corrupt identity bytes stay intact for recovery");
                File.WriteAllText(broken,new string('x',OwnerIdentity.MaxProfileBytes+1),Wire.Utf8);
                Reject<InvalidDataException>(()=>OwnerIdentity.Open(broken),"oversized private profile refused before decoding");
                using(var lease=new FileStream(clientPath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                    Reject<IOException>(()=>OwnerIdentity.Open(clientPath),"concurrent profile writer cannot replace existing identity");

                string token=null,oldEpoch=null,world=Guid.NewGuid().ToString("N");
                using(var relay=new Relay(hostKey))
                using(var client=new Client{Identity=key})
                {
                    relay.Start(0);client.Connect("127.0.0.1",relay.Port,"oldname");
                    Wait(()=>relay.Peers().Length==1,"initial real TCP owner accepted");
                    var peer=relay.Peers().Single();token=client.ResumeToken;oldEpoch=client.ResumeEpoch;
                    var bindings=new CompanyBindings();bindings.Report(Roster(key.Owner,world));bindings.Join(peer.Identity,ClaimFor(peer.Identity),2000000);
                    bindings.Require(peer.Identity,ProtocolSchema.ValidateClient(Msg.Make("act",peer.Name,Act(peer.Name))));
                    Check(peer.Identity.Owner==key.Owner,"company binds to cryptographically proven peer, not its name");
                    using(var stolen=new Client{Identity=foreignKey,ResumeToken=token,ResumeEpoch=oldEpoch})
                        Reject<InvalidDataException>(()=>stolen.Connect("127.0.0.1",relay.Port,"oldname"),"stolen session token without private key cannot replace owner");
                    Check(relay.Peers().Length==1 && relay.Peers()[0].Identity.Owner==key.Owner,"failed owner takeover keeps original player connected");
                }
                // Destroy both endpoints and their session dictionaries; reconstruct
                // only disk profiles and the host-reported saved registry.
                using(var restarted=new Relay(OwnerIdentity.Open(hostPath)))
                using(var client=new Client{Identity=OwnerIdentity.Open(clientPath),ResumeToken=token,ResumeEpoch=oldEpoch})
                {
                    restarted.Start(0);client.Connect("127.0.0.1",restarted.Port,"renamed");
                    Wait(()=>restarted.Peers().Length==1,"real TCP host and client restart authenticates disk profile");
                    var peer=restarted.Peers().Single();
                    Check(restarted.HostOwner==hostKey.Owner && client.ResumeEpoch!=oldEpoch && client.ResumeToken!=token,"host key persists while session epoch and token rotate");
                    var bindings=new CompanyBindings();bindings.Report(Roster(key.Owner,world));bindings.Join(peer.Identity,ClaimFor(peer.Identity),2000000);
                    bindings.Require(peer.Identity,ProtocolSchema.ValidateClient(Msg.Make("act",peer.Name,Act(peer.Name))));
                    Check(peer.Identity.Owner==key.Owner && peer.Identity.Name=="renamed","saved company restores despite renamed player and new session id");
                    var impostor=new PeerIdentity(2,new string('0',64),"oldname",foreignKey.Owner);
                    bindings.Join(impostor,ClaimFor(impostor),2000000);
                    Reject<InvalidDataException>(()=>bindings.Require(impostor,ProtocolSchema.ValidateClient(Msg.Make("act","oldname#1",Act("oldname#1")))),"matching old name with another key has no saved-company authority");
                }

                var legacy=new CompanyBindings();legacy.Report(Roster(null,world));
                var ownerPeer=new PeerIdentities().Join("oldname",null,key.Owner);
                Reject<InvalidDataException>(()=>legacy.Join(ownerPeer,ClaimFor(ownerPeer),2000000),"legacy company is not assigned by name after restart");
                var request=new Dictionary<object,object>{{"id",2d},{"owner",key.Owner},{"world",world},{"request",Guid.NewGuid().ToString("N")}};
                legacy.ValidateAssignment(request);Check(true,"local host can explicitly migrate the matching world's legacy company");
                request["world"]=Guid.NewGuid().ToString("N");Reject<InvalidDataException>(()=>legacy.ValidateAssignment(request),"migration for a different save world is refused");request["world"]=world;
                request["id"]=1d;Reject<InvalidDataException>(()=>legacy.ValidateAssignment(request),"client-company migration cannot replace host company");request["id"]=2d;
                legacy.Report(Roster(foreignKey.Owner,world));Reject<InvalidDataException>(()=>legacy.ValidateAssignment(request),"migration cannot overwrite a confirmed foreign owner");
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("co_assign_owner","Peer#1")),"remote client cannot send host ownership directive");
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",Act(fn:"mpfCoAssign"))),"remote client cannot emit host ownership migration command");
            }
            finally { Directory.Delete(root,true); }
        }
    }
}
