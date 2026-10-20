using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace MPFever
{
    // Separate command identity from the host's small session counter. The same
    // transfer is idempotent; a new host/transfer with id=1 is a different load.
    static class SnapshotControl
    {
        public static string MenuSequence(string request)
        {
            if (!SnapshotLimits.Hex(request, 32)) throw new InvalidDataException("Invalid load request token");
            return "r" + request;
        }
        public static string Payload(int id, string name, string request)
        {
            MenuSequence(request);
            if (id < 1 || string.IsNullOrEmpty(name) || name.Length > 512) throw new InvalidDataException("Invalid load control");
            return "{[\"id\"]=" + id.ToString(CultureInfo.InvariantCulture) + ",[\"name\"]=" + LuaLit.Quote(name) + ",[\"request\"]=" + LuaLit.Quote(request) + "}";
        }
    }

    static class SnapshotLimits
    {
        public const int ChunkBytes = 256 * 1024;
        // A provisional safety budget, not a claim about Gigantomaniac saves.
        public const long MaxBytes = 1024L * 1024 * 1024;
        public const int SaveTimeoutSeconds = 600;
        // A slow link is not a dead one: a 190 MB save through a relayed tunnel
        // took 21 minutes, so a fixed 30-minute budget restarted larger saves
        // from zero forever. Transfers end when nothing moves for the idle
        // budget; the absolute cap only bounds a peer that trickles bytes.
        public const int TransferIdleSeconds = 600, ReceiveIdleSeconds = 180, TransferHardSeconds = 4 * 3600;
        public static int LoadTimeoutSeconds(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 1800;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds < 60 || seconds > 7200)
                throw new InvalidDataException("MPFEVER_LOAD_TIMEOUT_SECONDS must be an integer from 60 to 7200");
            return seconds;
        }
        public static string Hash(Stream stream)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static bool Hex(string s, int n) => s != null && s.Length == n && s.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        public static bool CanonicalBase64(string data,int bytes)
        {
            if(bytes<1 || bytes>ChunkBytes || data==null || data.Length!=4*((bytes+2)/3))return false;
            int pad=(3-bytes%3)%3,end=data.Length-pad,last=0;
            for(int i=0;i<end;i++)
            {
                char c=data[i];
                if(c>='A' && c<='Z')last=c-'A';else if(c>='a' && c<='z')last=c-'a'+26;
                else if(c>='0' && c<='9')last=c-'0'+52;else if(c=='+')last=62;else if(c=='/')last=63;else return false;
            }
            for(int i=end;i<data.Length;i++)if(data[i]!='=')return false;
            return !(pad==2 && (last&15)!=0 || pad==1 && (last&3)!=0);
        }
        public static long Integer(Dictionary<object, object> t, string key, long min, long max)
        {
            if (!t.TryGetValue(key, out var v) || !(v is double d) || double.IsNaN(d) || double.IsInfinity(d) || d < min || d > max || d != Math.Truncate(d))
                throw new InvalidDataException("Invalid snapshot integer: " + key);
            return (long)d;
        }
        public static string Text(Dictionary<object, object> t, string key)
        { if (!t.TryGetValue(key, out var v) || !(v is string s)) throw new InvalidDataException("Invalid snapshot string: " + key); return s; }
        // Raw DEFLATE per chunk, kept only when it saves at least an eighth: an
        // already compressed save costs one fast attempt per chunk and is sent as is.
        public static byte[] Pack(byte[] data, int count)
        {
            using (var output = new MemoryStream(count))
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true)) deflate.Write(data, 0, count);
                return output.Length <= count - count / 8 ? output.ToArray() : null;
            }
        }
        // The output is bounded by the chunk size announced in the manifest, never
        // by the compressed data: a hostile stream cannot inflate past it.
        public static byte[] Unpack(byte[] packed, int expected)
        {
            var result = new byte[expected]; int got = 0;
            using (var deflate = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress))
            {
                while (got < expected) { int r = deflate.Read(result, got, expected - got); if (r == 0) break; got += r; }
                if (got != expected || deflate.ReadByte() != -1) throw new InvalidDataException("Compressed part size mismatch");
            }
            return result;
        }
    }

    // Ends a transfer only when its measure (bulk bytes written, receipts) has not
    // moved for TransferIdleSeconds, or after the absolute TransferHardSeconds.
    sealed class TransferProgress
    {
        readonly Func<long> measure;
        readonly DateTime started;
        readonly int idleSeconds;
        long last;
        DateTime moved;
        public TransferProgress(Func<long> measure, DateTime now, int idleSeconds = SnapshotLimits.TransferIdleSeconds)
        { this.measure = measure; this.idleSeconds = idleSeconds; started = moved = now; last = measure(); }
        public bool Expired(DateTime now)
        {
            long value = measure();
            if (value != last) { last = value; moved = now; }
            return (now - moved).TotalSeconds >= idleSeconds || (now - started).TotalSeconds >= SnapshotLimits.TransferHardSeconds;
        }
    }

    // A launcher heartbeat proves the connection is alive, not engine progress.
    // Loading may outlast the normal budget, but never the absolute two hours.
    sealed class LoadLiveness
    {
        public const int HardSeconds=7200, HeartbeatSeconds=10;
        readonly DateTime started;
        readonly int softSeconds;
        DateTime lastAlive;
        public LoadLiveness(DateTime now,int seconds) { started=lastAlive=now;softSeconds=seconds; }
        public void Alive(DateTime now) { if(now>lastAlive)lastAlive=now; }
        public bool Expired(DateTime now) => (now-started).TotalSeconds>=HardSeconds
            || (now-started).TotalSeconds>=softSeconds && (now-lastAlive).TotalSeconds>=60;
    }

    sealed class SnapshotManifest
    {
        public int Id, Parts;
        public long Bytes;
        public string Transfer, Sha256;
        public static SnapshotManifest Parse(string payload)
        {
            var t = LuaLit.Parse(payload) as Dictionary<object, object>;
            if (t == null) throw new InvalidDataException("Invalid snapshot manifest");
            var m = new SnapshotManifest { Id = (int)SnapshotLimits.Integer(t, "id", 1, int.MaxValue),
                Bytes = SnapshotLimits.Integer(t, "bytes", 1, SnapshotLimits.MaxBytes),
                Parts = (int)SnapshotLimits.Integer(t, "n", 1, SnapshotLimits.MaxBytes / SnapshotLimits.ChunkBytes),
                Transfer = SnapshotLimits.Text(t, "transfer"), Sha256 = SnapshotLimits.Text(t, "sha256") };
            if (m.Parts != (m.Bytes + SnapshotLimits.ChunkBytes - 1) / SnapshotLimits.ChunkBytes || !SnapshotLimits.Hex(m.Transfer, 32) || !SnapshotLimits.Hex(m.Sha256, 64))
                throw new InvalidDataException("Inconsistent snapshot manifest");
            return m;
        }
        public string Payload() => "{[\"id\"]=" + Id + ",[\"bytes\"]=" + Bytes.ToString(CultureInfo.InvariantCulture) + ",[\"n\"]=" + Parts
            + ",[\"transfer\"]=" + LuaLit.Quote(Transfer) + ",[\"sha256\"]=" + LuaLit.Quote(Sha256) + "}";
    }

    // The open read-only handle prevents the saved file changing after its hash.
    sealed class SnapshotSource : IDisposable
    {
        readonly FileStream file;
        public readonly SnapshotManifest Manifest;
        public SnapshotSource(string path, int id)
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (id < 1 || file.Length < 1 || file.Length > SnapshotLimits.MaxBytes) throw new InvalidDataException("Snapshot size or id outside safety budget");
                Manifest = new SnapshotManifest { Id = id, Bytes = file.Length, Parts = (int)((file.Length + SnapshotLimits.ChunkBytes - 1) / SnapshotLimits.ChunkBytes),
                    Transfer = Guid.NewGuid().ToString("N"), Sha256 = SnapshotLimits.Hash(file) };
            }
            catch { file.Dispose(); throw; }
        }
        public CompletedSnapshot Stage(string directory,Func<bool> cancelled)
        {
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,".mpfever-"+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                using(var input=new FileStream(file.Name,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    var buffer=new byte[64*1024];int read;
                    while((read=input.Read(buffer,0,buffer.Length))>0)
                    { if(cancelled())throw new OperationCanceledException();output.Write(buffer,0,read); }
                    output.Flush(true);
                }
                return new CompletedSnapshot(Manifest,path);
            }
            catch { if(File.Exists(path))File.Delete(path);throw; }
        }
        public void SendTo(Func<Msg, bool> send, string from, Func<bool> cancelled,bool[] have=null)
        {
            if(have!=null && have.Length!=Manifest.Parts)throw new InvalidDataException("Invalid resume mask length");
            if (cancelled() || !send(Msg.Make("resync_begin", from, Manifest.Payload()))) throw new IOException("Snapshot transfer cancelled");
            // Each peer opens its own cursor while this source keeps the file immutable.
            using (var reader = new FileStream(file.Name, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var buffer = new byte[SnapshotLimits.ChunkBytes];
                int misses = 0;
                for (int i = 0; i < Manifest.Parts; i++)
                {
                    if(cancelled())throw new IOException("Snapshot transfer cancelled");
                    if(have!=null && have[i]) {reader.Position=Math.Min(Manifest.Bytes,(long)(i+1)*SnapshotLimits.ChunkBytes);continue;}
                    int wanted = (int)Math.Min(buffer.Length, Manifest.Bytes - (long)i * buffer.Length), n = 0;
                    while (n < wanted) { int r = reader.Read(buffer, n, wanted - n); if (r == 0) throw new IOException("Snapshot source truncated"); n += r; }
                    // an already compressed save: after 16 chunks in a row that do not shrink, only every 16th is tried
                    var packed = misses < 16 || i % 16 == 0 ? SnapshotLimits.Pack(buffer, n) : null;
                    misses = packed == null ? misses + 1 : 0;
                    string payload = "{[\"id\"]=" + Manifest.Id + ",[\"transfer\"]=" + LuaLit.Quote(Manifest.Transfer) + ",[\"i\"]=" + i
                        + ",[\"n\"]=" + Manifest.Parts + (packed == null ? "" : ",[\"z\"]=" + packed.Length)
                        + ",[\"data\"]=\"" + (packed == null ? Convert.ToBase64String(buffer, 0, n) : Convert.ToBase64String(packed)) + "\"}";
                    if (cancelled() || !send(Msg.Make("resync_file", from, payload))) throw new IOException("Snapshot transfer cancelled or peer too slow");
                }
            }
        }
        public void Dispose() { file.Dispose(); }
    }

    sealed class CompletedSnapshot : IDisposable
    {
        public readonly SnapshotManifest Manifest;
        readonly string path;
        public CompletedSnapshot(SnapshotManifest manifest, string path) { Manifest = manifest; this.path = path; }
        public string CommitTo(string directory, string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 100 || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Invalid local save name");
            Directory.CreateDirectory(directory);
            string dst = Path.Combine(directory, name + ".sav"), tmp = Path.Combine(directory, ".mpfever-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output, 64 * 1024); output.Flush(true); }
                // No copy-over fallback: a failed rename must preserve the old final save.
                if (File.Exists(dst)) File.Replace(tmp, dst, null); else File.Move(tmp, dst);
                return dst;
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        public void Dispose() { if (File.Exists(path)) File.Delete(path); }
    }

    // One transfer per local game, <=4096 flags and one chunk of RAM. Partial data
    // lives in a temporary file and never replaces a loadable save before SHA-256.
    sealed partial class SnapshotReceiver : IDisposable
    {
        readonly object gate = new object();
        readonly string directory;
        readonly Func<DateTime> now;
        readonly Timer timer;
        SnapshotManifest manifest;
        FileStream file;
        string path;
        bool[] seen;
        int received, highestId;
        DateTime touched, begun;
        bool disposed;
        public SnapshotReceiver(string directory, Func<DateTime> clock = null,bool resumable=false)
        {
            this.directory = directory; now = clock ?? (() => DateTime.UtcNow);this.resumable=resumable;
            timer = new Timer(_ => { try { Expire(); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ObjectDisposedException) { Log.Warn("Snapshot cleanup: " + e.Message); } }, null, 30000, 30000);
        }
        public int Percent { get { lock (gate) return manifest == null ? 0 : 100 * received / manifest.Parts; } }
        public long ReceivedBytes { get { lock(gate)return manifest==null?0:seen.Select((yes,i)=>yes?Math.Min(SnapshotLimits.ChunkBytes,manifest.Bytes-(long)i*SnapshotLimits.ChunkBytes):0).Sum(); } }
        public void Begin(string payload,Func<bool> cancelled=null)
        {
            var next = SnapshotManifest.Parse(payload); // before touching disk or allocating flags
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(SnapshotReceiver));
                Expire();
                if (manifest != null && next.Id == manifest.Id && next.Transfer == manifest.Transfer && next.Bytes == manifest.Bytes && next.Sha256 == manifest.Sha256) return;
                if (next.Id <= highestId) throw new InvalidDataException("Stale or conflicting snapshot manifest");
                Clear(); highestId = next.Id;
                Directory.CreateDirectory(directory);
                if(resumable) { OpenResumable(next,cancelled??(()=>false));return; }
                path = Path.Combine(directory, ".mpfever-" + Guid.NewGuid().ToString("N") + ".part");
                try
                {
                    file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    file.SetLength(next.Bytes);
                    manifest = next; seen = new bool[next.Parts]; received = 0; begun = touched = now();
                }
                catch { Clear(); throw; }
            }
        }
        public CompletedSnapshot Add(string payload)
        {
            var t = LuaLit.Parse(payload) as Dictionary<object, object>;
            if (t == null) throw new InvalidDataException("Invalid snapshot part");
            return Add(t);
        }
        public CompletedSnapshot Add(Dictionary<object,object> t)
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(SnapshotReceiver));
                Expire();
                if (manifest == null) throw new InvalidDataException("No active snapshot manifest");
                int id = (int)SnapshotLimits.Integer(t, "id", 1, int.MaxValue), n = (int)SnapshotLimits.Integer(t, "n", 1, 4096);
                int i = (int)SnapshotLimits.Integer(t, "i", 0, manifest.Parts - 1);
                if (id != manifest.Id || n != manifest.Parts || SnapshotLimits.Text(t, "transfer") != manifest.Transfer) throw new InvalidDataException("Part does not belong to active snapshot");
                string data = SnapshotLimits.Text(t, "data");
                int wanted = (int)Math.Min(SnapshotLimits.ChunkBytes, manifest.Bytes - (long)i * SnapshotLimits.ChunkBytes);
                bool packed = t.ContainsKey("z");
                int encoded = packed ? (int)SnapshotLimits.Integer(t, "z", 1, wanted) : wanted;
                if (!SnapshotLimits.CanonicalBase64(data,encoded)) throw new InvalidDataException("Incorrect or noncanonical encoded part");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(data); }
                catch (FormatException e) { throw new InvalidDataException("Invalid part base64", e); }
                if (bytes.Length != encoded) throw new InvalidDataException("Incorrect decoded part size");
                if (packed) bytes = SnapshotLimits.Unpack(bytes, wanted);
                file.Position = (long)i * SnapshotLimits.ChunkBytes;
                if (seen[i])
                {
                    var old = new byte[wanted]; int got = 0;
                    while (got < old.Length) { int r = file.Read(old, got, old.Length - got); if (r == 0) break; got += r; }
                    if (got != wanted || !old.SequenceEqual(bytes)) throw new InvalidDataException("Conflicting duplicate part");
                }
                else
                {
                    file.Write(bytes, 0, bytes.Length); seen[i] = true; received++;
                    if(resumable)using(var sha=SHA256.Create())digests[i]=sha.ComputeHash(bytes);
                }
                touched = now();
                if(resumable && (touched-checkpointed).TotalSeconds>=5)Checkpoint();
                return CompleteIfReady();
            }
        }
        public CompletedSnapshot CompleteIfReady()
        {
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException(nameof(SnapshotReceiver));
                if(manifest==null || received!=manifest.Parts)return null;
                try
                {
                    file.Flush(true); file.Position = 0;
                    if (SnapshotLimits.Hash(file) != manifest.Sha256) throw new InvalidDataException("Snapshot SHA-256 mismatch");
                    file.Dispose(); file = null;
                    DeleteMetadata();
                    var done = new CompletedSnapshot(manifest, path);
                    path = null; manifest = null; seen = null; received = 0;
                    return done;
                }
                catch { Clear(); throw; }
            }
        }
        public bool Expire()
        {
            lock (gate)
            {
                if (manifest == null || now() - touched <= TimeSpan.FromSeconds(SnapshotLimits.ReceiveIdleSeconds) && now() - begun <= TimeSpan.FromSeconds(SnapshotLimits.TransferHardSeconds)) return false;
                // A stalled resumable download keeps its verified chunks for the
                // retry; deleting them made every slow attempt start from zero.
                if (resumable) Retain(); else Clear();
                return true;
            }
        }
        void Clear()
        {
            file?.Dispose(); file = null;
            if (path != null && File.Exists(path)) File.Delete(path);
            if(path!=null)DeleteMetadata();
            path = null; manifest = null; seen = null; received = 0;
        }
        public void Dispose() { lock (gate) { disposed = true; timer.Dispose(); Clear(); } }
    }
}
