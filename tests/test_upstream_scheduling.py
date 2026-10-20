"""Actual bridge scheduling functions with explicit asynchronous engine fixtures."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'
SOURCE = (CONTENT / 'mpfever_bridge.script.lua').read_text(encoding='utf-8')


def function_source(name):
    start = SOURCE.index('local function ' + name + '(')
    return SOURCE[start:SOURCE.index('\nend\n', start) + 5]


class SchedulingTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.lua.execute('''
            R={entitiesWith=function()return {}end};SIM={};now=100;STEP=200;gameTime=function()return now end
            gameSpeed=function()return 0 end;before=function(a,b)return a.uid<b.uid end
            doneKey=function(a)return a.uid end;coIsLoanEvent=function()return false end
            C.makesProposal=function()return false end;C.appendFile=function()end
            applied={};callbacks={};sent={};sync=false;logs={};log=function(s)logs[#logs+1]=s end
            api={cmd={sendCommand=function()end}};BS="/";NL="\\n";C.DIR="/fixture";O={sendCommand=function()end}
            function executeAction(a,send,paused,bind,cb)
                applied[#applied+1]=a.uid
                local result={uid=a.uid,success=true}
                if cb then if sync then cb(result)else callbacks[#callbacks+1]=function()cb(result)end end end
                return result
            end
            state={value={q={}},get=function(self)return self.value end,set=function(self,v)self.value=v end}
            G={frames=1,session={pauseAt=100},pendingPaused={},bind={},rev={},resultsSeen={},reportResult=function()end,
               peers={Guest={t=100}}}
            PAUSED_ANSWER_FRAMES,PAUSED_ASK_FRAMES,PAUSED_ASKS=600,1800,3
            nativeHoldAt=function()return nil end
            nativeWaiting=function()return false end;toSim=function()end
            send=function(k,p)sent[#sent+1]={kind=k,p=p}end
            PL=function()return nil end;withActor=function(_,fn,...)return fn(...)end
        ''')
        self.lua.execute(function_source('simApplyDue') + '\napply=simApplyDue')
        self.lua.execute(function_source('runPausedActions') + '\npaused=runPausedActions')
        self.lua.execute('''
            JOIN_GATHER=120;nativeArrived=function()end
            pumpTerrain=function()end;pumpCompanies=function()end
            replayNative=function(a)applied[#applied+1]=a.uid;G.replayOut=1;G.replaySince=G.frames end
            G.nativeQueue={}
        ''')
        self.lua.execute(function_source('runNativeReplays') + '\nreplays=runNativeReplays')

    def check(self, code):
        self.lua.execute(code)

    def test_one_purchase_per_game_step_across_repeated_updates(self):
        self.check('''state.value.q={{fn="makeVehicleBuyCmd",at=100,uid="1"},{fn="makeVehicleBuyCmd",at=100,uid="2"}}
            apply(state);apply(state);assert(#applied==1 and #state.value.q==1)
            now=300;apply(state);assert(#applied==2 and #state.value.q==0)''')

    def test_purchase_step_limit_survives_state_serialization(self):
        self.check('''state.value.q={{fn="makeVehicleBuyCmd",at=100,uid="1"},{fn="makeVehicleBuyCmd",at=100,uid="2"}}
            apply(state);state.value=C.deser(C.ser(state.value));apply(state);assert(#applied==1)''')

    def test_vehicle_watch_timeout_counts_game_steps_not_script_calls(self):
        self.check('''state.value.watches={{uid="watch",watch={steps=0,before={},kind="TRANSPORT_VEHICLE",key="v"}}}
            for i=1,20 do apply(state)end
            assert(#state.value.watches==1 and state.value.watches[1].watch.steps==1)
            for i=1,9 do now=now+200;apply(state)end
            assert(#state.value.watches==0 and state.value.results[1].success==false)''')

    def test_paused_assignment_waits_for_purchase_callback(self):
        self.check('''G.pendingPaused={{fn="makeVehicleBuyCmd",at=100,uid="1"},{fn="makeVehicleSetLineCmd",at=100,uid="2"}}
            paused();paused();assert(#applied==1 and #G.pendingPaused==1)
            callbacks[1]();paused();assert(#applied==2 and #G.pendingPaused==0)''')

    def test_line_creation_also_blocks_dependent_actions(self):
        self.check('''G.pendingPaused={{fn="makeLineCreateCmd",at=100,uid="1"},{fn="makeLineUpdateCmd",at=100,uid="2"}}
            paused();assert(#applied==1);callbacks[1]();paused();assert(#applied==2)''')

    def test_synchronous_callback_does_not_leave_a_phantom_barrier(self):
        self.check('''sync=true;G.pendingPaused={{fn="makeVehicleBuyCmd",at=100,uid="1"},{fn="makeVehicleSetLineCmd",at=100,uid="2"}}
            paused();assert(#applied==2 and G.pausedCreating==nil)''')

    def test_missing_callback_requests_recovery_once_without_releasing_dependents(self):
        self.check('''G.pendingPaused={{fn="makeVehicleBuyCmd",at=100,uid="1"},{fn="makeVehicleSetLineCmd",at=100,uid="2"}}
            paused();G.frames=602;paused();paused()
            assert(#applied==1 and #G.pendingPaused==1 and #sent==1 and sent[1].kind=="replay_failed")''')

    def test_late_engine_answer_records_result_and_releases_dependents(self):
        # The old code ignored an answer arriving after the timeout and kept the
        # dependents (and this game's clock, hence every game) held for good.
        self.check('''G.pendingPaused={{fn="makeVehicleBuyCmd",at=100,uid="1"},{fn="makeVehicleSetLineCmd",at=100,uid="2"}}
            paused();G.frames=602;paused();assert(#applied==1 and #sent==1)
            callbacks[1]();assert(G.pausedCreating==nil)
            paused();assert(#applied==2 and #G.pendingPaused==0)
            callbacks[1]();assert(#applied==2)''')

    def test_missing_answer_repeats_recovery_then_releases_hold(self):
        self.check('''G.pendingPaused={{fn="makeVehicleBuyCmd",at=100,uid="1"},{fn="makeVehicleSetLineCmd",at=100,uid="2"}}
            paused();G.frames=602;paused();assert(#sent==1)
            G.frames=602+1800;paused();assert(#sent==2 and #applied==1)
            G.frames=602+3600;paused();assert(#sent==3 and #applied==1)
            G.frames=602+5399;paused();assert(#applied==1 and G.pausedCreating~=nil)
            G.frames=602+5400;paused();assert(G.pausedCreating==nil and #applied==2 and #G.pendingPaused==0)
            for _,k in ipairs(sent) do assert(k.kind=="replay_failed") end''')

    def test_alone_in_session_missing_answer_does_not_freeze_game(self):
        self.check('''G.peers={};G.pendingPaused={{fn="makeLineCreateCmd",at=100,uid="1"},{fn="makeLineUpdateCmd",at=100,uid="2"}}
            paused();G.frames=602;paused();assert(#sent==1 and #applied==1)
            paused();assert(G.pausedCreating==nil and #applied==2)''')

    def test_unrelated_callback_does_not_clear_new_creation_barrier(self):
        self.check('''G.pendingPaused={{fn="makeVehicleBuyCmd",at=100,uid="1"}}
            paused();local old=callbacks[1];G.pausedCreating=nil
            G.pendingPaused={{fn="makeLineCreateCmd",at=100,uid="2"}};paused()
            local current=G.pausedCreating;old();assert(G.pausedCreating==current)''')

    def test_following_native_edit_waits_for_previous_callback_and_quiet_period(self):
        self.check('''G.nativeQueue={{fn="makeWorldBuildProposalCmd",at=100,uid="1"},{fn="makeWorldBuildProposalCmd",at=100,uid="2"}}
            replays();assert(#applied==1 and #G.nativeQueue==1)
            G.replayOut=0;G.replayQuiet=4;G.frames=2;replays();assert(#applied==1)
            G.frames=4;replays();assert(#applied==2 and #G.nativeQueue==0)''')

    def test_native_edit_gate_keeps_explicit_legacy_owner_assignment_path(self):
        self.check('''G.frames=200;G.replayOut=1;G.replaySince=200
            G.nativeQueue={{fn="mpfCoAssign",at=100,uid="1",recvFrame=0}}
            replays();assert(G.coBusy and G.coBusy.a.fn=="mpfCoAssign" and #G.nativeQueue==0)''')


if __name__ == '__main__':
    unittest.main()
