using System;
using System.Collections.Generic;
using System.IO;

namespace MPFever
{
    static partial class OfflineTests
    {
        static void SnapshotControlTests()
        {
            string first = new string('a', 32), restarted = new string('b', 32);
            Check(SnapshotControl.MenuSequence(first) != SnapshotControl.MenuSequence(restarted), "restarted host transfer gets a new menu load token");
            Check(SnapshotControl.MenuSequence(first) == SnapshotControl.MenuSequence(first), "duplicate transfer keeps its menu load token");
            string name = "Хост \"станция\"";
            var packet = (Dictionary<object, object>)LuaLit.Parse(SnapshotControl.Payload(1, name, first));
            Check(packet.Count == 3 && (double)packet["id"] == 1 && (string)packet["name"] == name && (string)packet["request"] == first, "load command data and token survive actual wire decoder");
            var second = (Dictionary<object, object>)LuaLit.Parse(SnapshotControl.Payload(1, name, restarted));
            Check((double)second["id"] == (double)packet["id"] && (string)second["request"] != (string)packet["request"], "reused session counter does not reuse command identity");
            foreach (var bad in new[] { "", "a", new string('a', 31), new string('g', 32), "bad\nresync_load 1" })
                Reject<InvalidDataException>(() => SnapshotControl.Payload(1, name, bad), "invalid control request token rejected");
            Reject<InvalidDataException>(() => SnapshotControl.Payload(0, name, first), "invalid control counter rejected");
            Reject<InvalidDataException>(() => SnapshotControl.Payload(1, "", first), "empty control save name rejected");
        }
    }
}
