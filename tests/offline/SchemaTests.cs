using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static string Act(string from = "Peer#1", int co = 2, string fn = "makeVehicleSellCmd", string args = "{[1]=7}")
            => "{[\"fn\"]=" + LuaLit.Quote(fn) + ",[\"origin\"]=" + LuaLit.Quote(from) + ",[\"oseq\"]=1,[\"uid\"]=" + LuaLit.Quote(from+":1") + ",[\"at\"]=123,[\"co\"]=" + co + ",[\"args\"]=" + args + "}";
        static void SchemaTests()
        {
            Check(ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",Act())) != null,"valid action schema");
            foreach (var kind in new[] {"session","auth","resync_save","resync_load","resync_file","co_roster","save_done","welcome","unrecognised"})
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make(kind,"Peer#1")),"client cannot send host control "+kind);
            foreach (var payload in new[] { Act("spoof"), Act().Replace("[\"uid\"]=\"Peer#1:1\"","[\"uid\"]=\"another\""),
                Act().Replace("[\"at\"]=123","[\"at\"]=-1"), Act().Replace("[\"oseq\"]=1","[\"oseq\"]=1.5"),
                Act().Replace("[\"co\"]=2","[\"co\"]=0"), Act(fn:"makeJournalClearAllCmd"), Act(fn:"arbitraryLuaFunction"),
                Act().Replace("[\"args\"]={[1]=7}","[\"args\"]=false"), Act()+" garbage" })
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",payload)),"malformed or privileged action refused");
            Check(ProtocolSchema.ValidateClient(Msg.Make("clock","Peer#1","{[\"t\"]=10,[\"sp\"]=4,[\"ah\"]=3,[\"tl\"]=2}")) != null,"valid clock");
            Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("clock","Peer#1","{[\"t\"]=1e30,[\"sp\"]=1}")),"clock beyond exact integer range");
            Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("speed_req","Peer#1","{[\"speed\"]=5}")),"unsupported speed");
            Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("nat_cancel","Peer#1","{[\"origin\"]=\"spoof\",[\"id\"]=1}")),"forged native hold identity");
            Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",Act(fn:"makeScriptingSendEventCmd",args:"{[1]=\"script\",[2]=\"Company\",[3]=\"hack\",[4]={}}"))),"arbitrary scripting event");
            string RankEvent(string p)=>Act(fn:"makeScriptingSendEventCmd",args:"{[1]=\"\",[2]=\"mpfever\",[3]=\"progression\",[4]="+p+"}");
            Check(ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",RankEvent("{[\"level\"]=2}")))!=null,"personal rank request accepted");
            string validRank=RankEvent("{[\"level\"]=2}");
            Check(ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",validRank.Replace("[1]=\"\"","[1]=\"mpfever\"")))!=null,"named scripting source also accepted");
            Check(ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",Act(fn:"makeScriptingSendEventCmd",args:"{[1]=\"\",[2]=\"Subvention\",[3]=\"onAccept\",[4]={[\"uid\"]=1}}")))!=null,"native subsidy empty source is compatible");
            foreach(var badSource in new[]{"false",LuaLit.Quote(new string('x',513)),LuaLit.Quote("bad\nsource")})
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",validRank.Replace("[1]=\"\"","[1]="+badSource))),"scripting source remains bounded typed control-free data");
            foreach(var bad in new[]{"{}","{[\"level\"]=0}","{[\"level\"]=257}","{[\"level\"]=1.5}","{[\"level\"]=\"2\"}",
                "{[\"level\"]=2,[\"mpfCo\"]=1}","{[\"level\"]=2,[\"experience\"]=99999}","{[\"level\"]=2,[\"mpfRequest\"]=\"old\"}","{[\"level\"]=2,[1]=true}"})
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",RankEvent(bad))),"rank request cannot set experience or forge company");
            string BusinessEvent(string p)=>Act(fn:"makeScriptingSendEventCmd",args:"{[1]=\"mpfever\",[2]=\"mpfever\",[3]=\"business\",[4]="+p+"}");
            string create="{[\"op\"]=\"create\",[\"title\"]=\"Goods\",[\"target\"]=\"sg:A\",[\"cargo\"]=1,[\"amount\"]=100,[\"reward\"]=10000,[\"duration\"]=12}";
            string policy="{[\"op\"]=\"policy\",[\"kind\"]=\"road\",[\"key\"]=\"road:A\",[\"mode\"]=\"toll\",[\"fee\"]=100,[\"company\"]=3,[\"use\"]=true,[\"connectGrant\"]=true,[\"edit\"]=false}";
            foreach(var business in new[]{create,policy,"{[\"op\"]=\"accept\",[\"id\"]=\"order1\"}","{[\"op\"]=\"cancel\",[\"id\"]=\"order1\"}"})
                Check(ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",BusinessEvent(business)))!=null,"bounded company business operation accepted");
            foreach(var bad in new[]{create.Replace("10000","-1"),create.Replace("10000","10000000001"),create.Replace("100,","0,"),create.Replace("12}","121}"),
                create.Replace("1,","1.5,"),create.Replace("\"Goods\"","\"\""),policy.Replace("100,","0,"),policy.Replace("true","123"),
                create.Replace("[\"op\"]=\"create\"","[\"op\"]=\"delivery\""),create.Replace("}",",[\"mpfCo\"]=1}"),create.Replace("}",",[\"mpfRequest\"]=\"replay\"}"),
                create.Replace("}",",[\"balance\"]=99999}"),policy.Replace("\"road\"","\"world\"")})
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",BusinessEvent(bad))),"forged or malformed business request refused");
            string receipt="{[\"id\"]=1,[\"transfer\"]=\""+new string('a',32)+"\",[\"phase\"]=\"loading\"}";
            foreach(var phase in new[]{"loading","ready"})Check(ProtocolSchema.ValidateClient(Msg.Make("load_status","Peer#1",receipt.Replace("loading",phase)))!=null,"exact snapshot load status accepted");
            foreach(var bad in new[]{receipt.Replace("loading","50%"),receipt.Replace("[\"id\"]=1","[\"id\"]=0"),receipt.Replace(new string('a',32),"invalid"),receipt.Replace("}",",[\"progress\"]=50}")})
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make("load_status","Peer#1",bad)),"invalid load status refused");
            var identities = new PeerIdentities(); var peer = identities.Join("Peer",null,new string('a',64)); var other = identities.Join("Other",null,new string('b',64));
            var bindings = new CompanyBindings();
            var join = ProtocolSchema.Table(LuaLit.Parse("{[\"key\"]=\"peer\",[\"name\"]=\"Peer\",[\"host\"]=false,[\"mode\"]=\"separate\",[\"start\"]=2000000}"),"args");
            join["owner"]=peer.Owner;
            var action = ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",Act()));
            Reject<InvalidDataException>(()=>bindings.Require(peer,action),"unbound peer cannot mutate company");
            bindings.Join(peer,join,2000000);
            bindings.Report("{[\"list\"]={[1]={[\"id\"]=1,[\"key\"]=\"host\",[\"ready\"]=true},[2]={[\"id\"]=2,[\"key\"]=\"peer\",[\"owner\"]=\""+peer.Owner+"\",[\"ready\"]=false}}}");
            Reject<InvalidDataException>(()=>bindings.Require(peer,action),"pending engine creation grants no authority");
            bindings.Report("{[\"list\"]={[1]={[\"id\"]=1,[\"key\"]=\"host\",[\"ready\"]=true},[2]={[\"id\"]=2,[\"key\"]=\"peer\",[\"owner\"]=\""+peer.Owner+"\",[\"ready\"]=true}}}");
            bindings.Require(peer,action); Check(true,"host-confirmed company accepts own action");
            Reject<InvalidDataException>(()=>bindings.Require(other,action),"other peer cannot use same company");
            var foreign = ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",Act(co:3)));
            Reject<InvalidDataException>(()=>bindings.Require(peer,foreign),"forged company number");
            var playerRef = ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",Act(args:"{[1]={[\"__ref\"]=true,[\"k\"]=\"player\",[\"co\"]=1}}")));
            Reject<InvalidDataException>(()=>bindings.Require(peer,playerRef),"nested foreign player reference");
            var reconnect = identities.Join("Peer",peer.Token,peer.Owner); bindings.Require(reconnect,action); Check(true,"binding survives authenticated socket reconnect");
            foreach (var bad in new[] {"host","peer"})
            { join["key"]=bad; Reject<InvalidDataException>(()=>bindings.Join(other,join,2000000),"cannot claim another peer or host name"); }
            join["key"]="other"; join["host"]=true;
            Reject<InvalidDataException>(()=>bindings.Join(other,join,2000000),"client cannot claim host role");
            join["host"]=false; join["start"]=2000001d;
            Reject<InvalidDataException>(()=>bindings.Join(other,join,2000000),"client cannot increase initial balance");
            Check(CompanyBindings.AsciiLower("ПРИВЕТAbC") == "ПРИВЕТabc","company key lowercase matches Lua byte semantics");
            using (var relay = new Relay())
            using (var client = new Client())
            {
                int dispatched = 0; var closed = new ManualResetEventSlim();
                relay.Received += (p,m) => Interlocked.Increment(ref dispatched);
                client.Closed += () => closed.Set();
                relay.Start(0); client.Connect("127.0.0.1",relay.Port,"attacker");
                Wait(()=>client.ResumeToken != null,"malicious socket completes legitimate handshake");
                client.Send(Msg.Make("auth","forgedHost","{[\"money\"]=999}"));
                Check(closed.Wait(5000) && dispatched == 0,"real Relay rejects client host-control before dispatch");
            }
        }
    }
}
