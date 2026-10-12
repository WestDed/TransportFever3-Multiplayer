using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace MPFever
{
    sealed class PeerIdentity
    {
        public readonly int Id;
        public readonly string Token, Name;
        public PeerIdentity(int id, string token, string name) { Id = id; Token = token; Name = name; }
    }

    // Session-scoped bearer credential, NOT Steam authentication. Only the server
    // creates credentials; public names and client-provided Steam IDs are no proof.
    sealed class PeerIdentities
    {
        readonly object gate = new object();
        readonly Dictionary<string, PeerIdentity> identities = new Dictionary<string, PeerIdentity>();
        public PeerIdentity Join(string name, string resume)
        {
            lock (gate)
            {
                if (resume != null)
                {
                    if (!SnapshotLimits.Hex(resume, 64) || !identities.TryGetValue(resume, out var old))
                        throw new InvalidDataException("Invalid or expired reconnection credential");
                    return old;
                }
                if (identities.Count >= 4096) throw new InvalidDataException("Session identity capacity reached");
                var bytes = new byte[32];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                string token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                var identity = new PeerIdentity(identities.Count + 1, token, name);
                identities.Add(token, identity);
                return identity;
            }
        }
    }
}
