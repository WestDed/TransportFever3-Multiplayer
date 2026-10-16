"""Load/save dedup across launcher restarts; real Lua claims on temporary files."""
from pathlib import Path
import tempfile
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class ResyncControlTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.lua.globals().folder = self.tmp.name
        self.lua.execute('''
            C.DIR=folder;C.IO=io;BS="/";NL="\\n";C.BS=BS
            loads=0;saves=0;messages={};H={};log=function()end;gameTime=function()return 100 end
            api={type={SavegameId={new=function()return {}end}}}
            app={SaveGameNamespace={getSavegame=function()return "save"end},
                saveGame=function(_,cb)saves=saves+1;cb()end,
                findAllSavegames=function()return {{saveName="Host",path="fixture"}}end,
                loadGame=function()loads=loads+1 end}
            send=function(k,p)messages[#messages+1]={kind=k,p=p}end
            a={id=1,name="Host",request=string.rep("a",32)}
            b={id=1,name="Host",request=string.rep("b",32)}
        ''')
        text = (CONTENT / 'mpfever_bridge.script.lua').read_text(encoding='utf-8')
        start = text.index('local function claimResync(')
        end = text.index('-- Census (diagnostic', start)
        self.lua.execute(text[start:end])

    def check(self, code):
        self.lua.execute(code)

    def test_new_transfer_with_same_counter_loads_but_duplicate_does_not(self):
        self.check('H.resync_load("host",a);H.resync_load("host",a);H.resync_load("host",b);assert(loads==2)')

    def test_save_ack_echoes_the_exact_request_not_just_reused_counter(self):
        self.check('''H.resync_save("host",a);H.resync_save("host",a);H.resync_save("host",b)
            assert(saves==2 and messages[1].p.request==a.request and messages[2].p.request==b.request)''')

    def test_claims_distinguish_save_load_and_reject_injected_tokens(self):
        self.check('''H.resync_save("host",a);H.resync_load("host",a);assert(saves==1 and loads==1)
            a.request="bad\\nresync_load 3";H.resync_load("host",a);assert(loads==1)''')

    def test_ui_fallback_and_bridge_use_same_claim_key(self):
        self.check('''local key=C.controlKey("resync_load",a)
            C.appendFile(folder.."/resync_claims.txt",key);H.resync_load("host",a);assert(loads==0)''')

    def test_legacy_local_control_without_token_remains_idempotent(self):
        self.check('a.request=nil;H.resync_load("host",a);H.resync_load("host",a);assert(loads==1)')

    def test_real_menu_loader_accepts_new_token_with_same_save_and_keeps_duplicate_out(self):
        text = (CONTENT / 'mpfever_auto.lua').read_text(encoding='utf-8')
        code = text[text.index('local lastLoadSeq = nil'):text.index('-- the loaded game waits for a key press')]
        self.check('DIR=folder;IO=io;S={menuReady=true};loadSave=function()loads=loads+1 end')
        self.lua.execute(code + '\nloadMenu=menuLoad')
        self.check('''local function write(token)
                local f=assert(io.open(folder.."/menu_state.txt","wb"));f:write("load=Host\\nloadSeq=r"..token.."\\n");f:close()
            end
            write(a.request);loadMenu();loadMenu();assert(loads==1)
            write(b.request);loadMenu();assert(loads==2)''')
        # Reloading the application script must still obey its real marker file.
        self.lua.execute(code + '\nloadMenu=menuLoad;loadMenu();assert(loads==2)')


if __name__ == '__main__':
    unittest.main()
