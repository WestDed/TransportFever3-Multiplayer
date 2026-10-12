using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;

namespace MPFever
{
    static class TcpBenchmark
    {
        sealed class Result
        {
            public int Peers {get;set;}
            public long SnapshotBytes {get;set;}
            public double Seconds {get;set;}
            public double AggregateMiBPerSecond {get;set;}
            public double CpuSeconds {get;set;}
            public long PeakWorkingSetBytes {get;set;}
            public string Sha256 {get;set;}
        }
        public static int Run()
        {
            var rows=new List<Result>();
            foreach(int peers in new[]{1,4,8}) rows.Add(RunCase(peers,32*1024*1024));
            var report=new {schema=1,gameTested=false,network="loopback TCP",generatedData=true,os=Environment.OSVersion.ToString(),runtime=Environment.Version.ToString(),results=rows};
            Directory.CreateDirectory("audit-results");
            File.WriteAllText("audit-results/tcp-load.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("PASS loopback TCP benchmark, JSON saved to audit-results/tcp-load.json. No game.");
            return 0;
        }
        static Result RunCase(int count,int size)
        {
            string root=Path.Combine(Path.GetTempPath(),"mpfever-load-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var clients=new List<Client>();var receivers=new List<SnapshotReceiver>();
            var errors=new ConcurrentQueue<Exception>();var complete=new CountdownEvent(count);
            int tearingDown=0;
            try
            {
                var path=Path.Combine(root,"generated.bin");var block=new byte[1024*1024];new Random(41).NextBytes(block);
                using(var file=new FileStream(path,FileMode.Create))for(int n=0;n<size;n+=block.Length)file.Write(block,0,Math.Min(block.Length,size-n));
                using(var source=new SnapshotSource(path,1))
                using(var relay=new Relay())
                {
                    relay.Start(0);
                    for(int i=0;i<count;i++)
                    {
                        int index=i;var receiver=new SnapshotReceiver(Path.Combine(root,"receiver-"+i));receivers.Add(receiver);
                        var client=new Client();clients.Add(client);int ended=0;
                        client.Received+=m=>
                        {
                            try
                            {
                                if(m.Kind=="resync_begin")receiver.Begin(m.Payload);
                                else if(m.Kind=="resync_file")
                                {
                                    using(var done=receiver.Add(m.Payload))if(done!=null)
                                    {
                                        if(done.Manifest.Bytes!=size || done.Manifest.Sha256!=source.Manifest.Sha256)throw new Exception("Benchmark snapshot mismatch");
                                        var committed=done.CommitTo(Path.Combine(root,"final-"+index),"bench");
                                        using(var check=File.OpenRead(committed))if(SnapshotLimits.Hash(check)!=source.Manifest.Sha256)throw new Exception("Committed benchmark file mismatch");
                                        if(Interlocked.Exchange(ref ended,1)==0)complete.Signal();
                                    }
                                }
                            }
                            catch(Exception e) {errors.Enqueue(e);if(Interlocked.Exchange(ref ended,1)==0)complete.Signal();throw;}
                        };
                        client.Closed+=()=>
                        {
                            if(Volatile.Read(ref tearingDown)==0 && Interlocked.Exchange(ref ended,1)==0){errors.Enqueue(new IOException("Benchmark peer closed before commit"));complete.Signal();}
                        };
                        client.Connect("127.0.0.1",relay.Port,"bench"+i);
                    }
                    var end=DateTime.UtcNow.AddSeconds(10);
                    while(DateTime.UtcNow<end && clients.Any(c=>c.ResumeToken==null))Thread.Sleep(10);
                    if(clients.Any(c=>c.ResumeToken==null) || relay.Peers().Length!=count)throw new Exception("Benchmark peers did not join");
                    var process=Process.GetCurrentProcess();process.Refresh();long peak=process.WorkingSet64;
                    var cpu=process.TotalProcessorTime;var time=Stopwatch.StartNew();
                    using(var sampler=new Timer(_=>{try{process.Refresh();long rss=process.WorkingSet64;long old=Interlocked.Read(ref peak);while(rss>old){long found=Interlocked.CompareExchange(ref peak,rss,old);if(found==old)break;old=found;}}catch(InvalidOperationException){}},null,0,20))
                    {
                        var peers=relay.Peers();var workers=peers.Select(peer=>new Thread(()=>
                        {try{source.SendTo(m=>relay.SendBulk(peer,m),"Host",()=>Volatile.Read(ref tearingDown)!=0);}catch(Exception e){errors.Enqueue(e);relay.Close(peer);}}){IsBackground=true}).ToArray();
                        foreach(var worker in workers)worker.Start();
                        if(!complete.Wait(TimeSpan.FromMinutes(2)))throw new Exception("Benchmark transfer timed out");
                        if(!workers.All(w=>w.Join(5000)) || !errors.IsEmpty)throw new AggregateException("Benchmark transfer failed",errors);
                        time.Stop();process.Refresh();peak=Math.Max(peak,process.WorkingSet64);
                        var result=new Result {Peers=count,SnapshotBytes=size,Seconds=time.Elapsed.TotalSeconds,AggregateMiBPerSecond=(double)size*count/1048576/time.Elapsed.TotalSeconds,CpuSeconds=(process.TotalProcessorTime-cpu).TotalSeconds,PeakWorkingSetBytes=peak,Sha256=source.Manifest.Sha256};
                        Console.WriteLine(JsonSerializer.Serialize(result));return result;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref tearingDown,1);
                foreach(var client in clients)client.Dispose();foreach(var receiver in receivers)receiver.Dispose();
                Directory.Delete(root,true);
            }
        }
    }
}
