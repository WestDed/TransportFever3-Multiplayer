using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MPFever
{
    // Limits are checked before a frame or file delta can grow the allocation.
    static class Wire
    {
        public const int Version = 2;
        public const int MaxFrameBytes = LuaLit.MaxBytes + 256;
        public const int MaxPeers = 8;
        public const int HandshakeMs = 10000;
        public const int PartialFrameMs = 60000;
        public const int SendTimeoutMs = 60000;
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool ValidEnvelope(Msg m)
        {
            if (m == null || string.IsNullOrEmpty(m.Kind) || m.Kind.Length > 32 || m.From == null || m.From.Length > 64 || m.Payload == null) return false;
            foreach (char c in m.Kind) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')) return false;
            foreach (char c in m.From) if (char.IsControl(c)) return false;
            if (m.Payload.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0) return false;
            try { return Utf8.GetByteCount(m.Payload) <= LuaLit.MaxBytes && Utf8.GetByteCount(m.ToString()) <= MaxFrameBytes; }
            catch (EncoderFallbackException) { return false; }
        }

        public static byte[] Encode(Msg m)
        {
            if (!ValidEnvelope(m)) throw new InvalidDataException("Invalid or oversized message envelope");
            return Utf8.GetBytes(m + "\n");
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
        int pos, count;
        public BoundedLineReader(Stream stream, int limit = Wire.MaxFrameBytes, int partialMs = Wire.PartialFrameMs)
        { this.stream = stream; line = new LineAccumulator(limit); this.partialMs = partialMs; }
        public string ReadLine(int idleMs = -1)
        {
            var idle = Stopwatch.StartNew();
            Stopwatch partial = null;
            while (true)
            {
                while (pos < count)
                {
                    byte b = buffer[pos++];
                    if (b == 10) return line.Finish();
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
}
