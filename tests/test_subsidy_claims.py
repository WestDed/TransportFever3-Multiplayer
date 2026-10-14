"""Real CO.installContracts with injected game state, not an engine test."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class SubsidyClaimTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().CO = self.lua.execute((CONTENT / 'mpfever_co.lua').read_text(encoding='utf-8'))
        self.lua.execute('''
            reg={mode="separate",list={{id=1,ent=10},{id=2,ent=20}}}
            st={proposedSubventions={{uid=101,data={}}},activeSubventions={},
                completedSubventions={},failedSubventions={}}
            state={get=function() return st end,set=function(_,value) st=value end}
            calls=0; paid=0; move=true
            original=function(_,_,_,_,name,param)
                calls=calls+1
                if name=="onAccept" then
                    paid=paid+100
                    if move then
                        local offer=st.proposedSubventions[1]
                        if offer then table.remove(st.proposedSubventions,1); st.activeSubventions[1]=offer end
                    end
                end
                return "engine-result"
            end
            api={res={genericRep={getAllOfType=function() return {} end}}}
            ug_require=function() error("notification module not part of this fixture") end
            game={["::/game_mechanics/subventions/subventions.script"]={handleEvent=original}}
            assert(CO.installContracts({registry=function() return reg end,localCo=function() return 1 end}))
            accept=function(co,uid)
                return game["::/game_mechanics/subventions/subventions.script"].handleEvent(
                    nil,state,"source","Subvention","onAccept",{mpfCo=co,uid=uid})
            end
        ''')

    def check(self, code):
        self.lua.execute(code)

    def test_first_accept_preserves_game_result_and_owner(self):
        self.check('assert(accept(2,101)=="engine-result"); assert(calls==1 and paid==100 and st.activeSubventions[1].mpfOwner==2)')

    def test_other_company_cannot_take_accepted_offer(self):
        self.check('accept(1,101); accept(2,101); assert(calls==1 and paid==100 and st.activeSubventions[1].mpfOwner==1)')

    def test_same_company_retry_cannot_pay_twice(self):
        self.check('accept(2,101); accept(2,101); assert(calls==1 and paid==100)')

    def test_retained_offer_cannot_be_claimed_again(self):
        self.check('move=false; accept(1,101); accept(2,101); assert(calls==1 and st.proposedSubventions[1].mpfOwner==1)')

    def test_missing_offer_cannot_issue_reward(self):
        self.check('accept(2,999); assert(calls==0 and paid==0)')

    def test_completed_or_failed_offer_cannot_be_reaccepted(self):
        self.check('for _,list in ipairs({"completedSubventions","failedSubventions"}) do st[list]={{uid=101,mpfOwner=1}}; accept(2,101); assert(calls==0); st[list]={} end')

    def test_unknown_or_pending_company_cannot_accept(self):
        self.check('accept(3,101); reg.list[2].ent=nil; accept(2,101); assert(calls==0 and st.proposedSubventions[1].mpfOwner==nil)')

    def test_single_company_also_rejects_duplicate_claim(self):
        self.check('reg.list[2]=nil; move=false; accept(1,101); accept(1,101); assert(calls==1 and paid==100)')

    def test_saved_claim_survives_module_restart(self):
        self.check('move=false; accept(1,101)')
        common = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.lua.globals().st = common.deser(common.ser(self.lua.globals().st))
        self.lua.globals().CO = self.lua.execute((CONTENT / 'mpfever_co.lua').read_text(encoding='utf-8'))
        self.check('game["::/game_mechanics/subventions/subventions.script"]={handleEvent=original}; CO.installContracts({registry=function() return reg end,localCo=function() return 1 end}); accept(2,101); assert(calls==1 and paid==100)')

    def test_shared_mode_uses_original_game_handler(self):
        self.check('reg.mode="shared"; accept(1,101); accept(2,101); assert(calls==2)')


if __name__ == '__main__':
    unittest.main(verbosity=2)
