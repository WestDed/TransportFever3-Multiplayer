"""Launcher source wiring checks. These are not a Windows UI/game launch test."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]


def body(source, start, end):
    return source[source.index(start):source.index(end, source.index(start))]


class PrelaunchWiringTests(unittest.TestCase):
    def setUp(self):
        self.main=(ROOT/'launcher/MainForm.cs').read_text(encoding='utf-8')
        self.lobby=(ROOT/'launcher/MainForm.Lobby.cs').read_text(encoding='utf-8')
        self.cache=(ROOT/'launcher/MainForm.SnapshotCache.cs').read_text(encoding='utf-8')

    def test_default_init_opens_lobby_without_launching_tf3(self):
        init=body(self.main,'void Init()','// ---------------------------------------------------------------- main menu mode')
        self.assertIn('StartLobbyMode()',init)
        self.assertNotIn('StartMenuMode()',init)
        lobby=body(self.lobby,'void StartLobbyMode()','void HostLobby()')
        self.assertNotIn('.Launch(',lobby)
        self.assertIn('new Thread(MenuLoop)',lobby)

    def test_actual_host_and_join_buttons_use_prelaunch_paths(self):
        self.assertIn('if(prelaunchLobby)HostLobby();',self.main)
        self.assertIn('if(prelaunchLobby)JoinLobby(hostBox.Text.Trim());',self.main)
        host=body(self.lobby,'void HostLobby()','void JoinLobby(')
        self.assertIn('OnMenuRequest("host"',host)
        join=body(self.lobby,'void JoinLobby(','void BroadcastLobby()')
        self.assertIn('OnMenuRequest("join"',join)

    def test_launch_is_guarded_by_authoritative_readiness(self):
        start=body(self.lobby,'void LaunchLobby()','void LaunchLobbyGame()')
        self.assertIn('room.Prepare(room.Revision)',start)
        self.assertIn('SnapshotExchange.Send(source,hostName,preparation.Decision(peer)',start)
        self.assertIn('preparation.AllCommitted',start)
        self.assertIn('room.LaunchPrepared(revision,source.Manifest.Transfer)',start)
        self.assertIn('QueueLobbyLaunch(room.Id',start)
        staging=body(start,'using(var staged=','if(saved==0)')
        self.assertIn('lock(snapshotCommitGate)',staging)
        self.assertLess(staging.index('if(!Current())return;'),staging.index('staged.CommitTo'))
        self.assertNotIn('BroadcastLobby();LaunchLobbyGame();',start)
        receive=body(self.lobby,'void ReceiveLobby(','void LaunchLobby()')
        self.assertIn('view.Members.Values.All',receive)
        self.assertIn('view.Content!=lobbyContent',receive)
        self.assertIn('view.Snapshot!=lobbyCommittedTransfer',receive)
        self.assertIn('QueueLobbyLaunch(view.Room,',receive)
        scheduled=body(self.lobby,'void QueueLobbyLaunch(','void LaunchLobbyGame()')
        self.assertIn('lock(sessionGate)',scheduled)
        self.assertIn('generation==sessionGen',scheduled)

    def test_cache_negotiation_is_on_live_network_path(self):
        self.assertIn('OnSnapshotOffer(game,m,client)',self.main)
        self.assertIn('SnapshotExchange.Send(source,hostName,probe',self.main)
        self.assertIn('snapshotPeers.TryGetValue(from.Name,out var expectedPeer)',self.main)
        self.assertIn('ReferenceEquals(expectedPeer,from)',self.main)
        self.assertIn('snapshotCache.Find(manifest,hasPartial?Enumerable.Empty<string>():LocalSnapshotCandidates()',self.cache)
        self.assertIn('CommitSnapshot(game,found,client,message.From,true,Active)',self.cache)
        self.assertIn('resync_need',self.cache)

    def test_host_waiting_for_clients_does_not_resume_before_initial_snapshot(self):
        auto=body(self.main,'void AutoStart(','// ---------------------------------------------------------------- autotest')
        self.assertIn('prepared.AllReady && ClocksReady()',auto)
        self.assertIn('prepared.Present(relay.Peers())',auto)
        self.assertIn('StartSession(!waiting)',auto)
        initial=auto[auto.index('var prepared=lobbyPreparation;'):auto.index('// With waiting clients')]
        self.assertNotIn('Resync(',initial)
        self.assertIn('StartSession();',initial)
        final=initial[initial.index('lock(sessionGate)'):]
        self.assertLess(final.index('if(!Current())return;'),final.index('StartSession();'))
        start=body(self.main,'void StartSession(','void SetSpeed(')
        self.assertIn('if(resume && startGen==sessionGen)',start)
        self.assertIn('speed = resume ? 0 : 1',start)

    def test_cancelled_cache_worker_cannot_request_a_new_session_load(self):
        self.assertIn('cacheProbes.Clear()',self.main)
        self.assertIn('cacheProbes.Remove(game)',self.main)
        self.assertIn('gen==sessionGen',self.cache)
        commit=self.cache[self.cache.index('void CommitSnapshot('):]
        self.assertIn('lock(snapshotCommitGate)',commit)
        self.assertIn('lock(sessionGate)',commit)
        self.assertIn('lock(sessionGate) { sessionGen++;clientCancelled = true; }',self.main)
        self.assertLess(commit.index('if(!current())return;'),commit.index('completed.CommitTo'))
        self.assertLess(commit.rindex('if(!current())return;'),commit.index('loadingClients[game]'))

    def test_missing_clients_abort_before_host_reload(self):
        recovery=body(self.main,'void Resync(string reason)','// client side:')
        self.assertIn('activeRelay.Count==0',recovery)
        self.assertIn('peers.Length==0',recovery)
        self.assertIn('|| !Present(peers)',recovery)


if __name__=='__main__':
    unittest.main()
