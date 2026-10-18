using System;
using System.IO;
using System.Threading;
namespace MPFever
{
    static partial class OfflineTests
    {
        static void SpeedRegressionTests()
        {
            for(int speed=0;speed<=4;speed++)foreach(string kind in new[]{"clock","speed_req"})
                Check(ProtocolSchema.ValidateClient(Msg.Make(kind,"Peer",kind=="clock"?"{[\"t\"]=10,[\"sp\"]="+speed+"}":"{[\"speed\"]="+speed+"}"))!=null,"real speed domain "+kind+"="+speed);
            foreach(var bad in new[]{"-1","5","1.5","\"3\"","true","{}"})foreach(var kind in new[]{"clock","speed_req"})
                Reject<InvalidDataException>(()=>ProtocolSchema.ValidateClient(Msg.Make(kind,"Peer",kind=="clock"?"{[\"t\"]=10,[\"sp\"]="+bad+"}":"{[\"speed\"]="+bad+"}")),"speed type/range still bounded "+bad);
            using(var relay=new Relay())using(var client=new Client())
            {
                int clocks=0,requests=0;
                relay.Received+=(p,m)=> {if(m.Kind=="clock")Interlocked.Increment(ref clocks);if(m.Kind=="speed_req")Interlocked.Increment(ref requests);};
                relay.Start(0);client.Connect("127.0.0.1",relay.Port,"speed");
                client.Send(Msg.Make("clock","speed","{[\"t\"]=10,[\"sp\"]=3}"));
                client.Send(Msg.Make("speed_req","speed","{[\"speed\"]=3}"));
                Wait(()=>clocks==1 && requests==1,"real relay keeps speed-3 peer connected instead of reconnect loop");
            }
        }
    }
}
