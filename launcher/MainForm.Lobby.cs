using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using static MPFever.L;

namespace MPFever
{
    sealed partial class MainForm
    {
        bool prelaunchLobby, lobbyGameLaunched,lobbyLaunchFailed;
        LobbyRoom hostLobby;
        volatile LobbyView lobbyView;
        string lobbyContent,lobbyPeerName;
        volatile object lobbyAttempt;
        volatile LobbyPreparation lobbyPreparation;
        string lobbyCommittedTransfer;
        readonly ComboBox lobbySave = new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList };
        readonly CheckBox lobbySeparate = new CheckBox { Checked=true,AutoSize=true };
        readonly ModernButton lobbyReady = new ModernButton(ButtonKind.Secondary);
        readonly ModernButton lobbyLaunch = new ModernButton(ButtonKind.Primary);
        readonly ModernButton lobbyRefresh = new ModernButton(ButtonKind.Ghost);
        readonly ModernButton lobbyCancel = new ModernButton(ButtonKind.Ghost);
        readonly Label lobbyPlayers = new Label { AutoSize=false,Height=100 };
        Card lobbyCard,sessionControls;
        Control BuildLobbyCard()
        {
            lobbyCard=new Card { Visible=MenuModeDefault && !Dev };
            lobbySeparate.Text=T("Une entreprise par joueur", "One company per player");
            lobbyReady.Text=T("Prêt", "Ready");
            lobbyLaunch.Text=T("Lancer Transport Fever 3", "Launch Transport Fever 3");
            lobbyRefresh.Text=T("Actualiser les sauvegardes", "Refresh saves");
            lobbyCancel.Text=T("Annuler", "Cancel");
            foreach(var b in new[]{lobbyReady,lobbyLaunch,lobbyRefresh,lobbyCancel}) { b.ResetFont();b.Fit(20);b.Height=Theme.S(36); }
            lobbyPlayers.Height=Theme.S(96);lobbyPlayers.ForeColor=Theme.Text;
            var options=new FlowLayoutPanel {Height=Theme.S(42),WrapContents=false};
            options.Controls.AddRange(new Control[]{lobbySeparate,lobbyRefresh});
            var actions=new FlowLayoutPanel {Height=Theme.S(44),WrapContents=false};
            actions.Controls.AddRange(new Control[]{lobbyReady,lobbyLaunch,lobbyCancel});
            Ui.StackTop(lobbyCard,Ui.Caption(T("Salon avant lancement", "Prelaunch lobby")),Ui.Field(T("Sauvegarde de l'hôte", "Host save"),lobbySave),
                options,lobbyPlayers,actions);
            Ui.FitHeight(lobbyCard);return lobbyCard;
        }
        void InitLobbyControls()
        {
            lobbyRefresh.Click+=(s,e)=>RefreshLobbySaves();
            lobbyReady.Click+=(s,e)=>
            {
                var v=lobbyView;if(v==null || v.Phase!="waiting")return;
                // Readiness applies to this exact membership revision only.
                bool currentlyReady=lobbyPeerName!=null && v.Members.TryGetValue(lobbyPeerName,out var ownReady) && ownReady;
                Client[] connected;lock(clients)connected=clients.ToArray();
                foreach(var c in connected)c.Send(Msg.Make("lobby_ready",menuGame.Name,v.ReadyPayload(Version,lobbyContent,!currentlyReady)));
            };
            lobbyLaunch.Click+=(s,e)=> {if(lobbyLaunchFailed)LaunchLobbyGame();else LaunchLobby();};
            lobbyCancel.Click+=(s,e)=>
            {
                if(lobbyGameLaunched)return;
                EndSession();hostLobby=null;lobbyView=null;running=false;
                hostBtn.Enabled=joinBtn.Enabled=true;RefreshLobbySaves();
            };
        }
        void RefreshLobbySaves()
        {
            string selected=lobbySave.SelectedItem as string;lobbySave.Items.Clear();
            try
            {
                var names=GameInstall.SaveDirs().SelectMany(d=>Directory.GetFiles(d,"*.sav"))
                    .OrderByDescending(File.GetLastWriteTimeUtc).Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                lobbySave.Items.AddRange(names);
                if(selected!=null && lobbySave.Items.Contains(selected))lobbySave.SelectedItem=selected;
                else if(lobbySave.Items.Count>0)lobbySave.SelectedIndex=0;
                Log.W("Local save catalogue: "+names.Length+" save(s). Exact SHA-256 is checked when the host offers a snapshot.");
            }
            catch(Exception e) { Log.Warn("Save catalogue: "+e.Message); }
        }
        void StartLobbyMode()
        {
            prelaunchLobby=true;menuMode=true;
            menuGame=new GameLink(SavedName(),"menu");games.Add(menuGame);
            menuGame.Exited+=()=> { try { BeginInvoke((Action)Close); } catch { } };
            InitLobbyControls();RefreshLobbySaves();
            lobbyContent=LobbyFiles.Fingerprint(GameInstall.FindModSource(),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"winhttp.dll"));
            LoadSettings().TryGetValue("addr",out var address);if(!string.IsNullOrWhiteSpace(address))hostBox.Text=address;
            MenuStatus();UpdateLobbyUi();
            new Thread(MenuLoop) { IsBackground=true,Name="Lobby/menu link" }.Start();
            Log.W(T("Connectez les joueurs, confirmez leur état prêt, puis lancez le jeu depuis le salon.","Connect players, confirm readiness, then launch the game from the lobby."));
            if(JoinAtStart!=null)BeginInvoke((Action)(()=>Guard(()=>JoinLobby(JoinAtStart))));
        }
        void HostLobby()
        {
            string name=lobbySave.SelectedItem as string;
            if(string.IsNullOrEmpty(name) || GameInstall.FindSave(name)==null)throw new IOException(T("Choisissez une sauvegarde existante. Créez d'abord une carte en solo si nécessaire.","Choose an existing save. Create a map in single player first if needed."));
            hostLobby=new LobbyRoom(Version,lobbyContent,name,lobbySeparate.Checked?"separate":"shared");
            OnMenuRequest("host",name,PlayerName(),hostLobby.Mode);
            if(relay==null) {hostLobby=null;throw new IOException(menuText);}
            BroadcastLobby();
        }
        void JoinLobby(string address)
        {
            OnMenuRequest("join",address,PlayerName());
            if(clients.Count==0)throw new IOException(menuText);
        }
        void BroadcastLobby()
        {
            var room=hostLobby;var network=relay;if(room==null || network==null)return;
            string payload=room.Payload();lobbyView=LobbyView.Parse(payload);
            network.Broadcast(Msg.Make("lobby_state",hostName,payload));
        }
        void ReceiveLobby(string payload)
        {
            var view=LobbyView.Parse(payload);
            var old=lobbyView;
            if(old!=null && old.Room==view.Room && view.Revision<old.Revision)return;
            lobbyView=view;
            if(view.Version!=Version || view.Content!=lobbyContent)throw new InvalidDataException("MPFever files differ; install the same complete package as the host");
            if(view.Phase=="preparing") {menuPhase="downloading";menuText=T("Préparation de la sauvegarde commune avant lancement...","Preparing the common save before game launch...");MenuStatus();}
            if(view.Phase=="waiting" && old!=null && old.Phase=="preparing")
            {SuspendDownloads();lobbyCommittedTransfer=null;menuLoad="";menuLoadSeq="";menuText=T("Connectez les joueurs, confirmez leur état prêt, puis lancez le jeu depuis le salon.","Connect players, confirm readiness, then launch the game from the lobby.");MenuStatus();}
            if(view.Phase=="launching" && !lobbyGameLaunched && lobbyPeerName!=null && view.Members.TryGetValue(lobbyPeerName,out var memberReady) && memberReady)
            {
                if(view.Version!=Version || view.Content!=lobbyContent || !view.Members.Values.All(v=>v))throw new InvalidDataException("Incompatible or unready lobby launch");
                if(view.Snapshot!=lobbyCommittedTransfer)return; // late join waits for a fresh authoritative offer
                QueueLobbyLaunch(view.Room,()=>lobbyView!=null && lobbyView.Phase=="launching" && lobbyView.Snapshot==lobbyCommittedTransfer && clients.Count>0);
            }
        }
        void LaunchLobby()
        {
            var room=hostLobby;
            if(room==null || lobbyGameLaunched)return;
            try
            {
                if(GameInstall.FindSave(room.Save)==null)throw new IOException("Host save no longer exists");
                activeLoadTimeout=SnapshotLimits.LoadTimeoutSeconds(Environment.GetEnvironmentVariable("MPFEVER_LOAD_TIMEOUT_SECONDS"));
                if(LobbyFiles.Fingerprint(GameInstall.FindModSource(),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"winhttp.dll"))!=lobbyContent)throw new IOException("MPFever files changed; recreate the lobby");
                if(!room.Prepare(room.Revision))return;
                var attempt=new object();lobbyAttempt=attempt;lobbyPreparation=null;
                int generation=sessionGen,revision=room.Revision;var network=relay;var peers=network.Peers();
                menuPhase="downloading";menuText=T("Préparation de la sauvegarde commune avant lancement...","Preparing the common save before game launch...");MenuStatus();
                BroadcastLobby();
                new Thread(()=>PrepareLobbySave(room,revision,network,peers,attempt,generation)) {IsBackground=true,Name="Prelaunch common save"}.Start();
            }
            catch(Exception e) { Log.Err("Lobby launch: "+e.Message); }
        }
        void AbortLobbyPreparation()
        {
            lock(sessionGate)
            {
                if(hostLobby==null || !hostLobby.AbortPreparation())return;
                lobbyAttempt=null;lobbyPreparation=null;
                Log.Warn("Prelaunch transfer cancelled. Games stay closed; reconnect, mark Ready and start again to resume the same save.");
            }
        }
        void PrepareLobbySave(LobbyRoom room,int revision,Relay network,Peer[] peers,object attempt,int generation)
        {
            bool Current() => generation==sessionGen && ReferenceEquals(relay,network) && ReferenceEquals(hostLobby,room)
                && ReferenceEquals(lobbyAttempt,attempt) && room.Preparing && room.Revision==revision;
            bool Present() {var live=network.Peers();return live.Length==peers.Length && peers.All(live.Contains);}
            bool success=false;int stop=0;
            try
            {
                using(var source=new SnapshotSource(GameInstall.FindSave(room.Save),Interlocked.Increment(ref resyncId)))
                {
                    if(!Current() || !Present())return;
                    var preparation=new LobbyPreparation(source.Manifest,revision,peers);
                    lock(sessionGate)
                    {
                        if(!Current() || !room.SetSnapshot(revision,source.Manifest.Transfer))return;
                        lobbyPreparation=preparation;BroadcastLobby();
                    }
                    string name="MPFever lobby";int saved=0;
                    using(var staged=source.Stage(Path.Combine(menuGame.Dir,"snapshots"),()=>!Current()))
                    {
                        // Serialize with new-session commits: a cancelled copy
                        // cannot finish after and overwrite their common save.
                        lock(snapshotCommitGate)
                        {
                            foreach(var directory in GameInstall.SaveDirs())
                            {
                                if(!Current())return;
                                try {staged.CommitTo(directory,name);saved++;}
                                catch(IOException e) {Log.Warn("Host lobby destination: "+e.Message);}
                                catch(UnauthorizedAccessException e) {Log.Warn("Host lobby destination: "+e.Message);}
                            }
                        }
                    }
                    if(saved==0)throw new IOException("No host save directory is writable");
                    Log.W("Prelaunch snapshot prepared: "+source.Manifest.Bytes+" bytes, SHA-256 "+source.Manifest.Sha256+"; game processes remain closed");
                    var threads=new List<Thread>();var failures=new List<string>();
                    try
                    {
                        foreach(var peer in peers)
                        {
                            var thread=new Thread(()=>
                            {
                                try
                                {
                                    SnapshotExchange.Send(source,hostName,preparation.Decision(peer),
                                        m=>network.SendBulk(peer,m,false),m=>network.SendBulk(peer,m),
                                        ()=>!Current() || Volatile.Read(ref stop)!=0 || !Present(),
                                        (bytes,seconds)=>Log.W("Prelaunch queued to "+peer.Name+": "+bytes+" bytes, "+(long)(bytes/Math.Max(1,seconds))+" B/s (queueing, not receipt)"));
                                }
                                catch(Exception e) {lock(failures)failures.Add(peer.Name+": "+e.Message);}
                            }) {IsBackground=true,Name="Prelaunch to "+peer.Name};threads.Add(thread);thread.Start();
                        }
                        var deadline=DateTime.UtcNow.AddSeconds(SnapshotLimits.TransferTimeoutSeconds);
                        while(Current() && Present() && DateTime.UtcNow<deadline)
                        {
                            lock(failures)if(failures.Count>0)throw new IOException(string.Join("; ",failures));
                            if(preparation.AllCommitted && threads.All(t=>!t.IsAlive))break;
                            Thread.Sleep(100);
                        }
                        if(!Current() || !Present())return;
                        if(!preparation.AllCommitted)throw new IOException("Prelaunch snapshot receipt deadline exceeded");
                        lock(sessionGate)
                        {
                            if(!Current() || !Present())return;
                            menuLoad=name;menuLoadSeq=SnapshotControl.MenuSequence(source.Manifest.Transfer);
                            if(!room.LaunchPrepared(revision,source.Manifest.Transfer))return;
                            joinPending=false;success=true;MenuStatus();BroadcastLobby();
                            Log.W("Prelaunch verified by all "+peers.Length+" client(s) in "+(int)(DateTime.UtcNow-preparation.Started).TotalSeconds+"s; launching once from the common save");
                            QueueLobbyLaunch(room.Id,()=>ReferenceEquals(lobbyPreparation,preparation));
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref stop,1);
                        // Keep the source's immutable handle until senders stop.
                        foreach(var thread in threads)thread.Join();
                    }
                }
            }
            catch(Exception e) {Log.Err("Prelaunch save: "+e.Message);}
            finally
            {
                if(!success && Current()) {AbortLobbyPreparation();BroadcastLobby();}
            }
        }
        void QueueLobbyLaunch(string room=null,Func<bool> current=null)
        {
            int generation=sessionGen;
            BeginInvoke((Action)(()=>
            {
                lock(sessionGate)
                    if(generation==sessionGen && (current==null || current()) && (room==null || lobbyView!=null && lobbyView.Room==room))LaunchLobbyGame();
            }));
        }
        void LaunchLobbyGame()
        {
            if(lobbyGameLaunched || menuGame==null)return;
            try
            {
                lobbyGameLaunched=true;
                MenuStatus();menuGame.Launch(gameDir);lobbyLaunchFailed=false;
                Log.W("Lobby launched Transport Fever 3 from the verified common save; waiting for engine readiness.");
                UpdateLobbyUi();
            }
            catch(Exception e) { lobbyGameLaunched=menuGame.GameRunning;lobbyLaunchFailed=!lobbyGameLaunched;Log.Err("Game launch failed: "+e.Message);UpdateLobbyUi(); }
        }
        void UpdateLobbyUi()
        {
            if(lobbyCard==null)return;
            if(sessionControls!=null)sessionControls.Visible=!prelaunchLobby || lobbyGameLaunched;
            lobbyCard.Visible=prelaunchLobby && !lobbyGameLaunched;
            if(!prelaunchLobby || lobbyGameLaunched)return;
            var view=lobbyView;bool host=hostLobby!=null,connected=clients.Count>0;
            lobbySave.Enabled=lobbySeparate.Enabled=lobbyRefresh.Enabled=nameBox.Enabled=hostBox.Enabled=portBox.Enabled=!host && !connected;
            hint.Text=!string.IsNullOrEmpty(menuText)?menuText:T("Connectez les joueurs, confirmez leur état prêt, puis lancez le jeu depuis le salon.","Connect players, confirm readiness, then launch the game from the lobby.");
            bool compatible=view!=null && view.Version==Version && view.Content==lobbyContent;
            lobbyReady.Enabled=!host && connected && compatible && view.Phase=="waiting";
            lobbyReady.Active=view!=null && lobbyPeerName!=null && view.Members.TryGetValue(lobbyPeerName,out var ownReady) && ownReady;
            lobbyLaunch.Enabled=lobbyLaunchFailed || host && hostLobby.CanLaunch;
            lobbyCancel.Enabled=running || host || connected;
            lobbyPlayers.Text=view==null?T("Jeu fermé. Créez un salon ou rejoignez l'hôte.","Game closed. Create a lobby or join the host."):
                (!compatible?T("Les fichiers MPFever diffèrent. Installez le même paquet.","MPFever files differ. Install the same package.")+"\n":"")
                +view.Save+(view.Phase=="preparing"?"\n"+T("Préparation de la sauvegarde commune avant lancement...","Preparing the common save before game launch..."):"")+"\n"+string.Join("\n",view.Members.Select(p=>BaseName(p.Key)+": "+(p.Value?T("Prêt","Ready"):T("En attente","Waiting"))));
        }
    }
}
