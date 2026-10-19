using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MPFever
{
    // User's actual save size, generated data only. Runs on net8 AND the real
    // Windows net48 runtime; does not depend on Process/working-set access.
    static class LargeSnapshotBenchmark
    {
        const int Bytes=192519583;
        public static int Run()
        {
            string root=Path.Combine(Path.GetTempPath(),"mpf-large-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string file=Path.Combine(root,"generated.sav");var block=new byte[1024*1024];var random=new Random(97);
                using(var output=File.Create(file))for(int i=0;i<Bytes;i+=block.Length)
                {random.NextBytes(block);output.Write(block,0,Math.Min(block.Length,Bytes-i));}
                var rows=new List<string>();
                foreach(bool disk in new[]{false,true})rows.Add(RunCase(root,file,disk));
                Directory.CreateDirectory("audit-results");
                File.WriteAllText("audit-results/large-transfer.json","{\"schema\":1,\"gameTested\":false,\"network\":\"loopback TCP\",\"generatedData\":true,\"runtime\":"+Quote(Environment.Version.ToString())+",\"results\":["+string.Join(",",rows)+"]}");
                Console.WriteLine("PASS 192519583-byte decode-only and resumable-disk TCP benchmark; no game/Porthole.");return 0;
            }
            finally {Directory.Delete(root,true);}
        }
        static string Quote(string value) => "\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
        static string N(double value) => value.ToString("0.000",CultureInfo.InvariantCulture);
        static string RunCase(string root,string path,bool disk)
        {
            using(var source=new SnapshotSource(path,1))using(var relay=new Relay())using(var client=new Client())
            using(var sha=SHA256.Create())using(var done=new ManualResetEventSlim())
            using(var receiver=new SnapshotReceiver(Path.Combine(root,"spool"),resumable:true))
            {
                var windows=new List<string>();Exception failure=null;Thread sender=null;
                var watch=new Stopwatch();long received=0,lastBytes=0;double lastAt=0,callbackSeconds=0;int parts=0;bool stopping=false;
                relay.Joined+=peer=>
                {
                    sender=new Thread(()=>
                    {
                        try {watch.Start();source.SendTo(m=>relay.SendBulk(peer,m),"Host",()=>Volatile.Read(ref stopping));}
                        catch(Exception e) {failure=e;done.Set();}
                    }){IsBackground=true};sender.Start();
                };
                client.Closed+=()=> {if(!stopping && received!=Bytes){failure=new IOException("Large-transfer client closed before verification");done.Set();}};
                client.Received+=m=>
                {
                    var callback=Stopwatch.StartNew();
                    try
                    {
                        if(m.Kind=="resync_begin") {if(disk)receiver.Begin(m.Payload);return;}
                        if(m.Kind!="resync_file")return;
                        var fields=ProtocolSchema.Table(LuaLit.Parse(m.Payload),"part");
                        int index=(int)SnapshotLimits.Integer(fields,"i",0,source.Manifest.Parts-1);
                        if(index!=parts)throw new Exception("Unexpected benchmark part ordering");parts++;
                        int bytes=(int)Math.Min(SnapshotLimits.ChunkBytes,Bytes-received);
                        if(disk)
                        {
                            using(var complete=receiver.Add(fields))if(complete!=null)
                            {
                                string final=complete.CommitTo(Path.Combine(root,"final"),"verified");
                                using(var input=File.OpenRead(final))if(SnapshotLimits.Hash(input)!=source.Manifest.Sha256)throw new Exception("Disk benchmark hash mismatch");
                            }
                        }
                        else
                        {
                            string encoded=SnapshotLimits.Text(fields,"data");
                            if(!SnapshotLimits.CanonicalBase64(encoded,bytes))throw new Exception("Bad benchmark base64");
                            var data=Convert.FromBase64String(encoded);sha.TransformBlock(data,0,data.Length,data,0);
                        }
                        received+=bytes;
                        if(received-lastBytes>=16*1024*1024 || received==Bytes)
                        {
                            double now=watch.Elapsed.TotalSeconds;string row="{\"bytes\":"+received+",\"seconds\":"+N(now-lastAt)+",\"bytesPerSecond\":"+N((received-lastBytes)/Math.Max(0.000001,now-lastAt))+"}";
                            windows.Add(row);Console.WriteLine("LARGE "+(disk?"resumable-disk":"decode-only")+" "+row);lastAt=now;lastBytes=received;
                        }
                        if(received==Bytes)
                        {
                            if(!disk)
                            {
                                sha.TransformFinalBlock(new byte[0],0,0);
                                if(BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant()!=source.Manifest.Sha256)throw new Exception("Decode benchmark hash mismatch");
                            }
                            done.Set();
                        }
                    }
                    catch(Exception e) {failure=e;done.Set();}
                    finally {Interlocked.Exchange(ref callbackSeconds,callbackSeconds+callback.Elapsed.TotalSeconds);}
                };
                relay.Start(0);client.Connect("127.0.0.1",relay.Port,"large");
                try
                {
                    if(!done.Wait(TimeSpan.FromSeconds(180)))throw new Exception("192.5 MB transfer deadline exceeded; investigate sustained throughput, not just initial burst");
                    if(failure!=null)throw failure;
                    if(sender==null || !sender.Join(5000))throw new Exception("Large transfer sender did not stop");
                    // Callback's finally precedes the TCP loop returning to ReadLine.
                    if(received!=Bytes || parts!=source.Manifest.Parts)throw new Exception("Incomplete large transfer");
                    Console.WriteLine("PASS LARGE "+(disk?"resumable-disk":"decode-only")+": "+Bytes+" bytes in "+N(watch.Elapsed.TotalSeconds)+"s; callback "+N(callbackSeconds)+"s");
                    return "{\"mode\":"+Quote(disk?"resumable-disk":"decode-only")+",\"bytes\":"+Bytes+",\"seconds\":"+N(watch.Elapsed.TotalSeconds)+",\"callbackSeconds\":"+N(callbackSeconds)+",\"sha256\":"+Quote(source.Manifest.Sha256)+",\"windows\":["+string.Join(",",windows)+"]}";
                }
                finally {stopping=true;client.Dispose();relay.Dispose();sender?.Join(5000);}
            }
        }
    }
}
