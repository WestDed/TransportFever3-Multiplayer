"""Saved business core and engine/GUI adapters with injected API facts, not TF3."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT=Path(__file__).resolve().parents[1]/'mod/mpfever_1/content'

class BusinessTests(unittest.TestCase):
    def setUp(self):
        self.L=LuaRuntime(unpack_returned_tuples=True)
        self.L.globals().B=self.L.execute((CONTENT/'mpfever_business.lua').read_text())
        self.L.execute('''
            reg={mode="separate",list={{id=1,ent=7},{id=2,ent=8},{id=3,ent=9}}}
            funds={[1]=10000,[2]=1000,[3]=1000}
            balance=function(id)return funds[id]end
            g={p0={x=0,y=0,z=0},p1={x=100,y=0,z=0},t0={x=100,y=0,z=0},t1={x=100,y=0,z=0}}
            key=B.roadKey(g)
            function policy(mode,fee,p)
                p=p or {};p.key=key;p.kind="road";p.mode=mode;p.fee=fee or 0
                assert(B.setPolicy(reg,1,p,1,g))
            end
            function settle()
                for i=1,100 do local t,index,leg=B.nextLeg(reg);if not t then return end
                    leg.status="submitted";funds[leg.co]=funds[leg.co]+leg.amount;assert(B.ack(reg,t.id,index,true))
                end;error("unbounded transaction queue")
            end
            function order(amount,duration)
                local ok,id=B.createOrder(reg,1,{title="Delivery",target="sg:A",cargo=1,amount=amount or 2,reward=500,duration=duration or 12,monthDuration=100},0,balance)
                assert(ok);settle();return id
            end
        ''')
    def check(self,s):self.L.execute(s)
    def test_independent_use_connection_and_edit_rights(self):
        self.check('policy("private",0,{company=2,use=true,connectGrant=true,edit=false});assert(B.can(reg,key,2,"use") and B.can(reg,key,2,"connect") and not B.can(reg,key,2,"edit"));assert(not B.can(reg,key,3,"use"));assert(B.can(reg,key,1,"edit"))')
    def test_only_confirmed_owner_can_change_policy(self):
        self.check('policy("free");assert(not B.setPolicy(reg,2,{kind="road",key=key,mode="toll",fee=10},2,g));assert(reg.business.roads[key].owner==1)')
    def test_use_grant_does_not_grant_build_or_delete(self):
        self.check('policy("free");assert(B.can(reg,key,2,"use") and not B.can(reg,key,2,"connect") and not B.can(reg,key,2,"edit"))')
    def test_free_toll_private_station_modes_match_selection(self):
        self.check('for _,m in ipairs({"private","free","toll"})do assert(B.setPolicy(reg,1,{kind="station",key="sg:A",mode=m,fee=10},1));assert(reg.shares["sg:A"]==(m=="private" and 0 or m=="free" and -1 or 10))end')
    def test_road_entry_payment_once_and_reentry_again(self):
        self.check('policy("toll",100);assert(not B.visit(reg,2,"V:bus",key,balance));settle();assert(B.visit(reg,2,"V:bus",key,balance));assert(B.visit(reg,2,"V:bus",key,balance));assert(funds[2]==900 and funds[1]==10100);assert(B.visit(reg,2,"V:bus",nil,balance));assert(not B.visit(reg,2,"V:bus",key,balance));settle();assert(funds[2]==800 and funds[1]==10200)')
    def test_owner_and_free_use_are_not_charged(self):
        self.check('policy("toll",100);assert(B.visit(reg,1,"V:own",key,balance));policy("free");assert(B.visit(reg,2,"V:bus",key,balance));assert(B.nextLeg(reg)==nil)')
    def test_unaffordable_toll_does_not_issue_or_repeat_debit(self):
        self.check('policy("toll",2000);assert(not B.visit(reg,2,"V:bus",key,balance));assert(B.nextLeg(reg)==nil and funds[2]==1000);funds[2]=3000;assert(not B.visit(reg,2,"V:bus",key,balance));settle();assert(funds[2]==1000);assert(B.visit(reg,2,"V:bus",key,balance))')
    def test_pending_debits_reserve_spendable_funds(self):
        self.check('policy("toll",700);assert(not B.visit(reg,2,"V:one",key,balance));assert(not B.visit(reg,2,"V:two",key,balance));settle();assert(funds[2]==300 and funds[1]==10700);assert(reg.business.visits["V:two"].unpaid)')
    def test_failed_credit_never_repeats_successful_debit(self):
        self.check('policy("toll",100);B.visit(reg,2,"V:bus",key,balance);local t,i,l=B.nextLeg(reg);l.status="submitted";funds[2]=900;assert(B.ack(reg,t.id,i,true));local t,i,l=B.nextLeg(reg);assert(l.co==1);l.status="submitted";B.ack(reg,t.id,i,false);settle();assert(funds[2]==900 and funds[1]==10100)')
    def test_duplicate_callbacks_cannot_complete_unsubmitted_leg(self):
        self.check('policy("toll",100);B.visit(reg,2,"V:bus",key,balance);local t,i,l=B.nextLeg(reg);assert(not B.ack(reg,t.id,i,true));l.status="submitted";assert(B.ack(reg,t.id,i,true));assert(not B.ack(reg,t.id,i,true));assert(t.legs[2].status=="new")')
    def test_uncertain_bookings_after_reload_are_not_replayed(self):
        self.check('policy("toll",100);B.visit(reg,2,"V:bus",key,balance);local t,i,l=B.nextLeg(reg);l.status="submitted";B.recover(reg);assert(l.status=="uncertain" and B.nextLeg(reg)==nil);assert(not B.ack(reg,t.id,i,true))')
    def test_funding_must_finish_before_order_can_be_accepted(self):
        self.check('local ok,id=B.createOrder(reg,1,{title="A",target="sg:A",cargo=1,amount=1,reward=500,duration=1,monthDuration=100},0,balance);assert(ok and not B.acceptOrder(reg,2,id,1));settle();assert(B.acceptOrder(reg,2,id,1) and funds[1]==9500)')
    def test_one_carrier_only_even_for_simultaneous_claims(self):
        self.check('local id=order();assert(not B.acceptOrder(reg,1,id,1));assert(B.acceptOrder(reg,2,id,1));assert(not B.acceptOrder(reg,3,id,1))')
    def test_deliveries_match_carrier_cargo_destination_and_unique_event(self):
        self.check('local id=order();B.acceptOrder(reg,2,id,1);local d={token="unit:1",cargo=1,target="sg:A",amount=1};assert(not B.delivery(reg,3,d,2));d.token="unit:2";d.cargo=2;assert(not B.delivery(reg,2,d,2));d.token="unit:3";d.cargo=1;d.target="sg:B";assert(not B.delivery(reg,2,d,2));d.token="unit:4";d.target="sg:A";assert(B.delivery(reg,2,d,2));assert(not B.delivery(reg,2,d,2));assert(reg.business.orders[id].progress==1)')
    def test_reward_is_from_escrow_once_and_conserves_funds(self):
        self.check('local id=order(1);B.acceptOrder(reg,2,id,1);assert(B.delivery(reg,2,{token="cargo:1",cargo=1,target="sg:A",amount=1},2));settle();assert(reg.business.orders[id].status=="completed" and funds[1]==9500 and funds[2]==1500);B.delivery(reg,2,{token="cargo:2",cargo=1,target="sg:A",amount=1},3);settle();assert(funds[1]+funds[2]+funds[3]==12000)')
    def test_cancel_refunds_issuer_once(self):
        self.check('local id=order();assert(B.cancelOrder(reg,1,id));settle();assert(funds[1]==10000 and reg.business.orders[id].status=="cancelled");assert(not B.cancelOrder(reg,1,id));settle();assert(funds[1]==10000)')
    def test_issuer_cannot_cancel_active_carrier_and_carrier_can_abandon(self):
        self.check('local id=order();B.acceptOrder(reg,2,id,1);assert(not B.cancelOrder(reg,1,id));assert(B.cancelOrder(reg,2,id));assert(reg.business.orders[id].status=="open" and B.acceptOrder(reg,3,id,2))')
    def test_expiry_refunds_and_does_not_reward_late_delivery(self):
        self.check('local id=order(1,1);B.acceptOrder(reg,2,id,1);B.expire(reg,100);assert(not B.delivery(reg,2,{token="late",cargo=1,target="sg:A",amount=1},101));settle();B.expire(reg,200);settle();assert(reg.business.orders[id].status=="expired" and funds[1]==10000 and funds[2]==1000)')
    def test_one_delivery_never_counts_towards_two_orders(self):
        self.check('local a,b=order(1),order(1);B.acceptOrder(reg,2,a,1);B.acceptOrder(reg,2,b,1);B.delivery(reg,2,{token="one",cargo=1,target="sg:A",amount=1},2);settle();assert(reg.business.orders[a].progress+reg.business.orders[b].progress==1)')
    def test_command_retry_after_save_preserves_owner_and_deduplication(self):
        self.L.globals().C=self.L.execute((CONTENT/'mpfever_common.lua').read_text())
        self.check('local p={op="create",request="owner:seq",title="A",target="sg:A",cargo=1,amount=1,reward=500,duration=1};local ctx={now=0,monthDuration=100,balance=balance};local ok,id=B.command(reg,1,p,ctx);assert(ok);settle();reg=C.deser(C.ser(reg));local again,same=B.command(reg,1,p,ctx);assert(again and same==id and funds[1]==9500);assert(reg.business.orders[id].issuer==1)')
    def test_semantic_hash_ignores_local_entity_ids_but_detects_terms(self):
        self.L.globals().C=self.L.execute((CONTENT/'mpfever_common.lua').read_text())
        self.check('local id=order();local before=C.ser(B.sharedState(reg));reg.business.localSeen={localEntity=1};assert(before==C.ser(B.sharedState(reg)));reg.business.orders[id].reward=600;assert(before~=C.ser(B.sharedState(reg)))')
    def test_connection_split_keeps_owner_and_cannot_delete_middle(self):
        self.check('policy("free");local a={p0={x=0,y=0},p1={x=50,y=0},t0={x=50,y=0},t1={x=50,y=0}};local b={p0={x=50,y=0},p1={x=100,y=0},t0={x=50,y=0},t1={x=50,y=0}};assert(B.roadFor(reg,a).owner==1 and B.preserves(g,{a,b}));a.p1.x=20;a.t0.x=20;a.t1.x=20;b.p0.x=80;b.t0.x=20;b.t1.x=20;assert(not B.preserves(g,{a,b}))')
    def test_private_payments_not_exposed_to_other_companies(self):
        self.check('local id=order();local own=B.view(reg,1);local other=B.view(reg,2);assert(#own.payments==1 and #other.payments==0 and #other.orders==1);B.acceptOrder(reg,2,id,1);assert(#B.view(reg,3).orders==0)')
    def test_station_payments_conserve_money(self):
        self.check('assert(B.stationCharge(reg,2,1,"stop",100));settle();assert(funds[2]==900 and funds[1]==10100)')


if __name__=='__main__':unittest.main()
