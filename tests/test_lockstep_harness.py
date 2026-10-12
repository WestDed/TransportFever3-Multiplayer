"""Regression checks of the fake engine/loader used by the real Lua lockstep scenarios."""
import contextlib
import io
import os
import tempfile
import unittest
from unittest.mock import patch

from test_lockstep import Game, run, unexpected_logs


class HarnessTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.game = Game('HarnessHost', 'host', self.tmp.name, 50000)

    def test_namespace_aliases_share_real_modules(self):
        for name in ('mpfever_common.lua', 'mpfever_refs.lua', 'mpfever_co.lua'):
            self.assertTrue(self.game.L.eval(f'ug_require("{name}")==ug_require("mpfever_1::/{name}")'))
        self.game.L.execute('local CO=ug_require("mpfever_co.lua"); local reg=CO.new("separate",7); local c,n=CO.join(reg,"guest","Guest",false); assert(n and c.id==2)')

    def test_process_environment_does_not_leak_into_games(self):
        with patch.dict(os.environ, {'MPFEVER_NAME': 'external', 'MPFEVER_CPROBE': '1'}):
            other = Game('HarnessClient', 'client', self.tmp.name, 51000)
            self.assertEqual(other.L.eval('os.getenv("MPFEVER_NAME")'), 'HarnessClient')
            self.assertIsNone(other.L.eval('os.getenv("MPFEVER_CPROBE")'))
            self.assertEqual(os.environ['MPFEVER_NAME'], 'external')
        self.assertEqual(self.game.L.eval('os.getenv("MPFEVER_NAME")'), 'HarnessHost')

    def test_windows_paths_reach_same_fixture_files(self):
        self.game.L.execute(r'local C=ug_require("mpfever_common.lua"); assert(C.appendFile(C.DIR..C.BS.."probe.txt","one\ntwo\n"))')
        self.assertEqual(self.game.read('probe.txt'), ['one', 'two'])

    def test_exact_steps_advance_while_speed_zero(self):
        self.game.L.execute('api.cmd.sendCommand(api.cmd.debug.makeGamePerformSimulationStepsCmd(3)); SIMSTEP()')
        self.assertEqual(self.game.time(), 18600 + 3 * 200)
        self.assertEqual(self.game.L.eval('GAME.speed'), 0)
        self.assertEqual(self.game.applied(), [])
        self.game.L.execute('SIMSTEP()')
        self.assertEqual(self.game.time(), 19200)

    def test_delayed_vehicle_appears_next_engine_step(self):
        self.game.L.execute('GAME.engine=true; GAME.inUpdate=true; GAME_EXECUTE(api.cmd.makeVehicleBuyCmd(7,55,{})); GAME.engine=false; GAME.inUpdate=false')
        self.assertTrue(self.game.L.eval('WORLD[50001]==nil'))
        self.game.L.execute('GAME.speed=1; SIMSTEP()')
        self.assertTrue(self.game.L.eval('WORLD[50001].TRANSPORT_VEHICLE~=nil'))

    def test_diagnostics_gate_sees_errors_caught_by_lua(self):
        samples = ['simulation apply failed: bad reference', 'callback error: missing id',
                   'LATE action x', 'NATIVE REPLAY x SEGMENT COUNT DIFFERS']
        self.assertEqual(unexpected_logs('\n'.join(samples)), samples)
        self.assertEqual(unexpected_logs('action x SKIPPED, DESYNC unresolved: depot#9999'), [])

    def test_all_original_scenarios(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            result = run()
        self.assertEqual(result, 0, out.getvalue())
        self.assertNotIn('FAIL ', out.getvalue())
        self.assertEqual(out.getvalue().count('OK   '), 17)


if __name__ == '__main__':
    unittest.main(verbosity=2)
