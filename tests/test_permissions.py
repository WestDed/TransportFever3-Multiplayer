"""Production Lua rules with injected ownership facts; not an engine test."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class PermissionTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().P = self.lua.execute((CONTENT / 'mpfever_permissions.lua').read_text(encoding='utf-8'))
        self.lua.execute('reg={mode="separate",list={{id=1,ent=10},{id=2,ent=20}}}; owners={[100]=20,[200]=10}; owner=function(e) return owners[e] end')

    def test_own_fleet_and_lines(self):
        self.lua.execute('for fn in pairs(P.MUTATE) do assert(P.check(reg,{co=2,fn=fn,args={100,100}},owner)) end')

    def test_foreign_fleet_and_lines(self):
        self.lua.execute('for fn in pairs(P.MUTATE) do assert(not P.check(reg,{co=2,fn=fn,args={200,100}},owner)) end')

    def test_unknown_owner_and_lookup_failure_deny(self):
        self.lua.execute('assert(not P.check(reg,{co=2,fn="makeVehicleSellCmd",args={999}},owner)); assert(not P.check(reg,{co=2,fn="makeVehicleSellCmd",args={100}},function() error("engine unavailable") end))')

    def test_foreign_player_buy_and_create(self):
        self.lua.execute('assert(not P.check(reg,{co=2,fn="makeVehicleBuyCmd",args={10,100}},owner)); assert(not P.check(reg,{co=2,fn="makeLineCreateCmd",args={[3]=10}},owner))')

    def test_own_player_buy_and_create(self):
        self.lua.execute('assert(P.check(reg,{co=2,fn="makeVehicleBuyCmd",args={20,100}},owner)); assert(P.check(reg,{co=2,fn="makeLineCreateCmd",args={[3]=20}},owner))')

    def test_cannot_assign_to_foreign_line_or_depot(self):
        self.lua.execute('assert(not P.check(reg,{co=2,fn="makeVehicleSetLineCmd",args={100,200}},owner)); assert(not P.check(reg,{co=2,fn="makeVehicleSendToDepotCmd",args={100,200}},owner))')

    def test_shared_mode_unchanged(self):
        self.lua.execute('assert(P.check({mode="shared"},{fn="makeVehicleSellCmd",args={200}},owner))')

    def test_unconfirmed_company_has_no_authority(self):
        self.lua.execute('assert(not P.check(reg,{co=3,fn="makeVehicleSellCmd",args={100}},owner)); reg.list[2].ent=nil; assert(not P.check(reg,{co=2,fn="makeVehicleSellCmd",args={100}},owner))')


class LoanRepaymentTests(unittest.TestCase):
    def setUp(self):
        self.lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua.globals().CO = self.lua.execute((CONTENT / 'mpfever_co.lua').read_text(encoding='utf-8'))
        source = (CONTENT / 'mpfever_bridge.script.lua').read_text(encoding='utf-8')
        start = source.index('local function coLoanEvent(')
        end = source.index('-- every simulation step:', start)
        self.lua.execute('''
            booked={}; formulaInputs={};
            local M={lu={getFullLoanAndInterestPayBack=function(loan)
                formulaInputs[#formulaInputs+1]=loan; return loan.amount+loan.interest
            end, makeLoan=function(amount) return amount end}};
            loanMods=function() return M end;
            coLoanTable=function(st,co) return st.co.loan[co] end;
            coBook=function(ent,amount) booked[#booked+1]={ent=ent,amount=amount} end;
            log=function() end;
            st={co={list={{id=2,ent=20}},loan={[2]={obtainedLoans={{id=9,amount=1000,interest=100}}}}}}
        ''')
        self.lua.globals().repay = self.lua.execute(source[start:end] + '\nreturn coLoanEvent')

    def test_packet_terms_cannot_reduce_repayment(self):
        self.lua.execute('local stored=st.co.loan[2].obtainedLoans[1]; assert(repay(st,2,"Repay",{{},{id=9,amount=-99999,interest=0}})); assert(formulaInputs[1]==stored); assert(booked[1].ent==20 and booked[1].amount==-1100); assert(#st.co.loan[2].obtainedLoans==0)')

    def test_duplicate_repayment_does_not_charge_twice(self):
        self.lua.execute('assert(repay(st,2,"Repay",{{},{id=9}})); assert(not repay(st,2,"Repay",{{},{id=9}})); assert(#booked==1)')

    def test_unknown_loan_or_company_has_no_effect(self):
        self.lua.execute('assert(not repay(st,2,"Repay",{{},{id=99}})); assert(not repay(st,3,"Repay",{{},{id=9}})); assert(#booked==0 and #st.co.loan[2].obtainedLoans==1)')


if __name__ == '__main__':
    unittest.main(verbosity=2)
