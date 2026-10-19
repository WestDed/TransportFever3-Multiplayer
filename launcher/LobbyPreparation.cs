using System;
using System.Collections.Generic;
using System.Linq;

namespace MPFever
{
    // Production startup gate: receipts and engine readiness belong to exact
    // authenticated sockets and this transfer. A hello alone never starts time.
    sealed class LobbyPreparation
    {
        readonly object gate=new object();
        readonly Dictionary<Peer,SnapshotDecision> decisions=new Dictionary<Peer,SnapshotDecision>();
        readonly HashSet<Peer> committed=new HashSet<Peer>(),ready=new HashSet<Peer>();
        readonly Dictionary<Peer,LoadLiveness> watches=new Dictionary<Peer,LoadLiveness>();
        readonly Peer[] peers;
        public readonly SnapshotManifest Manifest;
        public readonly int Revision;
        public readonly DateTime Started=DateTime.UtcNow;
        public LobbyPreparation(SnapshotManifest manifest,int revision,Peer[] peers)
        {
            Manifest=manifest;Revision=revision;this.peers=(Peer[])peers.Clone();
            foreach(var p in peers)decisions.Add(p,new SnapshotDecision(p,manifest));
        }
        public Peer[] Peers => (Peer[])peers.Clone();
        public SnapshotDecision Decision(Peer peer) => decisions[peer];
        public bool Present(Peer[] connected) => peers.Length==connected.Length && peers.All(connected.Contains);
        public bool AllCommitted { get {lock(gate)return committed.Count==peers.Length;} }
        public bool AllReady { get {lock(gate)return ready.Count==peers.Length;} }
        public bool Expired(DateTime now) {lock(gate)return watches.Where(p=>!ready.Contains(p.Key)).Any(p=>p.Value.Expired(now));}
        public bool Reply(Peer peer,Dictionary<object,object> t,string kind,int loadSeconds)
        {
            lock(gate)
            {
                if(!decisions.TryGetValue(peer,out var decision) || SnapshotLimits.Integer(t,"id",1,int.MaxValue)!=Manifest.Id
                    || SnapshotLimits.Text(t,"transfer")!=Manifest.Transfer)return false;
                if(kind=="resync_need")return decision.Reply(peer,t,true);
                if(kind=="resync_received")
                {
                    decision.Reply(peer,t,false);
                    if(committed.Add(peer))watches.Add(peer,new LoadLiveness(DateTime.UtcNow,loadSeconds));
                    return true;
                }
                if(kind!="load_status" || !committed.Contains(peer))return false;
                if(SnapshotLimits.Text(t,"phase")=="ready")ready.Add(peer);else watches[peer].Alive(DateTime.UtcNow);
                return true;
            }
        }
    }
}
