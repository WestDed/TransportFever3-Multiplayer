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
            if(view.Phase=="launching" && !lobbyGameLaunched && lobbyPeerName!=null && view.Members.TryGetValue(lobbyPeerName,out var memberReady) && memberReady)
            {
                if(view.Version!=Version || view.Content!=lobbyContent || !view.Members.Values.All(v=>v))throw new InvalidDataException("Incompatible or unready lobby launch");
                QueueLobbyLaunch(view.Room);
            }
        }
        void LaunchLobby()
        {
            var room=hostLobby;
            if(room==null || lobbyGameLaunched)return;
            try
            {
                if(GameInstall.FindSave(room.Save)==null)throw new IOException("Host save no longer exists");
                if(LobbyFiles.Fingerprint(GameInstall.FindModSource(),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"winhttp.dll"))!=lobbyContent)throw new IOException("MPFever files changed; recreate the lobby");
                if(!room.Launch(room.Revision))return;
                BroadcastLobby();LaunchLobbyGame();
            }
            catch(Exception e) { Log.Err("Lobby launch: "+e.Message); }
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
                Log.W("Lobby launched Transport Fever 3. Host prepares the authoritative snapshot; clients wait for verification and load.");
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
            hint.Text=T("Connectez les joueurs, confirmez leur état prêt, puis lancez le jeu depuis le salon.","Connect players, confirm readiness, then launch the game from the lobby.");
            bool compatible=view!=null && view.Version==Version && view.Content==lobbyContent;
            lobbyReady.Enabled=!host && connected && compatible && view.Phase=="waiting";
            lobbyReady.Active=view!=null && lobbyPeerName!=null && view.Members.TryGetValue(lobbyPeerName,out var ownReady) && ownReady;
            lobbyLaunch.Enabled=lobbyLaunchFailed || host && hostLobby.CanLaunch;
            lobbyCancel.Enabled=running || host || connected;
            lobbyPlayers.Text=view==null?T("Jeu fermé. Créez un salon ou rejoignez l'hôte.","Game closed. Create a lobby or join the host."):
                (!compatible?T("Les fichiers MPFever diffèrent. Installez le même paquet.","MPFever files differ. Install the same package.")+"\n":"")
                +view.Save+"\n"+string.Join("\n",view.Members.Select(p=>BaseName(p.Key)+": "+(p.Value?T("Prêt","Ready"):T("En attente","Waiting"))));
        }
    }
}
