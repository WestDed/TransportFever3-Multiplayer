using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MPFever
{
    static class ProtocolSchema
    {
        public const long MaxInteger = 9007199254740991L;
        public static readonly HashSet<string> ClientKinds = new HashSet<string> {
            "hello", "clock", "act", "speed_req", "sync_hash", "chat", "replay_failed", "act_fail", "act_refused",
            "det_hash", "autotest_done", "area_done", "nat_pending", "nat_cancel", "resync_received", "load_status" };
        public static readonly HashSet<string> ClientCommands = new HashSet<string> {
            "makeVehicleBuyCmd", "makeVehicleSellCmd", "makeVehicleSetLineCmd", "makeVehicleSendToDepotCmd", "makeVehicleReverseCmd",
            "makeVehicleReplaceCmd", "makeVehicleSetStoppedByUserCmd", "makeVehicleTryToDepartCmd", "makeVehicleSetManualDepartureCmd",
            "makeVehicleSetModifiersCmd", "makeLineCreateCmd", "makeLineUpdateCmd", "makeLineDestroyCmd", "makeEntitySetNameCmd",
            "makeEntitySetColorCmd", "makeWorldBuildProposalCmd", "makeScriptingSendEventCmd", "mpfCoJoin", "assetBuild", "terrainEdit" };
        public static Dictionary<object, object> Table(object value, string field)
        { if (!(value is Dictionary<object, object> t)) throw new InvalidDataException("Expected table: " + field); return t; }
        public static object Get(Dictionary<object, object> t, object key) { t.TryGetValue(key, out var v); return v; }
        public static long Integer(Dictionary<object, object> t, object key, long min = 0, long max = MaxInteger)
        {
            var value = Get(t, key);
            if (!(value is double d) || double.IsNaN(d) || double.IsInfinity(d) || d < min || d > max || Math.Truncate(d) != d)
                throw new InvalidDataException("Expected bounded integer: " + key);
            return (long)d;
        }
        public static string Text(Dictionary<object, object> t, object key, int max)
        {
            if (!(Get(t,key) is string s) || s.Length == 0 || s.Length > max || s.Any(char.IsControl)) throw new InvalidDataException("Expected bounded string: " + key);
            return s;
        }
        static void OptionalBool(Dictionary<object, object> t, string key)
        { if (t.ContainsKey(key) && !(t[key] is bool)) throw new InvalidDataException("Expected boolean: " + key); }
        public static Dictionary<object, object> ValidateClient(Msg m)
        {
            if (!Wire.ValidEnvelope(m) || !ClientKinds.Contains(m.Kind)) throw new InvalidDataException("Client cannot send this message kind: " + m?.Kind);
            var t = Table(LuaLit.Parse(m.Payload), "payload");
            switch (m.Kind)
            {
                case "act": Action(t,m.From); break;
                case "clock":
                    Integer(t,"t"); Speed(t,"sp");
                    if (t.ContainsKey("ah")) Integer(t,"ah",0,1200);
                    if (t.ContainsKey("tl")) Integer(t,"tl",0,2);
                    break;
                case "speed_req": Speed(t,"speed"); break;
                case "chat":
                    if (!(Get(t,"text") is string text) || text.Length > 4096) throw new InvalidDataException("Invalid chat text");
                    break;
                case "sync_hash": Integer(t,"n",1,int.MaxValue); Table(Get(t,"parts"),"parts"); break;
                case "det_hash": Integer(t,"round",0,int.MaxValue); Table(Get(t,"parts"),"parts"); break;
                case "nat_pending": Integer(t,"at"); goto case "nat_cancel";
                case "nat_cancel":
                    Integer(t,"id",1,int.MaxValue);
                    if (Text(t,"origin",64) != m.From) throw new InvalidDataException("Forged native origin");
                    break;
                case "load_status":
                    if (t.Count!=3 || Text(t,"phase",16)!="loading" && Text(t,"phase",16)!="ready") throw new InvalidDataException("Invalid load status");
                    goto case "resync_received";
                case "resync_received": Integer(t,"id",1,int.MaxValue); if (!SnapshotLimits.Hex(Text(t,"transfer",32),32)) throw new InvalidDataException("Invalid receipt"); break;
                case "area_done": Integer(t,"n",0,int.MaxValue); break;
            }
            return t;
        }
        static void Speed(Dictionary<object, object> t, string key)
        { long speed = Integer(t,key,0,4); if (speed != 0 && speed != 1 && speed != 2 && speed != 4) throw new InvalidDataException("Unsupported speed"); }
        public static void Action(Dictionary<object, object> t, string from)
        {
            string fn = Text(t,"fn",64);
            if (!ClientCommands.Contains(fn)) throw new InvalidDataException("Unapproved client command: " + fn);
            long seq = Integer(t,"oseq",1); Integer(t,"at");
            if (Text(t,"origin",64) != from || Text(t,"uid",128) != from + ":" + seq.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("Forged action identity");
            if (t.ContainsKey("co")) Integer(t,"co",1,4096);
            OptionalBool(t,"native"); OptionalBool(t,"paused");
            var args = Table(Get(t,"args"),"args");
            if (fn == "mpfCoJoin")
            {
                if (!(Get(t,"native") is bool native) || !native || t.ContainsKey("co")) throw new InvalidDataException("Company join must be an unbound native action");
                Text(args,"key",128); Text(args,"name",128); Text(args,"cname",256);
                if (!(Get(args,"host") is bool) || Get(args,"mode") as string != "separate") throw new InvalidDataException("Invalid company join");
                Integer(args,"start",0,MaxInteger);
                if (!SnapshotLimits.Hex(Text(args,"owner",64),64)) throw new InvalidDataException("Invalid permanent owner id");
                if (args.ContainsKey("sid") && !(Get(args,"sid") is string sid && sid.Length == 17 && sid.All(c => c >= '0' && c <= '9'))) throw new InvalidDataException("Invalid declared Steam id");
                return;
            }
            if (fn == "makeScriptingSendEventCmd")
            {
                Text(args,1L,512); string id = Text(args,2L,64), name = Text(args,3L,64);
                var parameters=Table(Get(args,4L),"event parameters");
                bool allowed = id == "Loan" && (name == "Obtain" || name == "Repay") || id == "Subvention" && (name == "onAccept" || name == "onDecline") || id == "mpfever" && (name == "share" || name == "business");
                if (!allowed) throw new InvalidDataException("Unapproved scripting event");
                if(id=="mpfever" && name=="business") Business(parameters);
            }
            else if (fn == "makeWorldBuildProposalCmd") { Table(Get(args,1L),"proposal"); Table(Get(args,2L),"context"); }
            else if (fn == "makeVehicleBuyCmd" || fn == "makeVehicleReplaceCmd") Table(Get(args,fn == "makeVehicleBuyCmd" ? 3L : 2L),"vehicle config");
            else if (fn == "makeLineCreateCmd" || fn == "makeLineUpdateCmd") Table(Get(args,fn == "makeLineCreateCmd" ? 4L : 2L),"line");
            else if (fn == "terrainEdit")
            {
                Integer(args,"w",1,8192); Integer(args,"h",1,8192);
                if (!(Get(args,"blob") is string blob) || blob.Length > LuaLit.MaxString) throw new InvalidDataException("Invalid terrain blob");
            }
        }
        static void Business(Dictionary<object,object> p)
        {
            string op=Text(p,"op",16);
            string fields;
            switch(op)
            {
                case "policy":
                    fields="op,key,kind,label,mode,fee,connect,company,use,connectGrant,edit";
                    Text(p,"key",240); string kind=Text(p,"kind",16), mode=Text(p,"mode",16);
                    if(kind!="road" && kind!="station" || mode!="private" && mode!="free" && mode!="toll") throw new InvalidDataException("Invalid infrastructure policy");
                    Integer(p,"fee",0,1000000000);if(mode=="toll" && Integer(p,"fee")==0)throw new InvalidDataException("Empty toll");
                    if(p.ContainsKey("label"))Text(p,"label",120);
                    if(p.ContainsKey("company"))Integer(p,"company",1,4096);
                    foreach(var k in new[]{"connect","use","connectGrant","edit"})OptionalBool(p,k);
                    break;
                case "create":
                    fields="op,title,target,cargo,amount,reward,duration";
                    Text(p,"title",120);Text(p,"target",240);Integer(p,"cargo",0,4096);Integer(p,"amount",1,1000000);Integer(p,"reward",1,10000000000);Integer(p,"duration",1,120);break;
                case "accept": case "cancel":fields="op,id";Text(p,"id",120);break;
                default:throw new InvalidDataException("Unknown business operation");
            }
            var permitted=new HashSet<string>(fields.Split(','));
            foreach(var key in p.Keys)if(!(key is string s) || !permitted.Contains(s))throw new InvalidDataException("Untrusted business field");
        }
        public static void CheckPlayerReferences(Dictionary<object, object> t, int company)
        {
            if (Get(t,"__ref") is bool isRef && isRef && Get(t,"k") as string == "player" && Integer(t,"co",1,4096) != company)
                throw new InvalidDataException("Foreign player reference");
            foreach (var v in t.Values) if (v is Dictionary<object, object> child) CheckPlayerReferences(child,company);
        }
    }

    // Only the HOST GameLink can report roster entries. A peer's own co claim is
    // checked against that report; bindings survive socket replacement in this session.
    sealed class CompanyBindings
    {
        sealed class Claim { public string Key, Sid, Owner; public int Company; }
        sealed class Company { public int Id; public string Key, Sid, Owner; public bool Ready; }
        readonly object gate = new object();
        readonly Dictionary<int,Claim> claims = new Dictionary<int,Claim>();
        readonly Dictionary<int,int> owners = new Dictionary<int,int>();
        List<Company> roster = new List<Company>();
        string world;
        bool reportRejected;
        public string World { get { lock(gate) return world; } }
        public string OwnershipSummary { get { lock(gate) return reportRejected ? "INVALID HOST ROSTER; company actions blocked" : string.Join("; ",roster.Select(c=>c.Id + " " + c.Key + " owner=" + (c.Owner ?? "UNASSIGNED") + " ready=" + c.Ready)); } }
        public void Join(PeerIdentity peer, Dictionary<object, object> args, long start)
        {
            string key = ProtocolSchema.Text(args,"key",128);
            if ((bool)args["host"] || key != AsciiLower(peer.Name) || ProtocolSchema.Integer(args,"start") != start)
                throw new InvalidDataException("Peer cannot claim the host role, another name or starting money");
            string sid = ProtocolSchema.Get(args,"sid") as string;
            string permanent = ProtocolSchema.Text(args,"owner",64);
            if (permanent != peer.Owner || !SnapshotLimits.Hex(permanent,64)) throw new InvalidDataException("Company owner must match the proven connection key");
            lock (gate)
            {
                if (reportRejected) throw new InvalidDataException("Host company roster has not recovered from an invalid report");
                if (roster.Any(c => c.Id == 1 && c.Owner == permanent)) throw new InvalidDataException("Host company is reserved");
                if (!roster.Any(c=>c.Owner==permanent) && roster.Any(c=>c.Id!=1 && c.Owner==null && (c.Key==key || sid!=null && c.Sid==sid))) throw new InvalidDataException("Legacy company requires explicit assignment by the host; name and Steam id are not ownership proof");
                if (claims.TryGetValue(peer.Id,out var existing))
                {
                    if (existing.Key != key || existing.Owner != permanent) throw new InvalidDataException("Company identity cannot change during a session");
                    // Steam metadata may become available later or change after a
                    // reload. Only the proven private key establishes ownership.
                    existing.Sid = sid;
                    return;
                }
                var claim = new Claim {Key=key,Sid=sid,Owner=permanent};
                claim.Company = Resolve(peer.Id,claim,roster,owners);
                if (claim.Company != 0) owners[claim.Company] = peer.Id;
                claims.Add(peer.Id,claim);
            }
        }
        public void Report(string payload)
        {
            lock (gate)
            {
                // A failed report is evidence that the previous roster may no
                // longer describe the game. Block authority until a complete
                // valid report; do not leave partially committed bindings behind.
                reportRejected = true;
                var t = ProtocolSchema.Table(LuaLit.Parse(payload),"host roster");
                var rows = ProtocolSchema.Table(ProtocolSchema.Get(t,"list"),"company list");
                string reportedWorld=ProtocolSchema.Get(t,"world") as string;
                if (t.ContainsKey("world") && !SnapshotLimits.Hex(reportedWorld,32)) throw new InvalidDataException("Invalid world ownership scope");
                if (rows.Count > 4096) throw new InvalidDataException("Company roster too large");
                var next = new List<Company>(); var ids = new HashSet<int>(); var permanentOwners = new HashSet<string>();
                foreach (var row in rows.Values)
                {
                    var r = ProtocolSchema.Table(row,"company");
                    int id = (int)ProtocolSchema.Integer(r,"id",1,4096);
                    string key = ProtocolSchema.Get(r,"key") as string;
                    if (key == null || key.Length > 128 || !ids.Add(id) || !(ProtocolSchema.Get(r,"ready") is bool ready)) throw new InvalidDataException("Invalid company roster row");
                    string owner=ProtocolSchema.Get(r,"owner") as string;
                    if (r.ContainsKey("owner") && !SnapshotLimits.Hex(owner,64)) throw new InvalidDataException("Invalid owner in host roster");
                    // Pending engine entities still reserve their permanent owner.
                    if (owner != null && !permanentOwners.Add(owner)) throw new InvalidDataException("Ambiguous permanent company owner");
                    next.Add(new Company { Id=id,Key=key,Sid=ProtocolSchema.Get(r,"sid") as string,Owner=owner,Ready=ready });
                }
                var nextOwners = new Dictionary<int,int>(owners);
                var nextBindings = new Dictionary<int,int>();
                foreach (var entry in claims)
                {
                    int co = Resolve(entry.Key,entry.Value,next,nextOwners);
                    nextBindings.Add(entry.Key,co);
                    if (co != 0) nextOwners[co] = entry.Key;
                }
                // Commit only after every row and every existing claim passed.
                roster = next;
                world = reportedWorld;
                owners.Clear();
                foreach (var entry in nextOwners) owners.Add(entry.Key,entry.Value);
                foreach (var entry in nextBindings) claims[entry.Key].Company = entry.Value;
                reportRejected = false;
            }
        }
        static int Resolve(int peer, Claim claim, List<Company> companies, Dictionary<int,int> companyOwners)
        {
            var candidates = companies.Where(c=>c.Owner==claim.Owner).ToArray();
            if (candidates.Length > 1) throw new InvalidDataException("Ambiguous permanent company owner");
            if (candidates.Length != 1 || !candidates[0].Ready) return claim.Company;
            int co = candidates[0].Id;
            if (co == 1 || companyOwners.TryGetValue(co,out var owner) && owner != peer) throw new InvalidDataException("Company is already bound to another session identity");
            if (claim.Company != 0 && claim.Company != co) throw new InvalidDataException("Host roster changed an established company binding");
            return co;
        }
        public void Require(PeerIdentity peer, Dictionary<object,object> action)
        {
            int co = (int)ProtocolSchema.Integer(action,"co",2,4096);
            lock (gate)
            {
                if (reportRejected || !claims.TryGetValue(peer.Id,out var claim) || claim.Owner != peer.Owner || claim.Company != co || !roster.Any(c=>c.Id == co && c.Ready && c.Owner == peer.Owner)) throw new InvalidDataException("Company claim is not confirmed by the host");
            }
            ProtocolSchema.CheckPlayerReferences(ProtocolSchema.Table(ProtocolSchema.Get(action,"args"),"args"),co);
        }
        public static string AsciiLower(string s) => new string(s.Select(c=>c >= 'A' && c <= 'Z' ? (char)(c+32) : c).ToArray());
        public void ValidateAssignment(Dictionary<object,object> request)
        {
            int co=(int)ProtocolSchema.Integer(request,"id",2,4096);
            string owner=ProtocolSchema.Text(request,"owner",64), requestedWorld=ProtocolSchema.Text(request,"world",32);
            if(request.Count!=4 || !SnapshotLimits.Hex(owner,64) || !SnapshotLimits.Hex(requestedWorld,32) || !SnapshotLimits.Hex(ProtocolSchema.Text(request,"request",32),32)) throw new InvalidDataException("Invalid local ownership assignment");
            lock(gate)
            {
                var c=roster.SingleOrDefault(r=>r.Id==co);
                if(reportRejected || world==null || world!=requestedWorld || c==null || !c.Ready || c.Owner!=null && c.Owner!=owner || roster.Any(r=>r.Id!=co && r.Owner==owner)) throw new InvalidDataException("Ownership assignment does not match this world's unclaimed company");
            }
        }
    }
}
