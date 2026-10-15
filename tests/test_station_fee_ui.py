"""Run the production station-window recipe with React/engine fixtures, not TF3."""
from pathlib import Path
import unittest

from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class StationFeeUiTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().C = self.lua.execute(
            (CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.lua.execute('''
            clock=5;os.clock=function()return clock end
            C.T=function(_,en)return en end;T=C.T
            U={shares={["sg:A"]=100},shareVersion=1}
            sent={};logs={};log=function(s)logs[#logs+1]=s end
            api={util={formatMoney=tostring},cmd={
                makeScriptingSendEventCmd=function(...)return {...}end,
                sendCommand=function(cmd)sent[#sent+1]=cmd end}}
            mew={makeEntityWindowContent=function()return nil end}
            ug_require=function()return mew end
            recipes={};states={};cursor=0
            react={RegisterRecipe=function(name,fn)recipes[name]=fn;return name end,
                onStepTimer=function(fn)timer=fn end,
                useState=function(default)
                    cursor=cursor+1;local index=cursor
                    if not states[index]then states[index]={value=default}end
                    return {old=function()return states[index].value end,
                        set=function(_,v)states[index].nextValue=v;states[index].changed=true end}
                end}
            builtin={type={Orientation={Horizontal=1,Vertical=2}}}
            setmetatable(builtin,{__index=function(_,k)return function(p)p.__kind=k;return p end end})
            props={ownerText="Host",shareKey="sg:A",shareEdit=true}
            function render()
                for _,s in ipairs(states)do
                    if s.changed then s.value=s.nextValue;s.nextValue=nil;s.changed=false end
                end
                cursor=0;root=recipes.MPFeverOwnerLine(props)
            end
            function nodes(kind)
                local out={};local function walk(v)
                    if type(v)~="table"then return end
                    if v.__kind==kind then out[#out+1]=v end
                    if v.content then walk(v.content)end
                    for _,c in ipairs(v.children or {})do walk(c)end
                end
                walk(root);return out
            end
            function field()return assert(nodes("TextInputField")[1])end
            function apply()
                for _,b in ipairs(nodes("Button"))do
                    if b.content.text=="Apply"then b.onClick();return end
                end
                error("Apply button missing")
            end
            function typeFee(value)field().onTyping(value);render()end
            function hasText(value)
                for _,v in ipairs(nodes("TextView"))do if v.text==value then return true end end
                return false
            end
            function invalidHint()
                for _,v in ipairs(nodes("TextView"))do
                    if v.text=="Whole amount from 0 to 1000000000; 0 is free."then
                        return v.meta.class:find("error",1,true)~=nil
                    end
                end
                error("fee hint missing")
            end
            function assertSent(index,key,fee)
                local cmd=assert(sent[index]);assert(cmd[1]=="mpfever" and cmd[2]=="mpfever" and cmd[3]=="share")
                assert(cmd[4].key==key and cmd[4].fee==fee)
                assert(cmd[4].mpfCo==nil and cmd[4].mpfRequest==nil)
            end
        ''')
        source = (CONTENT / 'mpfever_ui.script.lua').read_text(encoding='utf-8')
        helpers = source[source.index('local SHARE_FEES ='):source.index('local function sharedGroupOf(')]
        recipe = source[source.index('local function sendShare('):source.index('-- ---------------------------------------------------------------- the session:')]
        self.lua.execute(helpers + '\n' + recipe + '\ninstallOwnerLine(react,builtin);render()')

    def check(self, code):
        self.lua.execute(code)

    def test_custom_amount_button_uses_existing_event_and_pending_confirmation(self):
        self.check('''typeFee("1234");assert(#sent==0);apply();render()
            assertSent(1,"sg:A",1234);assert(field().value=="1234")
            assert(nodes("ComboBox")[1].value==1234 and hasText("Applying for every player…"))
            U.shares["sg:A"]=1234;U.shareVersion=2;timer();render()
            assert(not hasText("Applying for every player…") and field().value=="1234")''')

    def test_enter_commits_supplied_text_without_waiting_for_typing_render(self):
        self.check('field().onValueChange("4321");assertSent(1,"sg:A",4321)')

    def test_zero_means_free_and_private_preset_remains_separate(self):
        self.check('''typeFee("0");apply();render();assertSent(1,"sg:A",-1)
            assert(field().value=="0" and nodes("ComboBox")[1].value==-1)
            nodes("ComboBox")[1].onValueChange(0);render();assertSent(2,"sg:A",0)
            assert(field().value=="" and nodes("ComboBox")[1].value==0)''')

    def test_invalid_input_stays_local_and_preserves_existing_fee(self):
        for value in ('', ' ', '-1', '-100', '+100', '1.5', '1e3', '0x10', 'nan',
                      'inf', '1 250', '1000000001', '9' * 33, '１２３', '12x'):
            with self.subTest(value=value):
                self.lua.globals().bad = value
                self.check('''typeFee(bad);apply();render();assert(#sent==0 and invalidHint())
                    assert(field().value==bad and nodes("ComboBox")[1].value==100)''')

    def test_upper_bound_and_surrounding_spaces_are_accepted(self):
        self.check('typeFee(" 1000000000 ");apply();assertSent(1,"sg:A",1000000000)')

    def test_correction_clears_error_and_preset_clears_draft(self):
        self.check('''typeFee("bad");apply();render();assert(invalidHint())
            typeFee("321");assert(not invalidHint());apply();render();assertSent(1,"sg:A",321)
            typeFee("999");nodes("ComboBox")[1].onValueChange("250");render()
            assertSent(2,"sg:A",250);assert(field().value=="250")''')

    def test_applying_unchanged_amount_does_not_send_duplicate(self):
        self.check('typeFee("00100");apply();render();assert(#sent==0 and field().value=="100")')

    def test_station_switch_does_not_reuse_other_stations_draft_or_pending_fee(self):
        self.check('''typeFee("345");apply();render();typeFee("999")
            props.shareKey="sg:B";U.shares["sg:B"]=800;render()
            assert(field().value=="800" and nodes("ComboBox")[1].value==800)
            assert(not hasText("Applying for every player…"));typeFee("456");apply()
            assertSent(2,"sg:B",456)''')

    def test_foreign_station_has_readonly_fee(self):
        self.check('''props.shareEdit=false;U.shares["sg:A"]=1234;render()
            assert(#nodes("TextInputField")==0 and #nodes("Button")==0 and #nodes("ComboBox")==0)
            assert(hasText("Shared: 1234 per stop of your vehicles"))''')

    def test_unconfigured_station_uses_real_default_instead_of_showing_private(self):
        self.check('''U.shares={};render();assert(field().value==tostring(C.SHARE_DEFAULT))
            assert(nodes("ComboBox")[1].value==100)''')

    def test_external_updates_refresh_fee_without_overwriting_typed_draft(self):
        self.check('''U.shares["sg:A"]=777;U.shareVersion=2;timer();render();assert(field().value=="777")
            typeFee("456");U.shares["sg:A"]=888;U.shareVersion=3;timer();render()
            assert(field().value=="456" and nodes("ComboBox")[1].value==888)
            apply();assertSent(1,"sg:A",456)''')

    def test_pending_timeout_returns_to_confirmed_value(self):
        self.check('''typeFee("222");apply();render();clock=16;timer();render()
            assert(field().value=="100" and not hasText("Applying for every player…"))''')

    def test_reopened_window_displays_saved_custom_fee_and_includes_it_in_presets(self):
        self.check('''states={};U.shares["sg:A"]=1234;render();assert(field().value=="1234")
            local found=false;for _,item in ipairs(nodes("ComboBox")[1].items)do
                if item.value==1234 then found=true end
            end;assert(found)''')


if __name__ == '__main__':
    unittest.main()
