using System;
using System.IO;
using System.Linq;

namespace MPFever
{
    static partial class OfflineTests
    {
        static void CheckpointTests()
        {
            var s = new CheckpointState();
            s.Observe(1, new[] { "money", "edges" }, new[] { "edges" });
            s.Observe(2, new[] { "money", "conParams" }, new string[0]);
            Check(s.Pending.SequenceEqual(new[] { "edges" }) && s.Streak(s.Pending) == 1, "rotated-out mismatch stays pending without gaining a false streak");
            s.Observe(4, new[] { "money", "edges" }, new[] { "edges" });
            Check(s.Streak(s.Pending) == 2, "two observations of the same part confirm persistent difference");
            s.Observe(3, new[] { "edges" }, new string[0]);
            Check(s.Streak(s.Pending) == 2, "late equal checkpoint cannot clear newer mismatch");
            s.Observe(7, new[] { "edges" }, new string[0]);
            Check(s.Pending.Count == 0, "fresh equal observation clears that part");
            s.Observe(6, new[] { "edges" }, new[] { "edges" });
            Check(s.Pending.Count == 0, "late mismatch cannot reintroduce resolved drift");
            s.Observe(8, new[] { "money", "lines" }, new[] { "money", "lines" });s.Clear();
            Check(s.Pending.Count == 0 && s.Streak(new[] { "lines" }) == 0, "session and snapshot reset checkpoint memory");
            Reject<InvalidDataException>(() => s.Observe(9, Enumerable.Range(0, 257).Select(i => "p" + i), new string[0]), "checkpoint memory bound rejects excessive parts");
            Check(s.Pending.Count == 0, "invalid checkpoint does not partially change memory");
        }
    }
}
