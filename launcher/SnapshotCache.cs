using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace MPFever
{
    // Only complete, byte-for-byte verified snapshots enter the persistent cache.
    // A filename, timestamp, old world id or an old transfer receipt is no proof.
    sealed class SnapshotCache
    {
        public const long BudgetBytes=2*SnapshotLimits.MaxBytes;
        public const int MaxCandidates=2048, MaxHashes=16;
        readonly string directory;
        readonly long budget;
        public SnapshotCache(string directory,long budget=BudgetBytes) { if(budget<1)throw new ArgumentOutOfRangeException(nameof(budget));this.directory=directory;this.budget=budget; }
        public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MPFever","snapshots");
        public CompletedSnapshot Find(SnapshotManifest manifest,IEnumerable<string> saves,Func<bool> cancelled)
        {
            SweepStaleTemporary();
            string cached=Path.Combine(directory,manifest.Sha256+".sav");
            var paths=new[]{cached}.Concat(saves).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxCandidates);
            int hashed=0;
            foreach(var path in paths)
            {
                if(cancelled())throw new OperationCanceledException();
                try
                {
                    if(!File.Exists(path) || new FileInfo(path).Length!=manifest.Bytes)continue;
                    if(++hashed>MaxHashes)break;
                    Directory.CreateDirectory(directory);
                    string temporary=Path.Combine(directory,".mpfever-"+Guid.NewGuid().ToString("N")+".tmp");
                    bool keep=false;
                    try
                    {
                        // Keep the source locked throughout hash and copy. Never
                        // load a user's original file after releasing this handle.
                        using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                        using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                        using(var sha=SHA256.Create())
                        {
                            if(input.Length!=manifest.Bytes)continue;
                            var buffer=new byte[64*1024];long copied=0;int read;
                            while((read=input.Read(buffer,0,buffer.Length))>0)
                            {
                                if(cancelled())throw new OperationCanceledException();
                                copied+=read;if(copied>manifest.Bytes)throw new InvalidDataException("Local save changed size");
                                sha.TransformBlock(buffer,0,read,buffer,0);output.Write(buffer,0,read);
                            }
                            sha.TransformFinalBlock(new byte[0],0,0);
                            string hash=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
                            if(copied!=manifest.Bytes || hash!=manifest.Sha256)continue;
                            output.Flush(true);
                        }
                        keep=true;return new CompletedSnapshot(manifest,temporary);
                    }
                    finally { if(!keep && File.Exists(temporary))File.Delete(temporary); }
                }
                catch(IOException e) { Log.Dim("Local snapshot candidate skipped: "+e.Message); }
                catch(UnauthorizedAccessException) { Log.Dim("Local snapshot candidate is inaccessible"); }
            }
            return null;
        }
        void SweepStaleTemporary()
        {
            if(!Directory.Exists(directory))return;
            try
            {
                foreach(var file in new DirectoryInfo(directory).GetFiles(".mpfever-*.tmp").Take(MaxCandidates))
                    if(file.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-1))
                        try {file.Delete();} catch(IOException) { } catch(UnauthorizedAccessException) { }
            }
            catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
        public void Store(CompletedSnapshot snapshot)
        {
            snapshot.CommitTo(directory,snapshot.Manifest.Sha256);
            var entries=new DirectoryInfo(directory).GetFiles("*.sav").Where(f=>SnapshotLimits.Hex(Path.GetFileNameWithoutExtension(f.Name),64)).OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();
            long bytes=0;
            foreach(var file in entries)
            {
                bytes+=file.Length;
                if(bytes<=budget || file.Name==snapshot.Manifest.Sha256+".sav")continue;
                try { file.Delete(); } catch(IOException) { } catch(UnauthorizedAccessException) { }
            }
        }
    }
    static class SnapshotExchange
    {
        public static void Send(SnapshotSource source,string from,SnapshotDecision decision,Func<Msg,bool> control,Func<Msg,bool> bulk,Func<bool> cancelled,Action<long,double> progress=null,int probeMilliseconds=180000)
        {
            if(cancelled() || !control(Msg.Make("resync_offer",from,source.Manifest.Payload())))throw new IOException("Snapshot offer could not be queued");
            DateTime deadline=DateTime.UtcNow.AddMilliseconds(probeMilliseconds);
            while(!decision.Need.HasValue)
            {
                if(cancelled() || DateTime.UtcNow>=deadline)throw new IOException("Snapshot cache probe disconnected or timed out");
                Thread.Sleep(10);
            }
            if(cancelled())throw new IOException("Snapshot peer disconnected");
            if(!decision.Need.Value) {Log.W("Exact local snapshot acknowledged; bulk file skipped");return;}
            DateTime began=DateTime.UtcNow,last=began;long bytes=0;
            var have=decision.Have;int part=0;
            source.SendTo(m=>
            {
                bool sent=bulk(m);
                if(sent && m.Kind=="resync_file")
                {
                    while(have!=null && part<have.Length && have[part])part++;
                    bytes+=Math.Min(SnapshotLimits.ChunkBytes,source.Manifest.Bytes-(long)part*SnapshotLimits.ChunkBytes);part++;
                }
                if((DateTime.UtcNow-last).TotalSeconds>=10)
                {last=DateTime.UtcNow;progress?.Invoke(bytes,(last-began).TotalSeconds);}
                return sent;
            },from,cancelled,have);
            progress?.Invoke(bytes,(DateTime.UtcNow-began).TotalSeconds);
        }
    }
    // A decision belongs to this Peer object and exact manifest, not a reused
    // display name. A successful receipt also terminates the probe without bulk.
    sealed class SnapshotDecision
    {
        readonly object gate=new object();
        readonly Peer peer;
        public readonly SnapshotManifest Manifest;
        public bool? Need { get { lock(gate)return need; } }
        bool? need;
        bool[] have;
        public bool[] Have { get { lock(gate)return have==null?null:(bool[])have.Clone(); } }
        public SnapshotDecision(Peer peer,SnapshotManifest manifest) { this.peer=peer;Manifest=manifest; }
        public bool Reply(Peer from,Dictionary<object,object> t,bool missing)
        {
            lock(gate)
            {
                if(!ReferenceEquals(peer,from) || SnapshotLimits.Integer(t,"id",1,int.MaxValue)!=Manifest.Id || SnapshotLimits.Text(t,"transfer")!=Manifest.Transfer)return false;
                bool[] next=null;
                if(missing && t.TryGetValue("have",out var value))
                {
                    if(!(value is string mask) || mask.Length!=Manifest.Parts || mask.Any(c=>c!='0' && c!='1'))throw new InvalidDataException("Invalid snapshot resume mask");
                    next=mask.Select(c=>c=='1').ToArray();
                }
                if(need.HasValue)return need.Value==missing;
                have=next;need=missing;return true;
            }
        }
    }
}
