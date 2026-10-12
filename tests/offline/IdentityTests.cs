using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;

namespace MPFever
{
    static partial class OfflineTests
    {
        static void IdentityTests()
        {
            var identities = new PeerIdentities();
            var first = identities.Join("A",null), other = identities.Join("A",null);
            Check(first.Id != other.Id && first.Token != other.Token && SnapshotLimits.Hex(first.Token,64), "equal names receive different random identities");
            Check(identities.Join("renamed",first.Token) == first, "credential preserves identity and name across reconnect");
            Reject<InvalidDataException>(() => identities.Join("A",new string('0',64)), "forged credential cannot claim name");
            Reject<InvalidDataException>(() => new PeerIdentities().Join("A",first.Token), "credential is scoped to one host session");
            Reject<InvalidDataException>(() => identities.Join("A","bad"), "malformed credential");
            using (var relay = new Relay())
            using (var one = new Client())
            using (var two = new Client())
            using (var reconnected = new Client())
            {
                int left = 0; var oneClosed = new ManualResetEventSlim();
                relay.Joined += p => relay.DropOlder(p);
                relay.Left += p => Interlocked.Increment(ref left);
                one.Closed += () => oneClosed.Set();
                relay.Start(0);
                one.Connect("127.0.0.1",relay.Port,"same");
                Wait(() => one.ResumeToken != null,"real TCP server issues first credential");
                string originalName = relay.PeerNames().Single();
                two.Connect("127.0.0.1",relay.Port,"same");
                Wait(() => two.ResumeToken != null && relay.Count == 2,"real TCP name collision keeps both connections");
                Check(one.ResumeToken != two.ResumeToken && !oneClosed.IsSet && left == 0,"matching name cannot evict or reuse credential");
                reconnected.ResumeToken = one.ResumeToken;
                reconnected.Connect("127.0.0.1",relay.Port,"same");
                Check(oneClosed.Wait(5000),"valid credential evicts only its old socket");
                Wait(() => relay.Count == 2,"reconnect leaves other identity connected");
                Check(relay.PeerNames().Contains(originalName) && left == 0,"stable identity survives replacement without false peerleft");
                using (var forged = new TcpClient("127.0.0.1",relay.Port))
                {
                    var b = Wire.Encode(Msg.Make("join","same","{[\"protocol\"]="+Wire.Version+",[\"resume\"]=\""+new string('0',64)+"\"}"));
                    forged.GetStream().Write(b,0,b.Length); forged.ReceiveTimeout = 5000;
                    Check(forged.GetStream().ReadByte() == -1,"real TCP forged credential rejected before joining");
                }
                Check(relay.PeerNames().Length == 2,"forged reconnect cannot remove actual players");
            }
        }
    }
}
