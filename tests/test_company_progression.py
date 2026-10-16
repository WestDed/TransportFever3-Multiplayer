"""Real saved progression/permit Lua; game APIs below are fixtures, not TF3."""
from pathlib import Path
import tempfile
import unittest
from lupa.lua52 import LuaRuntime

CONTENT = Path(__file__).resolve().parents[1] / 'mod/mpfever_1/content'


class CompanyProgression(unittest.TestCase):
    def setUp(self):
        self.L = LuaRuntime(unpack_returned_tuples=True)
        self.L.globals().P = self.L.execute((CONTENT / 'mpfever_progression.lua').read_text(encoding='utf-8'))
        self.L.globals().C = self.L.execute((CONTENT / 'mpfever_common.lua').read_text(encoding='utf-8'))
        self.L.execute('''reg={mode="separate",list={{id=1,ent=7},{id=2,ent=8}}};
            config={basePopulation=100,ranks={{xp=0},{xp=110,ticket=0.9},{xp=130,ticket=0.8}}};
            seed={experience=105,level=1,potentialLevel=1};P.ensure(reg,config,{[1]=seed,[2]=seed})''')

    def check(self, code):
        self.L.execute(code)

    def test_own_transport_does_not_give_xp_to_other_company(self):
        self.check('assert(P.credit(reg,2,"trip",1));assert(P.view(reg,2).experience==106 and P.view(reg,1).experience==105)')

    def test_migration_copies_and_never_reseeds_saved_progress(self):
        self.check('seed.experience=999;P.credit(reg,2,"trip",1);P.ensure(reg,config,{[1]=seed,[2]=seed});assert(P.view(reg,1).experience==105 and P.view(reg,2).experience==106)')

    def test_new_company_does_not_inherit_current_host_rank(self):
        self.check('reg.list[3]={id=3,ent=9};P.ensure(reg,config,{[3]={level=3,experience=900}});assert(P.view(reg,3).level==1 and P.view(reg,3).experience==100)')

    def test_duplicate_delivery_is_ignored_after_serialization(self):
        self.check('P.credit(reg,2,"trip",1);reg=C.deser(C.ser(reg));assert(not P.credit(reg,2,"trip",1));assert(P.view(reg,2).experience==106)')

    def test_rank_needs_own_experience_and_explicit_confirmation(self):
        self.check('assert(not P.apply(reg,2,2));for i=1,5 do P.credit(reg,2,"trip"..i,i)end;assert(P.view(reg,2).potentialLevel==2 and P.view(reg,2).level==1);assert(P.apply(reg,2,2));assert(P.view(reg,2).ticketPriceMultiplier==0.9 and P.view(reg,1).level==1)')

    def test_duplicate_rank_application_and_downgrade(self):
        self.check('for i=1,25 do P.credit(reg,2,"trip"..i,i)end;local ok,changed=P.apply(reg,2,2);assert(ok and changed);ok,changed=P.apply(reg,2,2);assert(ok and not changed);assert(not P.apply(reg,2,1));assert(not P.apply(reg,2,4))')

    def test_invalid_or_foreign_network_data_cannot_set_xp(self):
        self.check('for _,v in ipairs({0,-1,1.5,999,"2"})do assert(not P.apply(reg,2,v))end;assert(not P.credit(reg,99,"trip",1));assert(not P.credit(reg,2,"",1));assert(not P.credit(reg,2,"trip",0/0))')

    def test_views_cannot_mutate_saved_state(self):
        self.check('local v=P.view(reg,2);v.experience=999;assert(P.view(reg,2).experience==105)')

    def test_local_entity_ids_are_absent_from_shared_digest(self):
        self.check('P.credit(reg,2,"local-entity-400",1);local a=C.ser(P.sharedState(reg));reg.list[2].ent=999;reg.progression.seen={};assert(C.ser(P.sharedState(reg))==a)')

    def test_future_saved_schema_is_not_silently_reset(self):
        self.check('reg.progression.version=999;assert(not pcall(P.ensure,reg,config,{}));assert(reg.progression.version==999)')

    def test_dedup_is_bounded(self):
        self.check('for i=1,P.MAX_SEEN+5 do P.credit(reg,2,"trip"..i,i)end;local n=0;for _ in pairs(reg.progression.seen.map)do n=n+1 end;assert(n<=P.MAX_SEEN)')

    def test_repeated_oversized_batch_cannot_cycle_the_dedup_ring(self):
        self.check('for pass=1,2 do for i=1,P.MAX_SEEN+5 do P.credit(reg,2,"trip"..i,1)end end;assert(P.view(reg,2).deliveries==P.MAX_SEEN)')

    def test_fixed_rank_setting_is_kept_for_new_companies(self):
        self.check('reg.progression=nil;config.enforcedLevel=3;P.ensure(reg,config,{});assert(P.view(reg,2).level==3 and P.view(reg,2).experience==100);P.credit(reg,2,"trip",1);assert(P.view(reg,2).potentialLevel==3 and not P.apply(reg,2,2))')

    def test_empty_world_uses_game_thresholds_without_inventing_population(self):
        self.check('reg.progression=nil;config.basePopulation=0;P.ensure(reg,config,{});assert(P.view(reg,2).experience==0);P.credit(reg,2,"trip",1);assert(P.view(reg,2).experience==1 and P.view(reg,1).experience==0)')


class ProgressionReader(unittest.TestCase):
    def setUp(self):
        self.L=LuaRuntime(unpack_returned_tuples=True)
        self.L.globals().CO=self.L.execute((CONTENT/'mpfever_co.lua').read_text(encoding='utf-8'))
        self.L.execute('native={level=9};M={getCompanyProgressionState=function(e)return e==7 and native or nil end};own={[7]={level=2},[8]={level=1}};read=function(e)return own[e],own[e]~=nil end;req=function()return M end')

    def test_reader_uses_own_progress_instead_of_canonical_entity(self):
        self.L.execute('assert(CO.patchProgression(req,read));assert(M.getCompanyProgressionState(7).level==2 and M.getCompanyProgressionState(8).level==1)')

    def test_reload_updates_provider_without_nested_wrappers(self):
        self.L.execute('CO.patchProgression(req,read);CO.patchProgression(req,function(e)return {level=3},true end);assert(M.getCompanyProgressionState(8).level==3)')

    def test_missing_managed_record_does_not_leak_host_progress(self):
        self.L.execute('CO.patchProgression(req,function(e)return nil,true,7 end);assert(M.getCompanyProgressionState(8)==nil)')

    def test_explicit_legacy_fallback_and_unmanaged_entities(self):
        self.L.execute('CO.patchProgression(req,function(e)return nil,false,e==8 and 7 or nil end);assert(M.getCompanyProgressionState(8)==native and M.getCompanyProgressionState(99)==nil)')


class ProgressionEngine(unittest.TestCase):
    def setUp(self):
        self.L=LuaRuntime(unpack_returned_tuples=True)
        for key,file in [('P','mpfever_progression.lua'),('CO','mpfever_co.lua'),('C','mpfever_common.lua')]:
            self.L.globals()[key]=self.L.execute((CONTENT/file).read_text(encoding='utf-8'))
        self.L.execute('''
            logs={};originalUpdates=0;originalEvents=0;nativeProgress={[7]={experience=105,level=1,potentialLevel=1}}
            reg={mode="separate",canon=7,list={{id=1,ent=7},{id=2,ent=8}}}
            state={value={co=reg},get=function(s)return s.value end,set=function(s,v)s.value=v end}
            nativeState={value={companyState={}},get=state.get,set=state.set}
            growth={update=function()originalUpdates=originalUpdates+1 end,handleEvent=function()originalEvents=originalEvents+1;return "original"end}
            game={["::/game_mechanics/company/company_growth.script"]=growth}
            util={getCompanyProgressionState=function(e)return nativeProgress[e]end,
                getXpNeededForRank=function(_,i)return ({0,110,130})[i]end,getMaxRank=function()return 3 end,
                getTicketPriceMultiplierForLevel=function(i)return ({1,0.9,0.8})[i]end}
            cu={getBasePopulation=function()return 100 end}
            ug_require=function(n)if n:find("company_progression_util",1,true)then return util elseif n:find("company_util",1,true)then return cu end;error("unavailable")end
            world={[10]={PLAYER_OWNED={player=7}},[20]={PLAYER_OWNED={player=8}},[30]={PLAYER_OWNED={player=8}},
                [40]={SIM_CARGO={startTime=0,pickupTime=1},SIM_ENTITY_AT_VEHICLE={lineStop1=0,roundTripCounter=1}}}
            api={type={ComponentType=setmetatable({},{__index=function(_,k)return k end})},engine={getComponent=function(e,k)return world[e] and world[e][k]end}}
            C.marshal=function(x)return x end
            E=P.engine;E.configure(C,{},CO,function(s)logs[#logs+1]=s end)
            event={{vehicleEntity=30,lineEntity=20,simEntity=40,basePrice=10,distance=100}}
        ''')

    def check(self,s):self.L.execute(s)

    def test_missing_game_modules_leave_original_shared_behavior(self):
        self.check('game={};E.update(state);assert(not reg.progression);growth.update();assert(originalUpdates==1 and #logs==1)')

    def test_not_initialized_native_progression_waits_without_reset(self):
        self.check('nativeProgress={};E.update(state);assert(not reg.progression);growth.update();assert(originalUpdates==1)')

    def test_unrecognized_native_state_does_not_reset_existing_progress(self):
        self.check('nativeProgress[7]={unknownLevelField=9};E.update(state);assert(not reg.progression)')

    def test_game_fixed_rank_config_is_read_without_overriding_it(self):
        self.check('api.res={genericRep={find=function()return 12 end,get=function()return {data={enforcedLevel=3}}end}};E.update(state);assert(P.view(reg,2).level==3 and reg.progression.enforcedLevel==3)')

    def test_old_world_population_updater_is_disabled_only_in_separate_mode(self):
        self.check('E.update(state);growth.update({},nativeState,200);assert(originalUpdates==0 and nativeState.value.companyState[8].experience==105);reg.mode="shared";growth.update();assert(originalUpdates==1)')

    def test_paid_engine_leg_credits_vehicle_and_line_owner_only(self):
        self.check('E.update(state);E.engineEvent(state,"TransportVehicleSystem","OnCalcTicketPrice",event,10);assert(P.view(reg,2).experience==106 and P.view(reg,1).experience==105)')

    def test_forged_engine_event_and_zero_price_do_not_give_xp(self):
        self.check('E.update(state);E.engineEvent(state,"mpfever","OnCalcTicketPrice",event,10);event[1].basePrice=0;E.engineEvent(state,"TransportVehicleSystem","OnCalcTicketPrice",event,10);assert(P.view(reg,2).experience==105)')

    def test_vehicle_owner_must_agree_with_line_owner(self):
        self.check('E.update(state);world[30].PLAYER_OWNED.player=7;E.engineEvent(state,"TransportVehicleSystem","OnCalcTicketPrice",event,10);assert(P.view(reg,2).experience==105)')

    def test_person_leg_and_resync_do_not_repeat_recent_credit(self):
        self.check('E.update(state);world[40].SIM_PERSON=world[40].SIM_CARGO;world[40].SIM_CARGO=nil;E.engineEvent(state,"SimEntityAtVehicleSystem","OnCalcTicketPrice",event,10);state.value=C.deser(C.ser(state.value));reg=state.value.co;E.update(state);E.engineEvent(state,"SimEntityAtVehicleSystem","OnCalcTicketPrice",event,10);assert(P.view(reg,2).experience==106)')

    def test_base_debug_and_unrouted_rank_events_cannot_modify_host(self):
        self.check('E.update(state);assert(growth.handleEvent(nil,nil,"","Company","applyLevel",{level=3})==nil);growth.handleEvent(nil,nil,"","","_debugAddExperience",{experience=999});assert(originalEvents==0);assert(growth.handleEvent(nil,nil,"","","handleLegacy",{})=="original")')

    def test_applied_rank_and_ticket_bonus_are_personal(self):
        self.check('E.update(state);for i=1,5 do P.credit(reg,2,"trip"..i,i)end;E.command(state,{mpfCo=2,level=2});local p={{lineEntity=10},{lineEntity=20}};local result=growth.handleEvent(nil,nil,"","TransportVehicleSystem","OnCalcTicketPrice",p);assert(result[1]==1 and result[2]==0.9 and result[0]==nil);assert(P.view(reg,1).level==1)')

    def test_unearned_and_duplicate_rank_commands(self):
        self.check('E.update(state);E.command(state,{mpfCo=2,level=3});assert(P.view(reg,2).level==1);for i=1,5 do P.credit(reg,2,"trip"..i,i)end;E.command(state,{mpfCo=2,level=2});E.command(state,{mpfCo=2,level=2});assert(P.view(reg,2).level==2 and P.view(reg,1).level==1)')

    def test_shared_game_price_handler_is_preserved(self):
        self.check('E.update(state);reg.mode="shared";assert(growth.handleEvent(nil,nil,"","TransportVehicleSystem","OnCalcTicketPrice",event)=="original")')

    def test_install_retry_does_not_stack_handlers(self):
        self.check('E.update(state);local wrapper=growth.handleEvent;E.update(state);assert(growth.handleEvent==wrapper);assert(util.getCompanyProgressionState(8).experience==105)')

    def test_native_save_metadata_reads_current_personal_rank(self):
        self.check('E.update(state);for i=1,5 do P.credit(reg,2,"t"..i,i)end;P.apply(reg,2,2);growth.handleEvent({},nativeState,"","","storeSavegameMetadata",{});assert(nativeState.value.companyState[7].level==1 and nativeState.value.companyState[8].level==2)')

    def test_native_hash_normalizes_player_entities_without_hiding_values(self):
        self.check('E.update(state);growth.update({},nativeState,200);local a=C.ser(P.nativeDigest(nativeState.value,reg));reg.list[2].ent=999;nativeState.value.companyState[999]=nativeState.value.companyState[8];nativeState.value.companyState[8]=nil;assert(C.ser(P.nativeDigest(nativeState.value,reg))==a);nativeState.value.companyState[999].experience=500;assert(C.ser(P.nativeDigest(nativeState.value,reg))~=a)')


class CompanyPermits(unittest.TestCase):
    def setUp(self):
        self.L=LuaRuntime(unpack_returned_tuples=True)
        self.L.globals().CO=self.L.execute((CONTENT/'mpfever_co.lua').read_text(encoding='utf-8'))
        self.L.execute('''actor=8;fail=false;counts=0
            world={[10]={player=7},[20]={player=8}}
            constructions={{10,{fileName="hq.con",persistentMetadata={permitKey="hq"}},1},
                {20,{fileName="colosseum.con",persistentMetadata={permitKey="landmark"}},2},
                {30,{fileName="monument.con",persistentMetadata={permitKey="world"}},3}}
            cu={getConstructionDisableCacheData=function()return {companyRank=2,extraPermits={hq=1},data={shared={numBuilt=9}}}end,
                countUsedConstructionPermits=function()return {original=99}end,getActualPermitKey=function(_,m)return m.key end}
            cm={getKey=function()return "company"end,constructionInstance={get=function(x)return x end}}
            req=function(n)return n=="company_util.tl" and cu or cm end
            api={type={ComponentType={PLAYER_OWNED=1}},res={constructionRep={get=function(i)return {metadata={key=({"hq","landmark","world"})[i]}}end}},
                engine={getComponent=function(e)return world[e]end,util={getPlayer=function()return actor end},
                system={streetConnectorSystem={forEachConstructionWithMetadata=function(_,_,_,cb)
                    for _,x in ipairs(constructions)do cb(x[1],x[2],x[3]);if fail then error("fixture census failure")end end
                end}}}}
            assert(CO.patchPermits(req))
        ''')

    def check(self,s):self.L.execute(s)

    def test_each_company_has_own_hq_and_landmark_count(self):
        self.check('local a=cu.getConstructionDisableCacheData({});assert(a.data.hq==nil and a.data.landmark.numBuilt==1 and a.companyRank==2 and a.extraPermits.hq==1);actor=7;local b=cu.countUsedConstructionPermits();assert(b.hq==1 and b.landmark==nil)')

    def test_unowned_world_buildings_remain_common(self):
        self.check('local a=cu.countUsedConstructionPermits();assert(a.world==1);actor=7;assert(cu.countUsedConstructionPermits().world==1)')

    def test_failed_census_does_not_grant_unlimited_permits(self):
        self.check('fail=true;local a=cu.getConstructionDisableCacheData({});assert(a.data.shared.numBuilt==9);local b=cu.countUsedConstructionPermits();assert(b.original==99)')

    def test_owner_change_and_bulldoze_refresh_counts(self):
        self.check('world[10].player=8;assert(cu.countUsedConstructionPermits().hq==1);table.remove(constructions,1);assert(cu.countUsedConstructionPermits().hq==nil)')


class ProgressionMirror(unittest.TestCase):
    def setUp(self):
        self.L=LuaRuntime(unpack_returned_tuples=True)
        self.L.globals().C=self.L.execute((CONTENT/'mpfever_common.lua').read_text(encoding='utf-8'))
        self.L.execute('''U={coWorld="world-A"};C.DIR="fixture";C.BS="/";txt="{}";C.IO={open=function()return {read=function(_,n)limit=n;return txt end,close=function()end}end}''')
        source=(CONTENT/'mpfever_ui.script.lua').read_text(encoding='utf-8')
        start=source.index('local function pollProgression(')
        self.L.execute(source[start:source.index('\nend\n',start)+5]+'\npoll=pollProgression')

    def test_mirror_is_bounded_and_refuses_another_save(self):
        self.L.execute('txt=C.ser({version=1,world="world-B",companies={}});poll();assert(not U.progression and limit==1048577);txt=string.rep("x",1048577);poll();assert(not U.progression)')

    def test_own_world_updates_once_and_invalid_data_is_not_executed(self):
        self.L.execute('txt=C.ser({version=1,world="world-A",companies={["2"]={level=2}}});poll();poll();assert(U.progression.companies["2"].level==2 and U.businessVersion==1);txt="(function() error(123) end)()";poll();assert(U.businessVersion==1)')


class ProgressionBridge(unittest.TestCase):
    def setUp(self):
        from test_lockstep import Game
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.game=Game('RankHost','host',self.tmp.name,50000)
        self.L=self.game.L
        self.L.execute('''
            reg={mode="separate",canon=7,list={{id=1,ent=7},{id=2,ent=8}}};STATE_DATA={co=reg}
            local old=ug_require
            pm={getCompanyProgressionState=function(e)if e==7 then return {level=1,experience=105}end end,
                getXpNeededForRank=function(_,i)return ({0,110,130})[i]end,getMaxRank=function()return 3 end,
                getTicketPriceMultiplierForLevel=function(i)if i~=1 then return 0.9 end end}
            ug_require=function(n)if n:find("company_progression_util",1,true)then return pm elseif n:find("company_util",1,true)then return {getBasePopulation=function()return 100 end}end;return old(n)end
            game={["::/game_mechanics/company/company_growth.script"]={update=function()end,handleEvent=function()end}}
            api.util={getDefaultYearDuration=function()return 1200 end,getDefaultMonthDuration=function()return 100 end}
            P=ug_require("mpfever_progression.lua");P.engine.update(STATE)
            function requestRank(actor,level,forged)
                SIM_EVENT("queue",{fn="makeScriptingSendEventCmd",co=actor,args={n=4,"","mpfever","progression",{level=level,mpfCo=forged}},at=GAME.time+200,uid="rank:"..level,origin="RankHost",oseq=level})
                GAME.stepBudget=1;SIMSTEP()
            end
        ''')

    def test_real_bridge_command_injects_actor_and_does_not_promote_host(self):
        self.L.execute('for i=1,5 do P.credit(reg,2,"t"..i,i)end;requestRank(2,2,1);assert(P.view(STATE_DATA.co,2).level==2 and P.view(STATE_DATA.co,1).level==1)')

    def test_real_bridge_command_refuses_an_unearned_rank(self):
        self.L.execute('requestRank(2,3);assert(P.view(STATE_DATA.co,2).level==1)')


if __name__ == '__main__':
    unittest.main()
