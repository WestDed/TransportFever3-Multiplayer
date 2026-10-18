using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MPFever
{
    // Limits are checked before a frame or file delta can grow the allocation.
    static class Wire
    {
        public const int Version = 10;
        public const int MaxFrameBytes = LuaLit.MaxBytes + 256;
        public const int MaxPeers = 8;
        public const int HandshakeReserve = 4;
        public const int MaxHandshakeBytes = 4096;
        public const int HandshakeMs = 10000;
        public const int PartialFrameMs = 60000;
        public const int SendTimeoutMs = 60000;
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool ValidEnvelope(Msg m)
        {
            if (m == null || string.IsNullOrEmpty(m.Kind) || m.Kind.Length > 32 || m.From == null || m.From.Length > 64 || m.Payload == null || m.Payload.Length > LuaLit.MaxBytes) return false;
            foreach (char c in m.Kind) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')) return false;
            foreach (char c in m.From) if (char.IsControl(c)) return false;
            if (m.Payload.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0) return false;
            try { int bytes=Utf8.GetByteCount(m.Payload);return bytes<=LuaLit.MaxBytes && m.Kind.Length+Utf8.GetByteCount(m.From)+2+bytes<=MaxFrameBytes; }
            catch (EncoderFallbackException) { return false; }
        }

        public static byte[] Encode(Msg m)
        {
            if (!ValidEnvelope(m)) throw new InvalidDataException("Invalid or oversized message envelope");
            return Utf8.GetBytes(m.Kind+"\t"+m.From+"\t"+m.Payload+"\n");
        }
    }

    sealed class LineAccumulator
    {
        readonly int limit;
        byte[] bytes;
        public int Length { get; private set; }
        public LineAccumulator(int limit) { this.limit = limit; bytes = new byte[Math.Min(limit, 4096)]; }
        public void Clear() { Length = 0; }
        public void Add(byte b)
        {
            if (Length == limit) throw new InvalidDataException("Line exceeds the byte limit");
            if (Length == bytes.Length) Array.Resize(ref bytes, Math.Min(limit, bytes.Length * 2));
            bytes[Length++] = b;
        }
        public string Finish()
        {
            int n = Length;
            if (n > 0 && bytes[n - 1] == 13) n--;
            string s = Wire.Utf8.GetString(bytes, 0, n);
            Clear();
            return s;
        }
    }

    // Idle games may legitimately send nothing for minutes while loading. Only the
    // first handshake and a frame that has actually started have deadlines.
    sealed class BoundedLineReader
    {
        readonly Stream stream;
        readonly LineAccumulator line;
        readonly byte[] buffer = new byte[8192];
        readonly int partialMs;
        readonly int limit;
        int pos, count;
        public BoundedLineReader(Stream stream, int limit = Wire.MaxFrameBytes, int partialMs = Wire.PartialFrameMs)
        { this.stream = stream; this.limit = limit; line = new LineAccumulator(limit); this.partialMs = partialMs; }
        // A smaller first-frame cap must keep this reader's already buffered bytes:
        // replacing the reader after join could lose the next pipelined message.
        public string ReadLine(int idleMs = -1, int? maxBytes = null)
        {
            int frameLimit = maxBytes ?? limit;
            if (frameLimit <= 0 || frameLimit > limit) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            var idle = Stopwatch.StartNew();
            Stopwatch partial = null;
            while (true)
            {
                while (pos < count)
                {
                    byte b = buffer[pos++];
                    if (b == 10) return line.Finish();
                    if (line.Length >= frameLimit) throw new InvalidDataException("Line exceeds the byte limit");
                    if (partial == null) partial = Stopwatch.StartNew();
                    line.Add(b);
                }
                int timeout = partial == null ? idleMs : partialMs - (int)Math.Min(int.MaxValue, partial.ElapsedMilliseconds);
                if (idleMs >= 0) timeout = Math.Min(timeout < 0 && partial == null ? idleMs : timeout, idleMs - (int)Math.Min(int.MaxValue, idle.ElapsedMilliseconds));
                if (timeout == 0 || timeout < 0 && (partial != null || idleMs >= 0)) throw new IOException("Message read deadline exceeded");
                if (stream.CanTimeout) stream.ReadTimeout = timeout;
                count = stream.Read(buffer, 0, buffer.Length); pos = 0;
                if (count == 0)
                {
                    if (line.Length != 0) throw new InvalidDataException("Truncated message at end of stream");
                    return null;
                }
            }
        }
    }

    // At most 256 KiB of appended IPC data per poll; incomplete lines are retained.
    // An invalid local producer cannot leave an ever-growing unterminated line.
    sealed class BoundedFileTail
    {
        public readonly string Path;
        readonly LineAccumulator line;
        long offset;
        bool discard;
        public int BadLines { get; private set; }
        public BoundedFileTail(string path, int limit = Wire.MaxFrameBytes) { Path = path; line = new LineAccumulator(limit); }
        public List<string> ReadLines()
        {
            var result = new List<string>();
            if (!File.Exists(Path)) return result;
            using (var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length < offset) { offset = 0; line.Clear(); discard = false; }
                fs.Position = offset;
                var buffer = new byte[8192];
                int budget = 256 * 1024, n;
                while (budget > 0 && (n = fs.Read(buffer, 0, Math.Min(buffer.Length, budget))) > 0)
                {
                    offset += n; budget -= n;
                    for (int i = 0; i < n; i++)
                    {
                        if (buffer[i] == 10)
                        {
                            if (!discard)
                            {
                                try { result.Add(line.Finish()); }
                                catch (DecoderFallbackException) { BadLines++; }
                            }
                            line.Clear(); discard = false;
                        }
                        else if (!discard)
                        {
                            try { line.Add(buffer[i]); }
                            catch (InvalidDataException) { BadLines++; line.Clear(); discard = true; }
                        }
                    }
                }
            }
            return result;
        }
    }

    // One independent writer per connection. A slow peer cannot hold the host's
    // broadcast lock. Bulk producers wait for bounded space on their own worker.
    sealed class BoundedOutbox : IDisposable
    {
        public const int MaxQueuedBytes = 8 * 1024 * 1024, MaxQueuedFrames = 128;
        public const int ReservedControlBytes = 512 * 1024, ReservedControlFrames = 96;
        readonly object gate = new object();
        sealed class Frame
        {
            public byte[] Data;
            public string StateKey;
        }
        readonly LinkedList<Frame> queue = new LinkedList<Frame>();
        // Only the final contiguous state-update segment is replaceable. Any
        // ordered message (including every snapshot part) starts a new segment.
        readonly Dictionary<string, LinkedListNode<Frame>> latest = new Dictionary<string, LinkedListNode<Frame>>(StringComparer.Ordinal);
        readonly Stream stream;
        readonly Action close;
        readonly string name;
        readonly int byteLimit;
        int bytes, frames;
        long coalesced;
        bool stopped;
        public BoundedOutbox(Stream stream, Action close, string name, int byteLimit = MaxQueuedBytes)
        {
            this.stream = stream; this.close = close; this.byteLimit = byteLimit; this.name = name;
            new Thread(WriteLoop) { IsBackground = true, Name = name }.Start();
        }
        public bool Enqueue(Msg m, bool wait = false)
        {
            var data = Wire.Encode(m);
            string key = StateKey(m);
            bool session = m.Kind == "session";
            // A snapshot producer must not consume the slots needed by ordinary
            // status/session messages: Relay.Send would otherwise close a healthy
            // downloading peer. Keep the existing FIFO and total memory limits.
            bool bulk = m.Kind == "resync_begin" || m.Kind == "resync_file";
            int budget = byteLimit - (bulk ? Math.Min(ReservedControlBytes, byteLimit / 8) : 0);
            int frameBudget = bulk ? MaxQueuedFrames - ReservedControlFrames : MaxQueuedFrames;
            if (data.Length > budget) return false;
            var time = Stopwatch.StartNew();
            lock (gate)
            {
                LinkedListNode<Frame> previous;
                while (true)
                {
                    if (stopped) return false;
                    previous = null;
                    if (key != null) latest.TryGetValue(key, out previous);
                    if (session && previous != null)
                    {
                        // A repeated pause/speed heartbeat adds no information.
                        // Keep its EARLIER position so the pause still precedes
                        // later clocks. Every actual session change is a fence.
                        if (data.SequenceEqual(previous.Value.Data)) { coalesced++; return true; }
                        previous = null;
                    }
                    int oldBytes = previous == null ? 0 : previous.Value.Data.Length;
                    int oldFrames = previous == null ? 0 : 1;
                    if (bytes - oldBytes + data.Length <= budget && frames - oldFrames < frameBudget) break;
                    if (!wait || time.ElapsedMilliseconds >= 120000) return false;
                    Monitor.Wait(gate, 100);
                }
                if (previous != null)
                {
                    queue.Remove(previous); bytes -= previous.Value.Data.Length; frames--; coalesced++;
                }
                // Append a replacement at its NEW position. Updating the old
                // slot would let a newer clock overtake earlier traffic.
                if (key == null || session) latest.Clear();
                var node = queue.AddLast(new Frame { Data = data, StateKey = key });
                if (key != null) latest[key] = node;
                bytes += data.Length; frames++;
                Monitor.PulseAll(gate);
                return true;
            }
        }
        static string StateKey(Msg m)
        {
            if (m.Kind == "session" || m.Kind == "mpstatus") return m.From + "\0" + m.Kind;
            if (m.Kind != "peerclock" || m.Payload.Length > 4096) return null;
            // All forwarded clocks have the HOST as From; payload.name is the
            // player whose clock must be kept independently. Malformed/unknown
            // clocks remain ordered instead of being accidentally discarded.
            var data = LuaLit.Parse(m.Payload) as Dictionary<object, object>;
            if (data == null || !data.TryGetValue("name", out var value) || !(value is string name) || name.Length == 0 || name.Length > 128) return null;
            return m.From + "\0peerclock\0" + name;
        }
        public string Describe()
        {
            lock (gate) return "frames=" + frames + "/" + MaxQueuedFrames + ", bytes=" + bytes + "/" + byteLimit
                + ", coalesced=" + coalesced + ", closed=" + stopped;
        }
        void WriteLoop()
        {
            try
            {
                while (true)
                {
                    byte[] data;
                    lock (gate)
                    {
                        while (!stopped && queue.Count == 0) Monitor.Wait(gate);
                        if (stopped) return;
                        var node = queue.First;
                        queue.RemoveFirst();
                        data = node.Value.Data;
                        if (node.Value.StateKey != null && latest.TryGetValue(node.Value.StateKey, out var pending) && pending == node)
                            latest.Remove(node.Value.StateKey);
                    }
                    stream.Write(data, 0, data.Length);
                    lock (gate) { bytes -= data.Length; frames--; Monitor.PulseAll(gate); }
                }
            }
            catch (Exception e)
            {
                bool intentional; lock (gate) intentional = stopped;
                Dispose();
                if (!intentional) Log.Warn(name + " write failed: " + e.Message);
                close();
            }
        }
        public void Dispose()
        {
            lock (gate) { stopped = true; queue.Clear(); latest.Clear(); Monitor.PulseAll(gate); }
        }
    }
}
