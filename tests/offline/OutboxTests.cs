using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static Msg Clock(string name, int time, string from = "Host") =>
            Msg.Make("peerclock", from, "{[\"name\"]=" + LuaLit.Quote(name) + ",[\"t\"]=" + time + ",[\"ah\"]=3}");
        static Msg Session(int time) => Msg.Make("session", "Host", "{[\"started\"]=true,[\"speed\"]=0,[\"pauseAt\"]=" + time + "}");
        static Msg Status(int time) => Msg.Make("mpstatus", "Host", "{[\"text\"]=\"loading " + time + "\"}");

        static void OutboxTests()
        {
            // Keep a real production writer blocked without relying on OS socket
            // buffer sizes. The old fixed reserve fails under this ongoing flood.
            using (var sink = new PausedWriter())
            using (var box = new BoundedOutbox(sink, () => sink.Release.Set(), "Status flood"))
            {
                var bulk = Msg.Make("resync_file", "Host", new string('a', 1024));
                Check(box.Enqueue(bulk) && sink.Started.Wait(5000), "slow transfer has one bulk frame in flight");
                int count = 1;
                while (count < 128 && box.Enqueue(bulk)) count++;
                Check(count < 128, "snapshot producer respects reserved capacity");
                bool accepted = true;
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 1000; i++)
                {
                    accepted &= box.Enqueue(Clock("Host", i));
                    accepted &= box.Enqueue(Session(i));
                    accepted &= box.Enqueue(Status(i));
                }
                Check(accepted, "ongoing periodic statuses do not exhaust a slow snapshot peer's finite reserve");
                Check(watch.ElapsedMilliseconds < 5000, "state updates never wait for a blocked socket");
                Check(box.Enqueue(Msg.Make("act", "Host", "{[\"uid\"]=\"once\"}")), "ordered action still fits after state flood");
                Check(box.Enqueue(Msg.Make("resync_load", "Host", "{}")), "load directive still fits after state flood");
                sink.Release.Set();
                Wait(() => sink.Messages().Length == count + 5, "bounded flood queue drains all chunks and current states");
                var frames = sink.Messages();
                Check(frames.Count(m => m.Kind == "resync_file") == count, "no snapshot chunk is coalesced");
                Check(frames.Single(m => m.Kind == "peerclock").Payload == Clock("Host", 999).Payload
                    && frames.Single(m => m.Kind == "session").Payload == Session(999).Payload
                    && frames.Single(m => m.Kind == "mpstatus").Payload == Status(999).Payload,
                    "latest complete clock session and UI status survive the flood");
                Check(frames[frames.Length - 2].Kind == "act" && frames.Last().Kind == "resync_load",
                    "action and snapshot load remain after their earlier states and chunks");
            }
            using (var sink = new PausedWriter())
            using (var box = new BoundedOutbox(sink, () => sink.Release.Set(), "Ordering barriers"))
            {
                Check(box.Enqueue(Msg.Make("chatmsg", "Host", "{}")) && sink.Started.Wait(5000), "ordering fixture writer is blocked");
                foreach (string barrier in new[] { "act", "resync_begin", "resync_file", "resync_load", "peerleft", "hash", "money" })
                {
                    box.Enqueue(Clock("A", 1)); box.Enqueue(Session(1)); box.Enqueue(Status(1));
                    box.Enqueue(Clock("A", 2)); box.Enqueue(Session(2)); box.Enqueue(Status(2));
                    Check(box.Enqueue(Msg.Make(barrier, "Host", "{}")), "queue accepts ordering barrier " + barrier);
                }
                sink.Release.Set();
                Wait(() => sink.Messages().Length == 29, "state segments drain without crossing ordered frames");
                var frames = sink.Messages();
                int at = 1;
                foreach (string barrier in new[] { "act", "resync_begin", "resync_file", "resync_load", "peerleft", "hash", "money" })
                {
                    Check(frames[at++].Payload == Clock("A", 2).Payload && frames[at++].Payload == Session(2).Payload
                        && frames[at++].Payload == Status(2).Payload && frames[at++].Kind == barrier,
                        "coalescing preserves state and order on each side of " + barrier);
                }
            }
            using (var sink = new PausedWriter())
            using (var box = new BoundedOutbox(sink, () => sink.Release.Set(), "State identity"))
            {
                box.Enqueue(Clock("InFlight", 0)); Check(sink.Started.Wait(5000), "state frame can be in flight");
                box.Enqueue(Clock("A", 1)); box.Enqueue(Session(1)); box.Enqueue(Clock("B", 1));
                box.Enqueue(Clock("A", 2)); box.Enqueue(Clock("A", 3, "OtherHost"));
                box.Enqueue(Clock("InFlight", 1));
                sink.Release.Set();
                Wait(() => sink.Messages().Length == 6, "peer clocks remain distinct and in-flight frame is immutable");
                var frames = sink.Messages();
                Check(frames[0].Payload == Clock("InFlight", 0).Payload && frames[1].Kind == "session"
                    && frames[2].Payload == Clock("B", 1).Payload && frames[3].Payload == Clock("A", 2).Payload,
                    "replacement moves to tail instead of advancing a newer clock ahead of earlier state");
                Check(frames[4].From == "OtherHost" && frames[5].Payload == Clock("InFlight", 1).Payload,
                    "clock key includes player and sender without touching the active write");
            }
            using (var sink = new PausedWriter())
            using (var box = new BoundedOutbox(sink, () => sink.Release.Set(), "Hard cap"))
            {
                box.Enqueue(Msg.Make("act", "Host", "{}")); Check(sink.Started.Wait(5000), "hard cap includes active write");
                int accepted = 1;
                while (accepted < 256 && box.Enqueue(Clock("unique" + accepted, 1))) accepted++;
                Check(accepted == BoundedOutbox.MaxQueuedFrames, "distinct state keys cannot evade the total frame bound");
                Check(!box.Enqueue(Msg.Make("act", "Host", "{}")), "ordered traffic still refuses bounded overflow promptly");
                box.Dispose();
                Check(!box.Enqueue(Clock("unique1", 2)), "closed queue never accepts replacement state");
                sink.Release.Set();
            }
            using (var sink = new PausedWriter())
            using (var box = new BoundedOutbox(sink, () => sink.Release.Set(), "Replacement bytes", 180))
            {
                box.Enqueue(Msg.Make("act", "Host", new string('a', 50))); Check(sink.Started.Wait(5000), "byte cap includes active write");
                var old = Status(1);
                Check(box.Enqueue(old), "small state fits byte cap");
                Check(!box.Enqueue(Msg.Make("mpstatus", "Host", new string('b', 130))), "oversized replacement cannot remove old frame or evade byte limit");
                sink.Release.Set();
                Wait(() => sink.Messages().Length == 2, "failed replacement preserves queued state");
                Check(sink.Messages()[1].Payload == old.Payload, "previous state survives rejected replacement");
            }
        }

        sealed class PausedWriter : Stream
        {
            public readonly ManualResetEventSlim Started = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
            readonly List<Msg> written = new List<Msg>();
            public Msg[] Messages() { lock (written) return written.ToArray(); }
            public override void Write(byte[] buffer, int offset, int count)
            {
                Started.Set();
                if (!Release.Wait(10000)) throw new IOException("Test writer was not released");
                var msg = Msg.Parse(Wire.Utf8.GetString(buffer, offset, count).TrimEnd('\n'));
                lock (written) written.Add(msg);
            }
            protected override void Dispose(bool disposing) { Release.Set(); base.Dispose(disposing); }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Flush() { }
        }
    }
}
