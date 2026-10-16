"""Actual local notification persistence, real files; no native UI is executed."""
from pathlib import Path
import tempfile
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class NoticePreferencesTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.lua.globals().folder = self.tmp.name
        self.lua.execute('''
            C.DIR=folder;C.BS="/";C.IO=io;clock=0;os.clock=function()return clock end
            N={};sent={};logs={};log=function(s)logs[#logs+1]=s end
            entries={[1]={timestamp=100,notification={type="industry",params={name="A",entity={entity=50,revision={num={2,3}}}}}}}
            history={1};available=true
            util={externalGetNotificationsStateNative=function()return entries end,
                getNotificationEntryFromNative=function(_,id)return entries[id]end,
                getHistoryFromNative=function()return history end}
            ug_require=function()if not available then error("not loaded")end;return util end
            original={event=function(...)return {...}end,sendCommand=function(cmd)sent[#sent+1]=cmd end}
            C.origCmds=function()return original end
            function reload()
                N.keys=nil;N.order=nil;N.filter=nil;N.filterDone=nil;N.done={};N.at=nil
                N.load()
            end
        ''')
        source = (CONTENT / 'mpfever_ui.script.lua').read_text(encoding='utf-8')
        start = source.index('-- Dismissals belong to this interface')
        end = source.index('local function tick(pendingState)', start)
        self.lua.execute(source[start:end])

    def check(self, code):
        self.lua.execute(code)

    def test_dismissal_survives_reload_and_uses_new_local_notification_id(self):
        self.check('''N.record("dismiss",{id=1});assert(#sent==0)
            reload();entries[3]=entries[1];entries[1]=nil;history={3}
            N.tick();clock=2;N.tick();assert(#sent==1 and sent[1][2]=="Notifications" and sent[1][3]=="dismiss" and sent[1][4].id==3)''')

    def test_enlist_removes_saved_dismissal(self):
        self.check('N.record("dismiss",{id=1});N.record("enlist",{id=1});reload();N.tick();assert(#sent==0)')

    def test_repeated_one_shot_notice_outside_time_window_is_not_hidden(self):
        self.check('N.record("dismiss",{id=1});reload();entries[1].timestamp=121001;N.tick();assert(#sent==0)')

    def test_persistent_warning_matches_without_relying_on_timestamp(self):
        self.check('''entries[1].persisting={vehicle=50};N.record("dismiss",{id=1});reload()
            entries[1].timestamp=999999;N.tick();assert(#sent==1)''')

    def test_filter_restored_once_through_original_local_command(self):
        self.check('''N.record("updateIgnoredTypes",{industry=true});reload();N.tick();clock=2;N.tick()
            assert(#sent==1 and sent[1][3]=="updateIgnoredTypes" and sent[1][4].industry==true)''')

    def test_key_is_order_independent_and_distinguishes_key_types(self):
        self.check('''assert(N.canon({a=1,b=2})==N.canon({b=2,a=1}))
            assert(N.canon({[1]="a"})~=N.canon({["1"]="a"}))
            assert(N.canon({entity=50,revision={num={2,3}}})==N.canon({entity=50,revision={num={2,99}}}))''')

    def test_key_distinguishes_close_floating_values_and_limits_complex_content(self):
        self.check('''assert(N.canon(1.0000001)~=N.canon(1.0000002))
            local deep={};deep.self=deep
            assert(N.key({notification={type="test",params=deep}})==nil)''')

    def test_preference_count_is_bounded_and_recent_choices_survive(self):
        self.check('''N.MAX_KEYS=2
            for i=1,3 do entries[1].notification.params.name=tostring(i);N.record("dismiss",{id=1})end
            assert(#N.order==2);reload();assert(#N.order==2)
            assert(N.dismissedBefore(entries[1]))''')

    def test_malformed_file_is_data_only_and_fails_open_for_visibility(self):
        path = Path(self.tmp.name) / 'notif_preferences.lua'
        path.write_text('(function() error("executed") end)()', encoding='utf-8')
        self.check('load=function()error("compiled preferences")end;N.tick();assert(#sent==0 and next(N.keys)==nil)')

    def test_notification_module_can_appear_later(self):
        self.check('available=false;assert(N.native()==nil);available=true;clock=3;assert(N.native()==entries)')

    def test_oversized_preferences_are_not_loaded(self):
        (Path(self.tmp.name) / 'notif_preferences.lua').write_text('x' * 200, encoding='utf-8')
        self.check('N.FILE_BYTES=100;N.tick();assert(next(N.keys)==nil and #sent==0)')

    def test_write_failure_does_not_send_preferences_over_network_or_throw(self):
        self.check('N.load();C.IO={open=function()return nil end};N.record("dismiss",{id=1});assert(#sent==0 and #logs>0)')


if __name__ == '__main__':
    unittest.main()
