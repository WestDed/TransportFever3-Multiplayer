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
        readonly Dictionary<GameLink,object> cacheProbes=new Dictionary<GameLink,object>();
        sealed class DownloadProgress { public DateTime Started=DateTime.UtcNow,Last=DateTime.UtcNow; }
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
            var manifest=SnapshotManifest.Parse(message.Payload);var probe=new object();int gen=sessionGen;
            lock(snapshotReceivers)cacheProbes[game]=probe;
            bool Active() { lock(snapshotReceivers)return gen==sessionGen && cacheProbes.TryGetValue(game,out var pending) && ReferenceEquals(probe,pending); }
            menuPhase="downloading";menuText=T("Vérification des sauvegardes locales...","Checking local saves...");MenuStatus();
            new Thread(()=>
            {
                try
                {
                    var deadline=DateTime.UtcNow.AddSeconds(60);
                    using(var found=snapshotCache.Find(manifest,LocalSnapshotCandidates(),()=>!Active() || DateTime.UtcNow>deadline))
                    {
                        if(!Active())return;
                        if(found!=null) {CommitSnapshot(game,found,client,message.From,true,Active);return;}
                    }
                }
                catch(OperationCanceledException) { if(!Active())return;Log.Dim("Local snapshot scan budget exhausted; requesting transfer"); }
                catch(Exception e) { if(!Active())return;Log.Warn("Local snapshot lookup: "+e.Message); }
                if(Active())client.Send(Msg.Make("resync_need",game.Name,"{[\"id\"]="+manifest.Id+",[\"transfer\"]="+LuaLit.Quote(manifest.Transfer)+"}"));
            }) {IsBackground=true,Name="Snapshot cache verification"}.Start();
        }
        void ReportDownload(GameLink game,SnapshotReceiver receiver)
        {
            DownloadProgress progress;lock(snapshotReceivers)downloadProgress.TryGetValue(game,out progress);
            if(progress==null || (DateTime.UtcNow-progress.Last).TotalSeconds<10)return;
            progress.Last=DateTime.UtcNow;
            Log.W("["+game.Name+"] snapshot receiving: "+receiver.Percent+"%, "+receiver.ReceivedBytes+" bytes, "+(long)(receiver.ReceivedBytes/Math.Max(1,(progress.Last-progress.Started).TotalSeconds))+" B/s");
        }
        void CommitSnapshot(GameLink game,CompletedSnapshot completed,Client client,string from,bool cached,Func<bool> current)
        {
            lock(snapshotCommitGate)
            {
                if(!current())return;
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
                    lock(snapshotReceivers)loadingClients[game]=new LoadingClient {Manifest=completed.Manifest,Client=client,LastSent=DateTime.MinValue};
                    client.Send(Msg.Make("resync_received",game.Name,"{[\"id\"]="+completed.Manifest.Id+",[\"transfer\"]="+LuaLit.Quote(completed.Manifest.Transfer)+"}"));
                    Log.W("["+game.Name+"] host snapshot verified and committed: "+completed.Manifest.Bytes+" bytes; "+(cached?"exact local cache/save hit, no bulk download":"downloaded"));
                    if(menuMode && !game.InGame)
                    {
                        menuPhase="loading";menuText=T("Chargement de la partie de l'hôte...","Loading the host's game...");
                        menuLoad=name;menuLoadSeq=SnapshotControl.MenuSequence(completed.Manifest.Transfer);MenuStatus();
                        // A late joiner has no waiting-room launch packet. Its game is
                        // started only after the authoritative snapshot has been verified.
                        if(prelaunchLobby && !lobbyGameLaunched)QueueLobbyLaunch(current:current);
                        return;
                    }
                    game.ToGame(Msg.Make("resync_load",from,SnapshotControl.Payload(completed.Manifest.Id,name,completed.Manifest.Transfer)));
                }
            }
        }
    }
}
