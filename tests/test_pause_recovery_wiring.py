"""Launcher source wiring of the pause/recovery fixes (the decision logic itself runs in tests/offline/RecoveryGateTests.cs).
These are not a Windows UI or game test."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


def body(source, start, end):
    i = source.index(start)
    return source[i:source.index(end, i + len(start))]


class PauseRecoveryWiringTests(unittest.TestCase):
    def setUp(self):
        self.main = (ROOT / 'launcher/MainForm.cs').read_text(encoding='utf-8')
        self.lobby = (ROOT / 'launcher/MainForm.Lobby.cs').read_text(encoding='utf-8')

    def test_no_permanent_resume_latch_remains(self):
        self.assertNotIn('resyncFailed', self.main)
        speed = body(self.main, 'void SetSpeed(', 'void Pause(')
        self.assertIn('recovery.Resume(resyncing, hostOverride, out bool overridden)', speed)

    def test_host_buttons_and_host_game_may_override(self):
        for n in (1, 2, 4):
            self.assertIn('x%dBtn.Click += (s, e) => SetSpeed(%d, T("hôte", "host"), true);' % (n, n), self.main)
        req = body(self.main, 'case "speed_req":', 'case "sync_hash":')
        self.assertIn('SetSpeed((int)d, m.From, from == null)', req)

    def test_failure_is_only_latched_after_the_games_were_paused(self):
        resync = body(self.main, 'void Resync(string reason)', '// client side: the host')
        self.assertLess(resync.index('Pause(T("resynchronisation"'), resync.index('recovery.Freeze()'))
        self.assertLess(resync.index('no clients remain; host world is not reloaded'), resync.index('recovery.Freeze()'))
        final = resync[resync.index('finally'):]
        self.assertIn('recovery.Failure(Now)', final)
        self.assertIn('frozen && activeRelay.Peers().Length == 0', final)

    def test_retry_and_deferred_request_are_started_by_the_host_loop(self):
        loop = body(self.main, 'void HostLoop(int gen)', 'void OnHostMessage(')
        self.assertIn('recovery.Due(Now)', loop)
        self.assertIn('Resync("automatic retry")', loop)
        failed = body(self.main, 'case "replay_failed":', 'case "resync_need":')
        self.assertIn('recovery.Request(lastResync + 15)', failed)

    def test_pause_lets_the_games_reach_their_stop_point(self):
        pause = body(self.main, 'void Pause(string who)', 'void DenyResume(')
        self.assertIn('if (speed < 1) speed = 1;', pause)

    def test_transfers_wait_for_progress_not_half_an_hour(self):
        for src in (self.main, self.lobby):
            self.assertNotIn('TransferTimeoutSeconds', src)
        self.assertIn('new TransferProgress(', self.lobby)
        resync = body(self.main, 'void Resync(string reason)', '// client side: the host')
        self.assertEqual(len(re.findall(r'WaitProgress\(\(\) =>', resync)), 2)


if __name__ == '__main__':
    unittest.main()
