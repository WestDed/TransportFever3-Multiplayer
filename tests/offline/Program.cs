using System;
using System.Collections.Generic;
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
        static int checks;
        public static void Check(bool ok, string name)
        { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        public static void Reject<T>(Action action, string name) where T : Exception
        { try { action(); } catch (T) { Check(true, name); return; } throw new Exception("Accepted: " + name); }
        public static void Wait(Func<bool> condition, string name)
        {
            var end = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < end) { if (condition()) { Check(true, name); return; } Thread.Sleep(10); }
            throw new Exception("Timed out: " + name);
        }
        static int Main(string[] args)
        {
            try
            {
#if NET8_0_OR_GREATER
                if(args.Length==1 && args[0]=="--bench") return TcpBenchmark.Run();
#endif
                NetworkTests();
                HandshakeTests();
                OwnershipTests();
                CompanyRosterTests();
                SnapshotTests();
                SnapshotControlTests();
                IdentityTests();
                SchemaTests();
                JournalTests();
                Console.WriteLine("Offline C# components: " + checks + " checks passed; no game was run.");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        static void NetworkTests()
        {
            using (var s = new MemoryStream(Wire.Utf8.GetBytes("Привет\r\nnext\n")))
            {
                var r = new BoundedLineReader(s, 13);
                Check(r.ReadLine() == "Привет" && r.ReadLine() == "next" && r.ReadLine() == null, "UTF8 framing and buffered lines");
            }
            using (var s = new MemoryStream(Encoding.ASCII.GetBytes("1234\n")))
                Check(new BoundedLineReader(s, 4).ReadLine() == "1234", "exact byte limit");
            using (var s = new MemoryStream(Encoding.ASCII.GetBytes("12345\n")))
                Reject<InvalidDataException>(() => new BoundedLineReader(s, 4).ReadLine(), "line overflow");
            using (var s = new MemoryStream(new byte[] { 0xff, 10 }))
                Reject<DecoderFallbackException>(() => new BoundedLineReader(s).ReadLine(), "invalid UTF8");
            using (var s = new MemoryStream(Encoding.ASCII.GetBytes("partial")))
                Reject<InvalidDataException>(() => new BoundedLineReader(s).ReadLine(), "truncated final frame");
            foreach (var bad in new[] { "act\ta\t{}\nchat\tb\t{}", "act\ta\t{}\tx", "ACT\ta\t{}", "a\ta\n\t{}", "act\ta", "\ta\t{}" })
                Check(Msg.Parse(bad) == null, "invalid envelope " + bad.Replace("\n", "<LF>"));
            Reject<InvalidDataException>(() => Wire.Encode(Msg.Make("act", "a", new string('x', LuaLit.MaxBytes + 1))), "outbound byte cap");
            var path = System.IO.Path.GetTempFileName();
            try
            {
                var tail = new BoundedFileTail(path, 8);
                File.WriteAllText(path, "abc", Wire.Utf8);
                Check(tail.ReadLines().Count == 0, "IPC incomplete line retained");
                File.AppendAllText(path, "def\n123456789\nok\n", Wire.Utf8);
                Check(tail.ReadLines().SequenceEqual(new[] { "abcdef", "ok" }) && tail.BadLines == 1, "IPC oversized line discarded, next line preserved");
                File.WriteAllText(path, "new\n", Wire.Utf8);
                Check(tail.ReadLines().Single() == "new", "IPC truncate resets offset");
                File.WriteAllText(path, new string('x', 300000) + "\nend\n", Wire.Utf8);
                Check(tail.ReadLines().Count == 0 && tail.ReadLines().Single() == "end", "bounded IPC poll drains huge delta over two calls");
            }
            finally { File.Delete(path); }
            // Actual sockets: fragments, partial EOF, and incomplete frame deadline.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                    using (var server = listener.AcceptTcpClient())
                    {
                        var reader = new BoundedLineReader(server.GetStream(), 64, 100);
                        var stream = client.GetStream();
                        foreach (var b in Wire.Utf8.GetBytes("я\n")) stream.WriteByte(b);
                        Check(reader.ReadLine() == "я", "real TCP fragmented multibyte frame");
                        stream.WriteByte(65);
                        Reject<IOException>(() => reader.ReadLine(), "real TCP partial-frame timeout");
                    }
                }
            }
            finally { listener.Stop(); }
            using (var relay = new Relay())
            using (var client = new Client())
            {
                Peer peer = null; Msg received = null; var left = new ManualResetEventSlim();
                relay.Joined += p => { peer = p; };
                relay.Received += (p, m) => { received = m; };
                relay.Left += p => left.Set();
                relay.Start(0);
                client.Connect("127.0.0.1", relay.Port, "tester");
                Wait(() => peer != null, "real Relay/Client versioned join");
                client.Send(Msg.Make("chat", "spoof", "{[\"text\"]=\"hi\"}"));
                Wait(() => received != null, "real Relay forwards message");
                Check(received.From == peer.Name && received.From != "spoof", "Relay stamps actual sender");
                client.Dispose();
                Check(left.Wait(5000), "real TCP disconnect removes peer");
            }
            using (var relay = new Relay())
            {
                int joined = 0, received = 0;
                relay.Joined += p => Interlocked.Increment(ref joined);
                relay.Received += (p,m) => Interlocked.Increment(ref received);
                relay.Start(0);
                using (var c = new TcpClient("127.0.0.1", relay.Port))
                {
                    var reader=RawChallenge(c,out var challenge);
                    var b = Wire.Encode(Msg.Make("chat", "spoof")); c.GetStream().Write(b,0,b.Length);
                    c.ReceiveTimeout = 5000;
                    Check(Msg.Parse(reader.ReadLine(5000)).Kind=="reject" && joined == 0 && received == 0, "message before join rejected without dispatch");
                }
                using (var c = new TcpClient("127.0.0.1", relay.Port))
                {
                    var reader=RawChallenge(c,out var challenge);
                    var b = Wire.Encode(Msg.Make("join", "old")); c.GetStream().Write(b,0,b.Length);
                    c.ReceiveTimeout = 5000;
                    Check(reader.ReadLine().StartsWith("reject\t"), "old protocol receives explicit rejection");
                    Check(joined == 0, "old client never joins");
                }
            }
            using (var relay = new Relay())
            using (var healthy = new Client())
            {
                Peer slow=null;var delivered=new ManualResetEventSlim();
                relay.Joined+=p=>{if(p.Identity.Name=="slow")slow=p;};
                healthy.Received+=m=>{if(m.Kind=="chatmsg")delivered.Set();};
                relay.Start(0);healthy.Connect("127.0.0.1",relay.Port,"healthy");
                Wait(()=>healthy.ResumeToken!=null,"healthy peer ready for real slow-consumer test");
                using(var raw=new TcpClient("127.0.0.1",relay.Port))
                {
                    raw.ReceiveBufferSize=1024;
                    var reader=RawChallenge(raw,out var challenge);
                    var join=SignedJoin(challenge,OwnerIdentity.Create(),"slow");raw.GetStream().Write(join,0,join.Length);
                    Wait(()=>slow!=null,"real TCP slow peer joined");
                    bool full=false;
                    var large=Msg.Make("chatmsg","Host","{[\"text\"]=\""+new string('a',512*1024)+"\"}");
                    for(int i=0;i<100;i++)if(!relay.SendBulk(slow,large,false)){full=true;break;}
                    Check(full,"real non-reading TCP peer fills bounded outbox");
                    relay.Close(slow);
                    relay.Broadcast(Msg.Make("chatmsg","Host","{[\"text\"]=\"ok\"}"));
                    Check(delivered.Wait(5000),"healthy peer receives broadcast after slow peer is isolated");
                }
            }
        }
    }
}
