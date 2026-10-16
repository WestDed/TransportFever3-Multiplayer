"""Real UI adapters; React, clocks and engine components are explicit fixtures."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


def function(text, name):
    i = text.index('local function ' + name + '(')
    return text[i:text.index('\nend\n', i) + 5]


class SpeedUiTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.lua.execute('''
            clock=5;wall=100;os.clock=function()return clock end;os.time=function()return wall end
            C.FACTORIES={"makeGameSetSpeedCmd","makeScriptingSendEventCmd"};original={};C.origCmds=function()return original end
            U={tags={},seq=0,logged=0,stats={speed=0},status={started="1",t="100",speed="4"}}
            R={};sent={};native=0;log=function()end;IMMEDIATE={}
            N={record=function()error("fixture preferences write failure")end}
            sendLine=function(k,p)sent[#sent+1]={kind=k,p=p}end
            speed={speedup=0,other="kept"}
            api={type={ComponentType={GAME_SPEED="speed"}},engine={util={getWorld=function()return 1 end},
                getComponent=function(_,ct)if ct=="speed"then return speed else return {untouched=true}end end},
                cmd={makeGameSetSpeedCmd=function()return {}end,makeScriptingSendEventCmd=function()return {}end,
                sendCommand=function()native=native+1 end}}
        ''')
        ui = (CONTENT / 'mpfever_ui.script.lua').read_text(encoding='utf-8')
        start = ui.index('U.shownSpeed = function()')
        self.lua.execute(ui[start:ui.index('\nend\n', start) + 5])
        self.lua.execute(function(ui, 'isCallable') + '\n' + function(ui, 'install') + '\ninstall();reinstall=install')
        bridge = (CONTENT / 'mpfever_bridge.script.lua').read_text(encoding='utf-8')
        self.lua.execute(function(bridge, 'gameSpeed') + '\nrealSpeed=gameSpeed')

    def check(self, code):
        self.lua.execute(code)

    def test_button_request_gets_callback_without_setting_native_simulation_speed(self):
        self.check('''local answered=0;local cmd=api.cmd.makeGameSetSpeedCmd(2)
            api.cmd.sendCommand(cmd,function(_,ok)assert(ok);answered=answered+1 end)
            assert(answered==1 and #sent==1 and sent[1].p.speed==2 and native==0)''')

    def test_repeated_request_is_throttled_but_every_callback_is_answered(self):
        self.check('''local answered=0
            for i=1,4 do api.cmd.sendCommand(api.cmd.makeGameSetSpeedCmd(2),function()answered=answered+1 end)end
            assert(answered==4 and #sent==1)''')

    def test_shared_api_shows_session_speed_but_bridge_still_reads_real_zero(self):
        self.check('''local c=api.engine.getComponent(1,"speed")
            assert(c.speedup==4 and c.mpfRealSpeedup==0 and c.other=="kept" and speed.speedup==0)
            assert(realSpeed()==0 and api.engine.getComponent(1,"other").untouched)''')

    def test_pending_speed_expires_and_stale_launcher_falls_back_to_engine(self):
        self.check('''api.cmd.sendCommand(api.cmd.makeGameSetSpeedCmd(2))
            assert(U.shownSpeed()==2);clock=8;assert(U.shownSpeed()==4)
            wall=107;assert(U.shownSpeed()==nil and api.engine.getComponent(1,"speed")==speed)''')

    def test_ui_reload_does_not_treat_previous_display_speed_as_engine_speed(self):
        self.check('''U.installed=false;U.status.speed="2";reinstall()
            local c=api.engine.getComponent(1,"speed")
            assert(c.speedup==2 and c.mpfRealSpeedup==0 and realSpeed()==0)''')

    def test_failed_preference_record_does_not_block_original_notification_command(self):
        self.check('''api.cmd.sendCommand(api.cmd.makeScriptingSendEventCmd("","Notifications","dismiss",{id=1}))
            assert(native==1 and #sent==0)''')

    def test_base_rank_button_uses_bounded_personal_operation(self):
        self.check('''U.coMap={[7]=1};U.stats.held=0;U.logged=20;R.translateOut=function()end
            local cmd=api.cmd.makeScriptingSendEventCmd("","Company","applyLevel",{level=2})
            api.cmd.sendCommand(cmd);local a=sent[1].p.args
            assert(sent[1].kind=="act_req" and a[2]=="mpfever" and a[3]=="progression" and a[4].level==2 and a[4].mpfCo==nil)''')

    def test_shared_company_rank_button_keeps_original_event(self):
        self.check('''U.stats.held=0;U.logged=20;R.translateOut=function()end
            api.cmd.sendCommand(api.cmd.makeScriptingSendEventCmd("","Company","applyLevel",{level=2}))
            local a=sent[1].p.args;assert(a[2]=="Company" and a[3]=="applyLevel")''')


class NoticeWindowTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.execute('''
            _react={recipeReplace={},originalRecipeFn={[1]=function(p)return p end}}
            state={phase="idle"};readState=function()return state end;log=function()end
            react={onStepTimer=function(fn)timer=fn end};closed=0
        ''')
        source = (CONTENT / 'mpfever_menu.lua').read_text(encoding='utf-8')
        start = source.index('-- The release notes window')
        end = source.index('-- the main page\'s recipe id', start)
        timer = source.index('\t\t-- MPFever starts something from here')
        timer_end = source.index('\t\t-- inside the menu\'s box layout', timer)
        self.lua.execute(source[start:end] + '\n' + source[timer:timer_end] + '\ncatchNotice(1,"ReleaseNotesWindow")')

    def check(self, code):
        self.lua.execute(code)

    def test_session_start_closes_notice_once_and_preserves_original_parameters(self):
        self.check('''local p={onClose=function()closed=closed+1 end,title="kept"};local q=_react.recipeReplace[1](p)
            assert(q~=p and q.title=="kept" and q.onClose~=p.onClose)
            timer();assert(closed==0);state.phase="loading";timer();timer();assert(closed==1)''')

    def test_manually_closed_notice_is_not_closed_again(self):
        self.check('''local q=_react.recipeReplace[1]({onClose=function()closed=closed+1 end});q.onClose()
            state.phase="joining";timer();assert(closed==1)''')

    def test_missing_or_failed_close_does_not_block_menu(self):
        self.check('''local p={title="no callback"};assert(_react.recipeReplace[1](p)==p)
            _react.recipeReplace[1]({onClose=function()error("fixture failure")end})
            state.phase="loading";timer();timer()''')


if __name__ == '__main__':
    unittest.main()
