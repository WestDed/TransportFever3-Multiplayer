using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MPFever
{
    // Pure, authoritative waiting-room state. Membership changes invalidate all
    // readiness: a delayed packet cannot start a different group of players.
    sealed class LobbyRoom
    {
        readonly object gate = new object();
        readonly Dictionary<string,bool> members = new Dictionary<string,bool>(StringComparer.Ordinal);
        public readonly string Id = Guid.NewGuid().ToString("N"), Content, Version, Save, Mode;
        public int Revision { get; private set; } = 1;
        public bool Launching { get; private set; }
        public LobbyRoom(string version,string content,string save,string mode)
        {
            if(!SnapshotLimits.Hex(content,64) || string.IsNullOrWhiteSpace(save) || save.Length>512 || mode!="shared" && mode!="separate")throw new InvalidDataException("Invalid lobby configuration");
            Version=version;Content=content;Save=save;Mode=mode;
        }
        public void Join(string peer)
        {
            lock(gate)
            {
                if(Launching)throw new InvalidDataException("Lobby already launching");
                if(members.ContainsKey(peer)) {Invalidate();return;}
                if(members.Count>=Wire.MaxPeers)throw new InvalidDataException("Lobby full");
                members.Add(peer,false);Invalidate();
            }
        }
        void Invalidate() { Revision++;foreach(var key in members.Keys.ToArray())members[key]=false; }
        public void Leave(string peer) { lock(gate)if(members.Remove(peer))Invalidate(); }
        public bool Ready(string peer,string room,int revision,string version,string content,bool ready)
        {
            lock(gate)
            {
                if(Launching || room!=Id || revision!=Revision || !members.ContainsKey(peer))return false;
                if(version!=Version || content!=Content)throw new InvalidDataException("Launcher/mod/native files differ from the host");
                members[peer]=ready;return true;
            }
        }
        public bool CanLaunch { get { lock(gate)return !Launching && members.Values.All(v=>v); } }
        public bool Launch(int revision)
        { lock(gate) { if(revision!=Revision || !CanLaunch)return false;Launching=true;return true; } }
        public string Payload()
        {
            lock(gate)return "{[\"room\"]="+LuaLit.Quote(Id)+",[\"revision\"]="+Revision+",[\"phase\"]="+LuaLit.Quote(Launching?"launching":"waiting")
                +",[\"save\"]="+LuaLit.Quote(Save)+",[\"mode\"]="+LuaLit.Quote(Mode)+",[\"version\"]="+LuaLit.Quote(Version)+",[\"content\"]="+LuaLit.Quote(Content)
                +",[\"members\"]={"+string.Join(",",members.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>"["+LuaLit.Quote(p.Key)+"]="+(p.Value?"true":"false")))+"}}";
        }
    }
    sealed class LobbyView
    {
        public string Room,Phase,Save,Mode,Version,Content;
        public int Revision;
        public Dictionary<string,bool> Members;
        public static LobbyView Parse(string payload)
        {
            var t=ProtocolSchema.Table(LuaLit.Parse(payload),"lobby");
            var v=new LobbyView { Room=ProtocolSchema.Text(t,"room",32),Revision=(int)ProtocolSchema.Integer(t,"revision",1,int.MaxValue),
                Phase=ProtocolSchema.Text(t,"phase",16),Save=ProtocolSchema.Text(t,"save",512),Mode=ProtocolSchema.Text(t,"mode",16),
                Version=ProtocolSchema.Text(t,"version",64),Content=ProtocolSchema.Text(t,"content",64),Members=new Dictionary<string,bool>() };
            if(t.Count!=8 || !SnapshotLimits.Hex(v.Room,32) || !SnapshotLimits.Hex(v.Content,64) || v.Phase!="waiting" && v.Phase!="launching" || v.Mode!="shared" && v.Mode!="separate")throw new InvalidDataException("Invalid lobby state");
            var members=ProtocolSchema.Table(ProtocolSchema.Get(t,"members"),"members");
            if(members.Count>Wire.MaxPeers)throw new InvalidDataException("Lobby too large");
            foreach(var p in members)
            {
                if(!(p.Key is string name) || name.Length<1 || name.Length>64 || name.Any(char.IsControl) || !(p.Value is bool ready))throw new InvalidDataException("Invalid lobby member");
                v.Members.Add(name,ready);
            }
            return v;
        }
        public string ReadyPayload(string version,string content,bool ready) => "{[\"room\"]="+LuaLit.Quote(Room)+",[\"revision\"]="+Revision+",[\"version\"]="+LuaLit.Quote(version)+",[\"content\"]="+LuaLit.Quote(content)+",[\"ready\"]="+(ready?"true":"false")+"}";
    }
    static class LobbyFiles
    {
        // Deterministic relative paths + file hashes, never local absolute paths.
        // This checks our shipped files, not third-party mods or Steam's game build.
        public static string Fingerprint(string mod,string native)
        {
            if(mod==null || !Directory.Exists(mod) || !File.Exists(native))throw new IOException("The complete MPFever package (mod + winhttp.dll) is required");
            var rows=new List<string>();
            foreach(var path in Directory.GetFiles(mod,"*",SearchOption.AllDirectories).OrderBy(p=>p.Substring(mod.Length).Replace('\\','/'),StringComparer.Ordinal))
                using(var input=File.OpenRead(path))rows.Add(path.Substring(mod.Length).Replace('\\','/')+":"+SnapshotLimits.Hash(input));
            using(var input=File.OpenRead(native))rows.Add("winhttp.dll:"+SnapshotLimits.Hash(input));
            using(var input=new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\n",rows))))return SnapshotLimits.Hash(input);
        }
    }
}
