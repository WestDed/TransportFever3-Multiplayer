using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MPFever
{
    // A rotated-out part is unknown at this checkpoint, not suddenly equal.
    // Each part counts its own observations; late answers cannot erase newer ones.
    sealed class CheckpointState
    {
        sealed class Part { public int Number, Streak; }
        readonly object gate = new object();
        readonly Dictionary<string, Part> parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        public void Clear() { lock (gate) parts.Clear(); }
        public void Observe(int number, IEnumerable<string> measured, IEnumerable<string> different)
        {
            var keys = measured.Distinct(StringComparer.Ordinal).ToArray();
            var bad = new HashSet<string>(different, StringComparer.Ordinal);
            if (number < 1 || keys.Length > 256 || keys.Any(k => string.IsNullOrEmpty(k) || k.Length > 128))
                throw new InvalidDataException("Invalid checkpoint parts");
            lock (gate)
            {
                if (parts.Count + keys.Count(k => !parts.ContainsKey(k)) > 256) throw new InvalidDataException("Too many checkpoint parts");
                foreach (string key in keys)
                {
                    if (!parts.TryGetValue(key, out var p)) parts[key] = p = new Part();
                    if (number <= p.Number) continue;
                    p.Number = number;
                    p.Streak = bad.Contains(key) ? Math.Min(p.Streak + 1, int.MaxValue - 1) : 0;
                }
            }
        }
        public List<string> Pending { get { lock (gate) return parts.Where(k => k.Value.Streak > 0).Select(k => k.Key).OrderBy(k => k, StringComparer.Ordinal).ToList(); } }
        public int Streak(IEnumerable<string> keys)
        { lock (gate) return keys.Select(k => parts.TryGetValue(k, out var p) ? p.Streak : 0).DefaultIfEmpty(0).Max(); }
    }
}
