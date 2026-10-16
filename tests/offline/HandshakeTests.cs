using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static void HandshakeTests()
        {
            // The smaller first-frame cap must not lose bytes already read for the
            // next frame or become a permanent cap on normal session traffic.
            using (var stream = new MemoryStream(Wire.Utf8.GetBytes("join\n" + new string('x', 5000) + "\n")))
            {
                var reader = new BoundedLineReader(stream);
                Check(reader.ReadLine(maxBytes: 4) == "join", "first-frame exact byte cap");
                Check(reader.ReadLine().Length == 5000, "post-handshake larger buffered frame is preserved");
            }
            using (var stream = new MemoryStream(Wire.Utf8.GetBytes("яяя\n")))
                Reject<InvalidDataException>(() => new BoundedLineReader(stream).ReadLine(maxBytes: 5), "handshake cap counts UTF8 bytes, not characters");
            using (var stream = new MemoryStream(Wire.Utf8.GetBytes("12345")))
                Reject<InvalidDataException>(() => new BoundedLineReader(stream).ReadLine(maxBytes: 4), "handshake overflow rejected before newline or EOF");
            using (var stream = new MemoryStream())
            {
                var reader = new BoundedLineReader(stream, 16);
                Reject<ArgumentOutOfRangeException>(() => reader.ReadLine(maxBytes: 0), "zero first-frame cap refused");
                Reject<ArgumentOutOfRangeException>(() => reader.ReadLine(maxBytes: 17), "per-frame cap cannot exceed configured reader limit");
            }

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Exception hostError = null;
            var oversizedHost = new Thread(() =>
            {
                try
                {
                    using (var socket = listener.AcceptTcpClient())
                    {
                        var bytes = Wire.Utf8.GetBytes(new string('x', Wire.MaxHandshakeBytes + 1));
                        socket.GetStream().Write(bytes, 0, bytes.Length);
                    }
                }
                catch (Exception e) { hostError = e; }
            }) { IsBackground = true };
            oversizedHost.Start();
            try
            {
                using (var client = new Client())
                {
                    int received = 0, closed = 0;
                    client.Received += m => Interlocked.Increment(ref received);
                    client.Closed += () => Interlocked.Increment(ref closed);
                    string reason = null;
                    try { client.Connect("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, "victim"); }
                    catch (InvalidDataException e) { reason = e.Message; }
                    Check(reason == "Line exceeds the byte limit", "actual Client rejects oversized host handshake at ingress cap");
                    Check(client.ResumeToken == null && received == 0 && closed == 0, "failed host handshake creates no identity or connected lifecycle events");
                }
                Check(oversizedHost.Join(5000) && hostError == null, "oversized host fixture completes without hidden failures");
            }
            finally { listener.Stop(); }

            // Reserve a port without listening: a failed TCP connect must retain
            // SocketException, not turn into an aggregate or closed wait-handle error.
            using (var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (var client = new Client())
            {
                reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                int events = 0;
                client.Received += m => Interlocked.Increment(ref events);
                client.Closed += () => Interlocked.Increment(ref events);
                Reject<SocketException>(() => client.Connect("127.0.0.1", ((IPEndPoint)reserved.LocalEndPoint).Port, "refused"), "failed actual TCP connect preserves original socket exception");
                Check(client.ResumeToken == null && events == 0, "refused TCP connection creates no accepted identity or lifecycle events");
            }

            using (var relay = new Relay())
            using (var healthy = new Client())
            {
                int dispatched = 0;
                var delivered = new ManualResetEventSlim();
                healthy.Received += m => { if (m.Kind == "chatmsg") delivered.Set(); };
                relay.Received += (p, m) => Interlocked.Increment(ref dispatched);
                relay.Start(0);
                healthy.Connect("127.0.0.1", relay.Port, "healthy");
                using (var pending = new TcpClient("127.0.0.1", relay.Port))
                {
                    Wait(() => relay.Count == 2, "pending TCP socket is accepted by listener");
                    var pendingReader=RawChallenge(pending,out var pendingChallenge);
                    Check(relay.Peers().Length == 1, "pending socket has no joined-player authority");
                    relay.Broadcast(Msg.Make("chatmsg", "Host", "{[\"text\"]=\"private session data\"}"));
                    Check(delivered.Wait(5000), "joined peer receives normal session broadcast");
                    Check(!pending.Client.Poll(250000, SelectMode.SelectRead), "pending socket receives no broadcast bytes");

                    // Both messages fit in a single TCP write. After processing the
                    // join, the same reader must deliver the buffered next message.
                    var join=SignedJoin(pendingChallenge,OwnerIdentity.Create(),"pipelined");
                    var chat = Wire.Encode(Msg.Make("chat", "spoof", "{[\"text\"]=\"" + new string('я', 3000) + "\"}"));
                    var data = new byte[join.Length + chat.Length];
                    Buffer.BlockCopy(join, 0, data, 0, join.Length);
                    Buffer.BlockCopy(chat, 0, data, join.Length, chat.Length);
                    pending.GetStream().Write(data, 0, data.Length);
                    var accepted = Msg.Parse(pendingReader.ReadLine(5000, Wire.MaxHandshakeBytes));
                    Check(accepted != null && accepted.Kind == "connection", "acceptance is first frame after a pending socket joins");
                    Wait(() => dispatched == 1, "post-join pipelined frame larger than handshake cap is dispatched once");
                }
            }

            using (var relay = new Relay())
            {
                int joined = 0, dispatched = 0;
                relay.Joined += p => Interlocked.Increment(ref joined);
                relay.Received += (p, m) => Interlocked.Increment(ref dispatched);
                relay.Start(0);
                foreach (var payload in new[] {
                    "{[\"protocol\"]=" + Wire.Version + ",[\"extra\"]=true}",
                    "{[\"protocol\"]=" + Wire.Version + ",[1]=true}",
                    "{[\"protocol\"]=" + Wire.Version + ",[\"resume\"]=false}",
                    "{[\"protocol\"]=" + Wire.Version + ",[\"protocol\"]=" + Wire.Version + "}" })
                {
                    using (var raw = new TcpClient("127.0.0.1", relay.Port))
                    {
                        Wait(() => relay.Count == 1, "malformed join socket accepted for validation");
                        var reader=RawChallenge(raw,out var challenge);
                        relay.Broadcast(Msg.Make("session", "Host", "{[\"private\"]=true}"));
                        var data = Wire.Encode(Msg.Make("join", "attacker", payload));
                        raw.GetStream().Write(data, 0, data.Length);
                        var rejection = Msg.Parse(reader.ReadLine(5000, Wire.MaxHandshakeBytes));
                        Check(rejection != null && rejection.Kind == "reject", "malformed join sees only rejection, no session data");
                    }
                    Wait(() => relay.Count == 0, "rejected handshake releases reserved socket slot");
                }
                using (var oldPeer = new TcpClient("127.0.0.1", relay.Port))
                {
                    var reader = RawChallenge(oldPeer, out var challenge);
                    string valid = Wire.Utf8.GetString(SignedJoin(challenge, OwnerIdentity.Create(), "old-version"));
                    string old = valid.Replace("[\"protocol\"]=" + Wire.Version, "[\"protocol\"]=" + (Wire.Version - 1));
                    Check(old != valid, "old-version fixture changes only protocol in an otherwise signed join");
                    var data = Wire.Utf8.GetBytes(old);oldPeer.GetStream().Write(data, 0, data.Length);
                    var answer = Msg.Parse(reader.ReadLine(5000));
                    Check(answer == null || answer.Kind != "connection", "previous package protocol cannot enter session");
                    Wait(() => relay.Count == 0, "old protocol rejection releases reserved socket");
                }
                using (var oversized = new TcpClient("127.0.0.1", relay.Port))
                {
                    // Deliberately no newline: the 4 KiB ingress cap must reject
                    // before parsing or waiting for a full 4 MiB data frame.
                    var reader=RawChallenge(oversized,out var challenge);
                    var bytes = Wire.Utf8.GetBytes("join\tlarge\t" + new string('x', Wire.MaxHandshakeBytes + 1));
                    oversized.GetStream().Write(bytes, 0, bytes.Length);
                    var rejection = Msg.Parse(reader.ReadLine(5000, Wire.MaxHandshakeBytes));
                    Check(rejection != null && rejection.Kind == "reject", "real TCP oversized unfinished handshake rejected immediately");
                }
                Wait(() => relay.Count == 0, "oversized handshake releases socket slot");
                Check(joined == 0 && dispatched == 0, "bad handshakes never trigger Joined or Received");
            }

            // Exercise the publication race while session traffic is already flowing.
            // Reusing one valid identity keeps this test below the eight-player cap.
            using (var relay = new Relay())
            {
                relay.Start(0);
                int stopBroadcast = 0;
                Exception broadcastError = null;
                var broadcaster = new Thread(() =>
                {
                    try
                    {
                        while (Volatile.Read(ref stopBroadcast) == 0)
                        {
                            relay.Broadcast(Msg.Make("session", "Host", "{[\"speed\"]=1}"));
                            Thread.Sleep(1);
                        }
                    }
                    catch (Exception e) { broadcastError = e; }
                }) { IsBackground = true };
                broadcaster.Start();
                try
                {
                    string token = null; var permanent=OwnerIdentity.Create();
                    for (int i = 0; i < 32; i++)
                    {
                        using (var client = new Client { ResumeToken = token, Identity = permanent })
                        {
                            int firstIsAcceptance = 0;
                            client.Received += m => Interlocked.CompareExchange(ref firstIsAcceptance, m.Kind == "connection" ? 1 : -1, 0);
                            client.Connect("127.0.0.1", relay.Port, "replacement");
                            Check(firstIsAcceptance == 1, "connection acceptance precedes concurrent broadcast, cycle " + i);
                            token = client.ResumeToken;
                        }
                    }
                }
                finally
                {
                    Volatile.Write(ref stopBroadcast, 1);
                    Check(broadcaster.Join(5000), "concurrent handshake broadcaster stops");
                }
                Check(broadcastError == null, "concurrent broadcast does not fail during reconnect");
            }
        }
    }
}
