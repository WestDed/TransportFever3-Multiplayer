using System;
using System.IO;

namespace MPFever
{
    static partial class OfflineTests
    {
        static string CompanyRow(int id, string owner, bool ready)
            => "{[\"id\"]=" + id + ",[\"key\"]=\"company" + id + "\",[\"ready\"]=" + (ready ? "true" : "false")
                + (owner == null ? "" : ",[\"owner\"]=" + LuaLit.Quote(owner)) + "}";
        static string CompanyRoster(string world, params string[] rows)
            => "{[\"world\"]=" + LuaLit.Quote(world) + ",[\"list\"]={" + string.Join(",", rows) + "}}";
        static void CompanyRosterTests()
        {
            string world = new string('d',32), host = new string('a',64), owner = new string('b',64), otherOwner = new string('c',64);
            var identities = new PeerIdentities();
            var peer = identities.Join("Peer",null,owner);
            var other = identities.Join("Other",null,otherOwner);
            var action = ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",Act()));
            string valid = CompanyRoster(world,CompanyRow(1,host,true),CompanyRow(2,owner,true));
            var bindings = new CompanyBindings();
            bindings.Report(valid); bindings.Join(peer,ClaimFor(peer),2000000); bindings.Require(peer,action);

            foreach (bool pendingReady in new[] {false,true})
            {
                string duplicate = CompanyRoster(world,CompanyRow(1,host,true),CompanyRow(2,owner,true),CompanyRow(3,owner,pendingReady));
                Reject<InvalidDataException>(()=>bindings.Report(duplicate),"duplicate owner refused even if one engine company is pending: " + pendingReady);
                Reject<InvalidDataException>(()=>bindings.Require(peer,action),"rejected roster revokes cached action authority until a valid report");
                Reject<InvalidDataException>(()=>bindings.Join(other,ClaimFor(other),2000000),"rejected roster grants no new company claim");
                var request = new System.Collections.Generic.Dictionary<object,object>{{"id",2d},{"owner",owner},{"world",world},{"request",new string('e',32)}};
                Reject<InvalidDataException>(()=>bindings.ValidateAssignment(request),"rejected roster cannot authorize a local migration");
                bindings.Report(valid); bindings.Require(peer,action);
                Check(true,"subsequent valid roster restores the established owner without a new company");
            }

            Reject<InvalidDataException>(()=>bindings.Report(CompanyRoster(world,CompanyRow(1,owner,false),CompanyRow(2,owner,true))),"host/client duplicate owner is rejected before binding");
            bindings.Report(valid);
            Reject<InvalidDataException>(()=>bindings.Report("{"),"malformed host report is refused");
            Reject<InvalidDataException>(()=>bindings.Require(peer,action),"malformed report cannot retain stale action authority");
            bindings.Report(valid); bindings.Require(peer,action);

            // A rejected report must not reserve a company for a pending claim
            // before discovering that another established binding was moved.
            var transactional = new CompanyBindings();
            transactional.Report(CompanyRoster(world,CompanyRow(1,host,true),CompanyRow(2,owner,false),CompanyRow(3,otherOwner,true)));
            transactional.Join(peer,ClaimFor(peer),2000000); transactional.Join(other,ClaimFor(other),2000000);
            Reject<InvalidDataException>(()=>transactional.Report(CompanyRoster(world,CompanyRow(1,host,true),CompanyRow(2,owner,true),CompanyRow(4,otherOwner,true))),"changed established company rejects the entire report");
            transactional.Report(CompanyRoster(world,CompanyRow(1,host,true),CompanyRow(4,owner,true),CompanyRow(3,otherOwner,true)));
            transactional.Require(peer,ProtocolSchema.ValidateClient(Msg.Make("act","Peer#1",Act(co:4))));
            transactional.Require(other,ProtocolSchema.ValidateClient(Msg.Make("act","Other#2",Act("Other#2",co:3))));
            Check(true,"rejected roster did not partially bind the previously pending owner");

            var metadata = ClaimFor(peer);
            metadata["sid"] = "12345678901234567"; bindings.Join(peer,metadata,2000000);
            metadata["sid"] = "22345678901234567"; bindings.Join(peer,metadata,2000000);
            metadata.Remove("sid"); bindings.Join(peer,metadata,2000000);
            bindings.Require(peer,action);
            Check(true,"same proven owner survives changed or temporarily unavailable declared Steam metadata");
            metadata["owner"] = otherOwner;
            Reject<InvalidDataException>(()=>bindings.Join(peer,metadata,2000000),"metadata refresh cannot change the proven owner");
        }
    }
}
