using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static MPFever.L;

namespace MPFever
{
    sealed partial class MainForm
    {
        readonly object snapshotCommitGate=new object();
        readonly SnapshotCache snapshotCache=new SnapshotCache(SnapshotCache.DefaultDirectory);
        sealed class CacheProbe { public volatile bool Cancelled;public Func<bool> Current; }
        readonly Dictionary<GameLink,CacheProbe> cacheProbes=new Dictionary<GameLink,CacheProbe>();
        readonly Dictionary<GameLink,SnapshotManifest> downloadOffers=new Dictionary<GameLink,SnapshotManifest>();
        sealed class DownloadProgress { public DateTime Started=DateTime.UtcNow,Last=DateTime.UtcNow;public long Reused; }
        readonly Dictionary<GameLink,DownloadProgress> downloadProgress=new Dictionary<GameLink,DownloadProgress>();
        IEnumerable<string> LocalSnapshotCandidates()
        {
            foreach(var directory in GameInstall.SaveDirs())
            {
                string[] candidates;
                try { candidates=Directory.GetFiles(directory,"*.sav").OrderByDescending(File.GetLastWriteTimeUtc).Take(SnapshotCache.MaxCandidates).ToArray(); }
                catch(IOException) { continue; } catch(UnauthorizedAccessException) { continue; }
                foreach(var path in candidates)yield return path;
            }
        }
        void OnSnapshotOffer(GameLink game,Msg message,Client client)
        {
            var manifest=SnapshotManifest.Parse(message.Payload);var probe=new CacheProbe();int gen=sessionGen;
            string preparingRoom=lobbyView!=null && lobbyView.Phase=="preparing"?lobbyView.Room:null;
            lock(snapshotReceivers)
            {
                if(cacheProbes.TryGetValue(game,out var oldProbe))oldProbe.Cancelled=true;
                cacheProbes[game]=probe;downloadOffers[game]=manifest;
            }
            bool Active()
            {
                var view=lobbyView;
                return gen==sessionGen && !probe.Cancelled && !clientCancelled
                    && (preparingRoom==null || view!=null && view.Room==preparingRoom && view.Phase!="waiting" && view.Snapshot==manifest.Transfer);
            }
            probe.Current=Active;
            menuPhase="downloading";menuText=T("Vérification des sauvegardes locales...","Checking local saves...");MenuStatus();
            new Thread(()=>
            {
                var began=DateTime.UtcNow;
                string partialDirectory=Path.Combine(SnapshotCache.DefaultDirectory,"partial",client.Identity.Owner);
                bool hasPartial=SnapshotReceiver.HasPartial(partialDirectory,manifest);
                try
                {
                    var deadline=DateTime.UtcNow.AddSeconds(60);
                    // On an interrupted transfer, do not spend another minute
                    // re-reading unrelated local saves before resuming its data.
                    using(var found=snapshotCache.Find(manifest,hasPartial?Enumerable.Empty<string>():LocalSnapshotCandidates(),()=>!Active() || DateTime.UtcNow>deadline))
                    {
                        if(!Active())return;
                        if(found!=null) {CommitSnapshot(game,found,client,message.From,true,Active);return;}
                    }
                }
                catch(OperationCanceledException) { if(!Active())return;Log.Dim("Local snapshot scan budget exhausted; requesting transfer"); }
                catch(Exception e) { if(!Active())return;Log.Warn("Local snapshot lookup: "+e.Message); }
                try
                {
                    SnapshotReceiver receiver;
                    lock(snapshotReceivers)
                    {
                        if(!Active())return;
                        if(snapshotReceivers.TryGetValue(game,out var old))old.Suspend();
                        receiver=new SnapshotReceiver(partialDirectory,resumable:true);
                        snapshotReceivers[game]=receiver;
                    }
                    var restoreDeadline=DateTime.UtcNow.AddSeconds(100);
                    receiver.Begin(manifest.Payload(),()=>!Active() || DateTime.UtcNow>restoreDeadline);
                    if(!Active())return;
                    using(var done=receiver.CompleteIfReady())
                    {if(done!=null) {CommitSnapshot(game,done,client,message.From,true,Active);return;}}
                    lock(snapshotReceivers)downloadProgress[game]=new DownloadProgress {Reused=receiver.ReceivedBytes};
                    Log.W("["+game.Name+"] local verification took "+(DateTime.UtcNow-began).TotalSeconds.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s; partial verified: "+receiver.ReceivedBytes+"/"+manifest.Bytes+" bytes reusable; requesting missing parts only");
                    if(Active())client.Send(Msg.Make("resync_need",game.Name,"{[\"id\"]="+manifest.Id+",[\"transfer\"]="+LuaLit.Quote(manifest.Transfer)+",[\"have\"]="+LuaLit.Quote(receiver.HaveMask)+"}"));
                }
                catch(Exception e) {if(Active()) {Log.Err("Snapshot resume preparation: "+e.Message);client.Dispose();}}
            }) {IsBackground=true,Name="Snapshot cache verification"}.Start();
        }
        void ReportDownload(GameLink game,SnapshotReceiver receiver)
        {
            DownloadProgress progress;lock(snapshotReceivers)downloadProgress.TryGetValue(game,out progress);
            if(progress==null || (DateTime.UtcNow-progress.Last).TotalSeconds<10)return;
            progress.Last=DateTime.UtcNow;
            Log.W("["+game.Name+"] snapshot receiving: "+receiver.Percent+"%, "+receiver.ReceivedBytes+" verified bytes ("+progress.Reused+" reused), "+(long)(Math.Max(0,receiver.ReceivedBytes-progress.Reused)/Math.Max(1,(progress.Last-progress.Started).TotalSeconds))+" B/s newly received");
        }
        void CommitSnapshot(GameLink game,CompletedSnapshot completed,Client client,string from,bool cached,Func<bool> current)
        {
            lock(snapshotCommitGate)
            {
                if(!current())return;
                var began=DateTime.UtcNow;Log.W("["+game.Name+"] snapshot copy/commit started: "+completed.Manifest.Bytes+" bytes; "+(cached?"local hit":"download verified"));
                string name=ResyncSaveName+" "+new string(game.Name.Where(char.IsLetterOrDigit).Take(40).ToArray());int saved=0;
                foreach(var directory in GameInstall.SaveDirs())
                {
                    if(!current())return;
                    try {completed.CommitTo(directory,name);saved++;}
                    catch(IOException e) {Log.Warn("Snapshot destination: "+e.Message);}
                    catch(UnauthorizedAccessException e) {Log.Warn("Snapshot destination: "+e.Message);}
                }
                if(saved==0)throw new IOException("No snapshot could be committed; game was not asked to load");
                try {snapshotCache.Store(completed);} catch(IOException e) {Log.Warn("Optional snapshot cache: "+e.Message);} catch(UnauthorizedAccessException e) {Log.Warn("Optional snapshot cache: "+e.Message);}
                lock(sessionGate)
                {
                    if(!current())return;
                    lobbyCommittedTransfer=completed.Manifest.Transfer;
                    lock(snapshotReceivers)loadingClients[game]=new LoadingClient {Manifest=completed.Manifest,Client=client,LastSent=DateTime.MinValue};
                    client.Send(Msg.Make("resync_received",game.Name,"{[\"id\"]="+completed.Manifest.Id+",[\"transfer\"]="+LuaLit.Quote(completed.Manifest.Transfer)+"}"));
                    Log.W("["+game.Name+"] host snapshot verified and committed: "+completed.Manifest.Bytes+" bytes; "+(cached?"exact local cache/save hit, no bulk download":"downloaded"));
                    Log.W("["+game.Name+"] snapshot copy/cache/commit took "+(DateTime.UtcNow-began).TotalSeconds.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s; engine load not yet acknowledged");
                    if(menuMode && !game.InGame)
                    {
                        menuPhase="loading";menuText=T("Chargement de la partie de l'hôte...","Loading the host's game...");
                        menuLoad=name;menuLoadSeq=SnapshotControl.MenuSequence(completed.Manifest.Transfer);MenuStatus();
                        // A late joiner has no waiting-room launch packet. Its game is
                        // started only after the authoritative snapshot has been verified.
                        if(prelaunchLobby && !lobbyGameLaunched && (lobbyView==null || lobbyView.Phase=="launching"))QueueLobbyLaunch(current:current);
                        return;
                    }
                    game.ToGame(Msg.Make("resync_load",from,SnapshotControl.Payload(completed.Manifest.Id,name,completed.Manifest.Transfer)));
                }
            }
        }
    }
}
