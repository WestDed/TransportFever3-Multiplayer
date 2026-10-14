"""Execute the production UI installer with injected module and station facts."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class StationSelectionTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        text = (CONTENT / 'mpfever_ui.script.lua').read_text(encoding='utf-8')
        start = text.index('local function installShareSelect()')
        end = text.index('local function sendShare(', start)
        self.lua.execute('''
            clock=5; os.clock=function() return clock end
            available=true; fees={[100]=0}; logs={}
            eu={isOwnedByPlayerOrNotOwned=function(e) return e==10 end}
            ug_require=function() if not available then error("not loaded yet") end return eu end
            log=function(s) logs[#logs+1]=s end
            api={engine={getComponent=function() return nil end},type={ComponentType={BASE_EDGE="edge"}}}
            U={coMap={[10]=1,[20]=2},coTxt="first",myCo=2,shareVersion=0}
        ''')
        self.lua.execute('''makeInstall=function(U)
            local sharedGroupOf=function(e) if e==100 then return 100 end end
            local foreignFee=function(sg) return fees[sg] end
        ''' + text[start:end] + '\nreturn installShareSelect end')
        self.lua.execute('install=makeInstall(U)')

    def check(self, code):
        self.lua.execute(code)

    def test_own_station_and_private_foreign_station(self):
        self.check('install(); assert(eu.isOwnedByPlayerOrNotOwned(10)); assert(not eu.isOwnedByPlayerOrNotOwned(100)); assert(not eu.isOwnedByPlayerOrNotOwned(999))')

    def test_paid_foreign_station_is_selectable(self):
        self.check('fees[100]=500; install(); assert(eu.isOwnedByPlayerOrNotOwned(100))')

    def test_sharing_change_invalidates_cached_denial_immediately(self):
        self.check('install(); assert(not eu.isOwnedByPlayerOrNotOwned(100)); fees[100]=500; U.shareVersion=1; assert(eu.isOwnedByPlayerOrNotOwned(100))')

    def test_revocation_invalidates_cached_allow_immediately(self):
        self.check('fees[100]=500; install(); assert(eu.isOwnedByPlayerOrNotOwned(100)); fees[100]=0; U.shareVersion=1; assert(not eu.isOwnedByPlayerOrNotOwned(100))')

    def test_missing_module_can_be_installed_later(self):
        self.check('available=false; install(); assert(not U.shareSelectInstalled); available=true; install(); fees[100]=500; assert(U.shareSelectInstalled and eu.isOwnedByPlayerOrNotOwned(100))')

    def test_reload_binds_new_ui_context(self):
        self.check('fees[100]=500; install(); assert(eu.isOwnedByPlayerOrNotOwned(100)); local nextUI={myCo=3}; makeInstall(nextUI)(); assert(nextUI.shareSelectInstalled and not eu.isOwnedByPlayerOrNotOwned(100)); assert(eu.isOwnedByPlayerOrNotOwned(10))')


class StationOwnerTests(unittest.TestCase):
    def test_ui_resolves_owner_through_station_construction(self):
        lua = LuaRuntime(unpack_returned_tuples=True)
        lua.execute('''
            world={[100]={STATION_GROUP={stations={101}}},[101]={STATION={}},[200]={PLAYER_OWNED={player=20}}}
            api={type={ComponentType=setmetatable({},{__index=function(_,k) return k end})},engine={}}
            api.engine.entityExists=function(e) return world[e]~=nil end
            api.engine.getComponent=function(e,k) return world[e] and world[e][k] end
            api.engine.system={streetConnectorSystem={
                getConstructionEntityForSubconstruction=function() return -1 end,
                getConstructionEntityForStation=function(e) return e==101 and 200 or -1 end}}
        ''')
        lua.globals().R = lua.execute((CONTENT / 'mpfever_refs.lua').read_text(encoding='utf-8'))
        source = (CONTENT / 'mpfever_ui.script.lua').read_text(encoding='utf-8')
        start = source.index('local function ownerOf(e)')
        end = source.index('local function companyName(', start)
        lua.globals().uiOwner = lua.execute(source[start:end] + '\nreturn ownerOf')
        lua.execute('assert(R.ownerOf(100)==20); assert(uiOwner(100)==20); assert(uiOwner(-1)==nil and uiOwner(999)==nil)')


if __name__ == '__main__':
    unittest.main(verbosity=2)
