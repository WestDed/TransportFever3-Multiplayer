"""Actual registry and bridge helpers with injected engine dependencies; no TF3."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class PersistentOwnershipTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().CO = self.lua.execute((CONTENT / 'mpfever_co.lua').read_text(encoding='utf-8'))
        self.lua.globals().C = self.lua.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.lua.execute('host=string.rep("a",64); owner=string.rep("b",64); impostor=string.rep("c",64); world=string.rep("d",32); reg=CO.new("separate",7,2000000); CO.join(reg,"host","Host",true,nil,nil,host,world)')

    def test_save_restart_rename_preserves_company_and_financial_state(self):
        self.lua.execute('local c,n=CO.join(reg,"old","Old",false,"Railway",nil,owner); assert(n); c.ent=900; c.balance=12345; c.vehicles={41,42}; reg.loan={[c.id]={obtainedLoans={{amount=5000}}}}; reg.shares={station=100}; reg=C.deser(C.ser(reg)); local restored,n=CO.join(reg,"renamed","New",false,"New company",nil,owner); assert(not n and restored.id==2 and restored.ent==900 and restored.name=="Railway" and restored.balance==12345 and restored.vehicles[2]==42 and reg.loan[2].obtainedLoans[1].amount==5000 and reg.shares.station==100 and #reg.list==2 and reg.world==world)')

    def test_same_name_and_declared_steam_id_cannot_reclaim_owner(self):
        self.lua.execute('local a=CO.join(reg,"same","Same",false,nil,"123",owner); local b,n=CO.join(reg,"same","Same",false,nil,"123",impostor); assert(n and a.id~=b.id and CO.match(reg,"same","123",false,impostor)==b and CO.match(reg,"same","123",false,owner)==a)')

    def test_missing_key_does_not_use_name_fallback(self):
        self.lua.execute('CO.join(reg,"same","Same",false,nil,"123",owner); assert(CO.match(reg,"same","123",false)==nil); local ok=pcall(CO.join,reg,"same","Same",false,nil,"123"); assert(not ok and #reg.list==2)')

    def test_legacy_company_needs_explicit_host_assignment(self):
        self.lua.execute('reg.list[2]={id=2,key="old",sid="123",name="Railway",color=2,ent=900,balance=456}; local ok=pcall(CO.join,reg,"old","Old",false,nil,"123",owner); assert(not ok and #reg.list==2); CO.assignOwner(reg,2,owner,world,host); reg=C.deser(C.ser(reg)); local c,n=CO.join(reg,"new","New",false,nil,nil,owner); assert(c.id==2 and c.ent==900 and c.balance==456 and not n)')

    def test_wrong_world_or_approver_and_overwrite_are_refused(self):
        self.lua.execute('reg.list[2]={id=2,key="old",color=2}; assert(not pcall(CO.assignOwner,reg,2,owner,string.rep("e",32),host)); assert(not pcall(CO.assignOwner,reg,2,owner,world,impostor)); assert(reg.list[2].owner==nil); CO.assignOwner(reg,2,owner,world,host); assert(not pcall(CO.assignOwner,reg,2,impostor,world,host) and reg.list[2].owner==owner)')

    def test_assignment_is_idempotent_and_cannot_grant_two_companies(self):
        self.lua.execute('reg.list[2]={id=2,key="old",color=2}; reg.list[3]={id=3,key="another",color=3}; CO.assignOwner(reg,2,owner,world,host); CO.assignOwner(reg,2,owner,world,host); assert(not pcall(CO.assignOwner,reg,3,owner,world,host) and not pcall(CO.assignOwner,reg,1,impostor,world,host) and #reg.list==3)')

    def test_assignment_without_confirmed_host_or_world_is_refused(self):
        for setup in (
            'reg=CO.new("separate",7,2000000); world=nil; host=nil',
            'reg.world=nil; world=nil',
            'reg.list[1].owner=nil; host=nil',
            'reg.world="invalid"; world="invalid"',
            'reg.list[1].owner="invalid"; host="invalid"',
        ):
            with self.subTest(setup=setup):
                self.setUp()
                self.lua.execute(setup)
                self.lua.execute('reg.list[2]={id=2,key="legacy",ent=900}; assert(not pcall(CO.assignOwner,reg,2,owner,world,host) and reg.list[2].owner==nil and reg.list[2].ent==900)')

    def test_changed_host_profile_is_not_silently_accepted(self):
        self.lua.execute('assert(not pcall(CO.join,reg,"host","Host",true,nil,nil,impostor,world) and reg.list[1].owner==host)')

    def test_host_private_key_cannot_create_a_second_client_company(self):
        self.lua.execute('assert(not pcall(CO.join,reg,"client","Client",false,nil,nil,host) and #reg.list==1)')

    def test_owner_and_world_are_part_of_registry_signature(self):
        self.lua.execute('local before=CO.signature(reg); reg.list[1].owner=impostor; assert(CO.signature(reg)~=before); reg.list[1].owner=host; reg.world=string.rep("e",32); assert(CO.signature(reg)~=before)')

    def test_duplicate_owner_does_not_pick_random_company(self):
        self.lua.execute('CO.join(reg,"one","One",false,nil,nil,owner); reg.list[3]={id=3,key="two",owner=owner}; assert(not pcall(CO.match,reg,"one",nil,false,owner))')

    def test_actual_bridge_restoration_does_not_create_or_fund_again(self):
        # Load the actual pumpCompanies body; the engine allocation callback is a
        # sentinel, not a fake claim that PLAYER/JOURNAL were exercised in TF3.
        source = (CONTENT / 'mpfever_bridge.script.lua').read_text(encoding='utf-8')
        start = source.index('local function pumpCompanies()')
        end = source.index('\n-- Trees, plants and rocks', start)
        body = source[start:end].replace('local function pumpCompanies()', 'function pumpCompanies()', 1)
        self.lua.execute('local c=CO.join(reg,"old","Old",false,nil,nil,owner); c.ent=900; reg=C.deser(C.ser(reg)); G={frames=1,co=reg,coBusy={frame=0,phase="apply",a={fn="mpfCoJoin",args={key="new",host=false,owner=owner}}}}; function toSim() end; function coCreate() error("must not allocate or credit restored company") end; function log() end')
        self.lua.execute(body)
        self.lua.execute('pumpCompanies(); assert(G.coBusy==nil and #reg.list==2)')


if __name__ == '__main__':
    unittest.main(verbosity=2)
