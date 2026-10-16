using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static void SnapshotTests()
        {
            string root = Path.Combine(Path.GetTempPath(), "mpfever-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Check(SnapshotLimits.LoadTimeoutSeconds(null) == 1800 && SnapshotLimits.LoadTimeoutSeconds("") == 1800,
                    "large map load default allows 30 minutes instead of fixed 10 minutes");
                Check(SnapshotLimits.LoadTimeoutSeconds("60") == 60 && SnapshotLimits.LoadTimeoutSeconds("7200") == 7200,
                    "load timeout accepts bounded override");
                foreach (var invalid in new[] { "59", "7201", "-1", "0", "NaN", "1.5", "999999999999" })
                    Reject<InvalidDataException>(() => SnapshotLimits.LoadTimeoutSeconds(invalid), "invalid load deadline refused: " + invalid);
                var loadStart=new DateTime(2026,10,15,0,0,0,DateTimeKind.Utc);
                var loading=new LoadLiveness(loadStart,1800);
                Check(!loading.Expired(loadStart.AddSeconds(1799)),"engine loading gets complete soft budget without game clocks");
                loading.Alive(loadStart.AddSeconds(1900));
                Check(!loading.Expired(loadStart.AddSeconds(1959)),"live launcher extends large-map engine load past soft budget");
                Check(loading.Expired(loadStart.AddSeconds(1960)),"missing launcher liveness after soft budget ends wait");
                loading.Alive(loadStart.AddSeconds(7199));
                Check(loading.Expired(loadStart.AddSeconds(7200)),"heartbeats never defeat absolute two-hour deadline");
                for(int n=1;n<=6;n++)Check(SnapshotLimits.CanonicalBase64(Convert.ToBase64String(new byte[n]),n),"canonical base64 padding "+n);
                foreach(var item in new[]{new{Text="AB==",Bytes=1},new{Text="AAB=",Bytes=2},new{Text="AA =",Bytes=2},new{Text="A===",Bytes=1}})
                    Check(!SnapshotLimits.CanonicalBase64(item.Text,item.Bytes),"noncanonical base64 padding refused");
                var bytes = new byte[SnapshotLimits.ChunkBytes * 2 + 3];
                new Random(42).NextBytes(bytes);
                var original = Path.Combine(root, "source.bin"); File.WriteAllBytes(original, bytes);
                var messages = new List<Msg>();
                using (var source = new SnapshotSource(original, 1))
                {
                    source.SendTo(m => { messages.Add(m); return true; }, "Host", () => false);
                    Check(messages.Count == 4 && messages[0].Kind == "resync_begin", "production snapshot source emits manifest and bounded parts");
                    Reject<IOException>(() => source.SendTo(m => false, "Host", () => false), "sender stops on closed peer");
                    Reject<IOException>(() => source.SendTo(m => true, "Host", () => true), "sender obeys cancellation");
                }
                var manifest = SnapshotManifest.Parse(messages[0].Payload);
                Check(manifest.Bytes == bytes.Length && manifest.Parts == 3 && SnapshotLimits.Hex(manifest.Sha256, 64), "manifest size, count, SHA256");
                foreach (var bad in new[] {
                    messages[0].Payload.Replace("[\"bytes\"]=" + bytes.Length, "[\"bytes\"]=1073741825"),
                    messages[0].Payload.Replace("[\"n\"]=3", "[\"n\"]=999999999"),
                    messages[0].Payload.Replace("[\"n\"]=3", "[\"n\"]=2"),
                    messages[0].Payload.Replace("[\"id\"]=1", "[\"id\"]=1.5"),
                    messages[0].Payload.Replace(manifest.Sha256, "../invalid"),
                    messages[0].Payload.Replace(manifest.Transfer, "bad") })
                    Reject<InvalidDataException>(() => SnapshotManifest.Parse(bad), "bad manifest rejected before file allocation");
                string spool = Path.Combine(root, "spool"), destination = Path.Combine(root, "saves");
                using (var receiver = new SnapshotReceiver(spool))
                {
                    Reject<InvalidDataException>(() => receiver.Add(messages[1].Payload), "part before manifest");
                    receiver.Begin(messages[0].Payload); receiver.Begin(messages[0].Payload);
                    foreach (var bad in new[] {
                        messages[1].Payload.Replace("[\"i\"]=0", "[\"i\"]=-1"),
                        messages[1].Payload.Replace("[\"i\"]=0", "[\"i\"]=3"),
                        messages[1].Payload.Replace("[\"n\"]=3", "[\"n\"]=4"),
                        messages[1].Payload.Replace("[\"id\"]=1", "[\"id\"]=2"),
                        messages[1].Payload.Replace(manifest.Transfer, "old"),
                        messages[3].Payload.Replace(Convert.ToBase64String(bytes, bytes.Length-3, 3), "bad?"),
                        messages[3].Payload.Replace(Convert.ToBase64String(bytes, bytes.Length-3, 3), "AAAAAA==") })
                        Reject<InvalidDataException>(() => receiver.Add(bad), "invalid part does not advance state");
                    Check(receiver.Percent == 0, "malformed parts leave progress unchanged");
                    Check(receiver.Add(messages[3].Payload) == null && receiver.Percent == 33, "out-of-order last part accepted");
                    Check(receiver.Add(messages[1].Payload) == null && receiver.Percent == 66, "first part accepted");
                    Check(receiver.Add(messages[1].Payload) == null && receiver.Percent == 66, "identical duplicate idempotent");
                    string oldData = Convert.ToBase64String(bytes, 0, SnapshotLimits.ChunkBytes);
                    var changed = bytes.Take(SnapshotLimits.ChunkBytes).ToArray(); changed[0] ^= 1;
                    Reject<InvalidDataException>(() => receiver.Add(messages[1].Payload.Replace(oldData, Convert.ToBase64String(changed))), "conflicting duplicate refused");
                    Directory.CreateDirectory(destination);
                    var final = Path.Combine(destination, "session.sav"); File.WriteAllText(final, "previous valid save");
                    using (var done = receiver.Add(messages[2].Payload))
                    {
                        Check(done != null, "complete snapshot verified");
                        Reject<InvalidDataException>(() => done.CommitTo(destination, "../escape"), "save name cannot traverse directories");
                        var blocker = Path.Combine(root, "blocker"); File.WriteAllText(blocker, "file");
                        Reject<IOException>(() => done.CommitTo(blocker, "session"), "disk destination error fails before load");
                        Check(File.ReadAllText(final) == "previous valid save", "disk error preserves previous final save");
                        done.CommitTo(destination, "session");
                        Check(File.ReadAllBytes(final).SequenceEqual(bytes), "atomic final save contains exactly source bytes");
                        Check(!Directory.GetFiles(destination, "*.tmp").Any(), "atomic commit cleans temporary file");
                    }
                    Reject<InvalidDataException>(() => receiver.Begin(messages[0].Payload), "completed transfer cannot replay");
                    Reject<InvalidDataException>(() => receiver.Add(messages[2].Payload), "late part cannot restart completed transfer");
                }
                Check(!Directory.GetFiles(spool).Any(), "completed spool cleaned");
                using (var receiver = new SnapshotReceiver(spool))
                {
                    receiver.Begin(messages[0].Payload);
                    var wrong = bytes.Take(SnapshotLimits.ChunkBytes).ToArray(); wrong[0] ^= 1;
                    receiver.Add(messages[1].Payload.Replace(Convert.ToBase64String(bytes,0,wrong.Length), Convert.ToBase64String(wrong)));
                    receiver.Add(messages[2].Payload);
                    Reject<InvalidDataException>(() => receiver.Add(messages[3].Payload), "corruption detected by whole-file SHA256");
                    Check(!Directory.GetFiles(spool).Any(), "corrupt spool deleted, no save committed");
                }
                var now = DateTime.UtcNow;
                using (var receiver = new SnapshotReceiver(spool, () => now))
                {
                    receiver.Begin(messages[0].Payload); receiver.Add(messages[1].Payload);
                    now = now.AddMinutes(4);
                    Check(receiver.Expire() && !Directory.GetFiles(spool).Any(), "abandoned partial snapshot expires");
                    Reject<InvalidDataException>(() => receiver.Begin(messages[0].Payload), "expired transfer cannot replay");
                    var newer = messages[0].Payload.Replace("[\"id\"]=1", "[\"id\"]=2");
                    receiver.Begin(newer);
                    Check(Directory.GetFiles(spool).Length == 1, "new transfer supersedes expired transfer");
                }
                Check(!Directory.GetFiles(spool).Any(), "disconnect/session dispose deletes incomplete spool");
                // Real source -> real outbox -> real TCP -> real receiver, no game file.
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                try
                {
                    using (var sender = new TcpClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port))
                    using (var receiverTcp = listener.AcceptTcpClient())
                    using (var outbox = new BoundedOutbox(sender.GetStream(), () => sender.Close(), "Test snapshot writer"))
                    using (var source = new SnapshotSource(original, 3))
                    using (var receiver = new SnapshotReceiver(spool))
                    {
                        Exception sendError = null;
                        var worker = new Thread(() => { try { source.SendTo(m => outbox.Enqueue(m,true), "Host", () => false); } catch(Exception e) { sendError = e; } });
                        worker.Start();
                        var reader = new BoundedLineReader(receiverTcp.GetStream());
                        receiver.Begin(Msg.Parse(reader.ReadLine(5000)).Payload);
                        CompletedSnapshot done = null;
                        for (int i=0; i<source.Manifest.Parts; i++) done = receiver.Add(Msg.Parse(reader.ReadLine(5000)).Payload);
                        using (done)
                        {
                            Check(done != null, "real TCP whole snapshot verifies");
                            done.CommitTo(destination,"tcp");
                            Check(File.ReadAllBytes(Path.Combine(destination,"tcp.sav")).SequenceEqual(bytes), "real TCP snapshot exact data");
                        }
                        Check(worker.Join(5000) && sendError == null, "real TCP bulk sender finishes");
                    }
                }
                finally { listener.Stop(); }
                // Deterministic blocked writer: queue bounds and another writer's progress.
                using (var blocked = new BlockingStream())
                using (var outbox = new BoundedOutbox(blocked, () => blocked.Release.Set(), "Bulk/control writer", 1000))
                {
                    var bulk = Msg.Make("resync_file", "Host", "{[\"data\"]=\"" + new string('a', 110) + "\"}");
                    Check(outbox.Enqueue(bulk) && blocked.Started.Wait(5000), "snapshot frame is in flight on slow link");
                    int queued = 1;
                    while (queued < 128 && outbox.Enqueue(bulk)) queued++;
                    Check(queued < 128, "bulk producer stops at bounded capacity");
                    Check(outbox.Enqueue(Msg.Make("mpstatus", "Host", "{[\"text\"]=\"loading\"}")),
                        "bulk queue reserves room for host status instead of disconnecting active download");
                    Check(outbox.Enqueue(Msg.Make("session", "Host", "{[\"started\"]=false,[\"speed\"]=0}")),
                        "bulk queue reserves room for pause/session control");
                    outbox.Dispose(); blocked.Release.Set();
                }
                using (var blocked = new BlockingStream())
                using (var slow = new BoundedOutbox(blocked, () => blocked.Release.Set(), "Blocked writer", 100))
                using (var healthyStream = new MemoryStream())
                using (var healthy = new BoundedOutbox(healthyStream, () => {}, "Healthy writer"))
                {
                    var msg = Msg.Make("chat", "P", "{[\"text\"]=\"" + new string('a',50) + "\"}");
                    Check(slow.Enqueue(msg) && blocked.Started.Wait(5000), "slow consumer holds first frame in flight");
                    var watch = Stopwatch.StartNew();
                    Check(!slow.Enqueue(msg) && watch.ElapsedMilliseconds < 1000, "full outbox refuses promptly including in-flight bytes");
                    Check(healthy.Enqueue(Msg.Make("chat", "P")), "another writer accepts while slow peer blocked");
                    Wait(() => healthyStream.Length > 0, "another writer progresses independently");
                    slow.Dispose(); blocked.Release.Set();
                }
            }
            finally { Directory.Delete(root, true); }
        }
        sealed class BlockingStream : Stream
        {
            public readonly ManualResetEventSlim Started = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
            public override void Write(byte[] buffer, int offset, int count) { Started.Set(); Release.Wait(5000); }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Flush() {}
        }
    }
}
