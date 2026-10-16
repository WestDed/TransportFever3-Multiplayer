"""Execute actual engine and React adapters; all engine facts are fixtures."""
from pathlib import Path
import unittest
from lupa.lua52 import LuaRuntime

CONTENT=Path(__file__).resolve().parents[1]/'mod/mpfever_1/content'

class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.L=LuaRuntime(unpack_returned_tuples=True)
        self.L.globals().B=self.L.execute((CONTENT/'mpfever_business.lua').read_text())
        self.L.execute('''
            ug_require=function()return B end
            reg={mode="separate",list={{id=1,ent=7,name="Host"},{id=2,ent=8,name="Guest"}}}
            st={co=reg,bind={["V:bus"]=300,["L:route"]=200}};state={get=function()return st end,set=function(_,v)st=v end}
            funds={[7]=10000,[8]=1000};logs={};commands={};held={};defer=false
            world={
                [10]={BASE_NODE={position={x=0,y=0,z=0}},PLAYER_OWNED={player=7}},
                [11]={BASE_NODE={position={x=100,y=0,z=0}},PLAYER_OWNED={player=7}},
                [20]={BASE_EDGE={node0=10,node1=11,tangent0={x=100,y=0,z=0},tangent1={x=100,y=0,z=0}},BASE_EDGE_STREET={},PLAYER_OWNED={player=7}},
                [100]={STATION_GROUP={},PLAYER_OWNED={player=7}},
                [200]={LINE={stops={{stationGroup=100}}},PLAYER_OWNED={player=8}},
                [300]={TRANSPORT_VEHICLE={line=200,stopIndex=0,userStopped=false},PLAYER_OWNED={player=8},MOVE_PATH={path={edges={{{entity=20,index=0},true}}},dyn={pathPos={edgeIndex=0}}}},
                [400]={SIM_CARGO={cargoType=1,startTime=0,pickupTime=1},SIM_ENTITY_AT_VEHICLE={lineStop1=0,roundTripCounter=1}}
            }
            R={CO=reg,allEdges=function()return {20}end,entitiesWith=function(t)if t=="STATION_GROUP"then return {100}elseif t=="TRANSPORT_VEHICLE"then return {300}end;return {}end,
                rawOwnerOf=function(e)return world[e] and world[e].PLAYER_OWNED and world[e].PLAYER_OWNED.player end,sgKey=function(e)return "sg:"..e end}
            CO={idOfEntity=function(r,e)for _,c in ipairs(r.list)do if c.ent==e then return c.id end end end,
                entityOf=function(r,id)for _,c in ipairs(r.list)do if c.id==id then return c.ent end end end}
            local function cmd(k) return function(...)return {kind=k,args={...}}end end
            api={type={ComponentType=setmetatable({},{__index=function(_,k)return k end}),JournalEntry={new=function()return {category={}}end,Type={OTHER=1}},Vec3f={new=function(x,y,z)return {x=x,y=y,z=z}end}},
                engine={getComponent=function(e,k)return world[e] and world[e][k]end,util={finance={getPlayersBalance=function(e)return funds[e]end},getEntityName=function()return "Station A"end}},
                util={getDefaultYearDuration=function()return 1200 end,getDefaultMonthDuration=function()return 100 end},
                res={cargoTypeRep={get=function(id)return ({[0]={name="Passengers"},[1]={name="Goods"}})[id]end,getAll=function()return {[0]="passenger.cargo",[1]="goods.cargo"}end}}}
            api.cmd={makeJournalBookAssetCmd=cmd("book"),makeEntitySetPlayerCmd=cmd("owner"),makeVehicleSetStoppedByUserCmd=cmd("stop")}
            api.cmd.sendCommand=function(c,cb)
                commands[#commands+1]=c
                local function apply()
                    if c.kind=="book"then funds[c.args[1]]=funds[c.args[1]]+c.args[2].amount
                    elseif c.kind=="owner"then world[c.args[1]].PLAYER_OWNED={player=c.args[2]}
                    elseif c.kind=="stop"then world[c.args[1]].TRANSPORT_VEHICLE.userStopped=c.args[2]end
                    if cb then cb({},true)end
                end
                if defer then held[#held+1]=apply else apply()end
            end
            C={origCmds=function()return {}end,marshal=function(t)return t end,shareFee=function(s,k)return s and s[k] or 100 end}
            log=function(t)logs[#logs+1]=t end
        ''')
        self.L.globals().E=self.L.execute((CONTENT/'mpfever_business_engine.lua').read_text())
        self.L.execute('E.configure(C,R,CO,log);key=B.roadKey(E.geometry(20))')
    def check(self,s):self.L.execute(s)
    def test_api_policy_sets_public_graph_but_keeps_company_owner(self):
        self.check('E.command(state,{op="policy",kind="road",key=key,mode="toll",fee=100,connect=true,mpfCo=1,mpfRequest="one"},0);E.update(state,200,200);assert(world[20].PLAYER_OWNED.player==-1 and R.businessOwner(20)==7 and reg.business.roads[key].owner==1);assert(funds[8]==900 and funds[7]==10100)')
    def test_owner_cannot_be_forged_by_guest(self):
        self.check('E.command(state,{op="policy",kind="road",key=key,mode="free",fee=0,mpfCo=2,mpfRequest="one"},0);assert(not reg.business.roads[key] and world[20].PLAYER_OWNED.player==7)')
    def test_connect_only_keeps_full_original_curve(self):
        self.check('''E.command(state,{op="policy",kind="road",key=key,mode="free",fee=0,connect=true,mpfCo=1,mpfRequest="one"},0);E.update(state,200,200)
            local p={proposal={removedSegments={20},addedNodes={{entity=-1,comp={position={x=50,y=0,z=0}}}},addedSegments={
                {comp={node0=10,node1=-1,tangent0={x=50,y=0,z=0},tangent1={x=50,y=0,z=0}}},
                {comp={node0=-1,node1=11,tangent0={x=50,y=0,z=0},tangent1={x=50,y=0,z=0}}}}}}
            assert(E.checkBuild(reg,2,p));p.proposal.addedSegments={};assert(not E.checkBuild(reg,2,p))''')
    def test_connect_permission_cannot_upgrade_road_template(self):
        self.check('world[20].BASE_EDGE.roadTemplate=1;E.command(state,{op="policy",kind="road",key=key,mode="free",fee=0,connect=true,mpfCo=1,mpfRequest="one"},0);E.update(state,200,200);local p={proposal={removedSegments={20},addedSegments={{comp={node0=10,node1=11,tangent0={x=100,y=0,z=0},tangent1={x=100,y=0,z=0},roadTemplate=2}}}}};assert(not E.checkBuild(reg,2,p));p.proposal.addedSegments[1].comp.roadTemplate=1;assert(E.checkBuild(reg,2,p))')
    def test_connect_cannot_change_lane_modes_and_equal_maps_keep_working(self):
        self.check('world[20].BASE_EDGE.laneConfigs={{transportModes={[0]=true,[1]=false}}};E.command(state,{op="policy",kind="road",key=key,mode="free",fee=0,connect=true,mpfCo=1,mpfRequest="one"},0);E.update(state,200,200);local c={node0=10,node1=11,tangent0={x=100,y=0,z=0},tangent1={x=100,y=0,z=0},laneConfigs={{transportModes={[1]=false,[0]=true}}}};local p={proposal={removedSegments={20},addedSegments={{comp=c}}}};assert(E.checkBuild(reg,2,p));c.laneConfigs[1].transportModes[1]=true;assert(not E.checkBuild(reg,2,p))')
    def test_connect_does_not_allow_node_deletion_or_unauthorized_attachment(self):
        self.check('E.command(state,{op="policy",kind="road",key=key,mode="free",fee=0,mpfCo=1,mpfRequest="one"},0);E.update(state,200,200);assert(not E.checkBuild(reg,2,{proposal={addedSegments={{comp={node0=10,node1=11}}}}}));assert(not E.checkBuild(reg,2,{proposal={removedNodes={10}}}))')
    def test_unfunded_toll_stops_then_paid_vehicle_resumes(self):
        self.check('funds[8]=0;E.command(state,{op="policy",kind="road",key=key,mode="toll",fee=100,mpfCo=1,mpfRequest="one"},0);E.update(state,200,200);assert(world[300].TRANSPORT_VEHICLE.userStopped and funds[8]==0);funds[8]=200;E.update(state,200,400);E.update(state,200,600);assert(not world[300].TRANSPORT_VEHICLE.userStopped and funds[8]==100)')
    def test_manual_stopped_vehicle_is_not_resumed_by_mod(self):
        self.check('world[300].TRANSPORT_VEHICLE.userStopped=true;E.command(state,{op="policy",kind="road",key=key,mode="private",fee=0,mpfCo=1,mpfRequest="one"},0);E.update(state,200,200);E.command(state,{op="policy",kind="road",key=key,mode="free",fee=0,mpfCo=1,mpfRequest="two"},400);E.update(state,200,400);assert(world[300].TRANSPORT_VEHICLE.userStopped)')
    def test_full_payment_queue_preserves_road_maintenance_obligation(self):
        self.check('E.command(state,{op="policy",kind="road",key=key,mode="free",fee=0,connect=true,mpfCo=1,mpfRequest="one"},0);E.update(state,200,200);world[20].MAINTENANCE_COST={maintenanceCost=1200};B.MAX_TRANSACTIONS=1;B.stationCharge(reg,2,1,"busy",100);defer=true;E.update(state,200,400);assert(reg.business.maintenance[key]==200);defer=false;held[1]();E.update(state,200,600);assert(reg.business.maintenance[key]==0);E.update(state,200,800);assert(funds[7]==9500)')
    def test_vanished_order_target_returns_saved_error_without_escrow(self):
        self.check('E.command(state,{op="create",target="sg:missing",title="Goods",cargo=1,amount=1,reward=500,duration=1,mpfCo=1,mpfRequest="one"},0);assert(next(reg.business.orders)==nil and #reg.business.notices["1"]==1 and reg.business.notices["1"][1].code=="request_failed" and funds[7]==10000)')
    def test_journal_callback_is_required_before_order_opens(self):
        self.check('defer=true;E.command(state,{op="create",target="sg:100",title="Goods",cargo=1,amount=1,reward=500,duration=1,mpfCo=1,mpfRequest="one"},0);E.flush(state);local id=next(reg.business.orders);assert(reg.business.orders[id].status=="funding");held[1]();assert(reg.business.orders[id].status=="open" and funds[7]==9500)')
    def test_engine_delivery_drives_reward_and_duplicate_event_is_ignored(self):
        self.check('E.command(state,{op="create",target="sg:100",title="Goods",cargo=1,amount=1,reward=500,duration=1,mpfCo=1,mpfRequest="one"},0);E.flush(state);local id=next(reg.business.orders);E.command(state,{op="accept",id=id,mpfCo=2,mpfRequest="two"},1);local p={vehicleEntity=300,lineEntity=200,simEntity=400};E.engineEvent(state,"TransportVehicleSystem","OnCalcTicketPrice",p,2);E.engineEvent(state,"TransportVehicleSystem","OnCalcTicketPrice",p,2);assert(reg.business.orders[id].status=="completed" and funds[8]==1500 and funds[7]==9500)')
    def test_passenger_delivery_uses_actual_person_component(self):
        self.check('world[400].SIM_CARGO=nil;world[400].SIM_PERSON={cargoType=0,pickupTime=1};E.command(state,{op="create",target="sg:100",title="Passengers",cargo=0,amount=1,reward=500,duration=1,mpfCo=1,mpfRequest="one"},0);E.flush(state);local id=next(reg.business.orders);E.command(state,{op="accept",id=id,mpfCo=2,mpfRequest="two"},1);E.engineEvent(state,"SimEntityAtVehicleSystem","OnCalcTicketPrice",{vehicleEntity=300,lineEntity=200,simEntity=400},2);assert(reg.business.orders[id].status=="completed" and funds[8]==1500)')
    def test_network_event_cannot_masquerade_as_engine_delivery(self):
        self.check('E.command(state,{op="create",target="sg:100",title="Goods",cargo=1,amount=1,reward=500,duration=1,mpfCo=1,mpfRequest="one"},0);E.flush(state);local id=next(reg.business.orders);E.command(state,{op="accept",id=id,mpfCo=2,mpfRequest="two"},1);E.engineEvent(state,"mpfever","OnCalcTicketPrice",{vehicleEntity=300,lineEntity=200,simEntity=400},2);assert(reg.business.orders[id].progress==0)')
    def test_gui_view_exposes_actual_cargo_ids_including_passengers(self):
        self.check('local v=E.view(reg,1);assert(#v.cargos==2 and v.cargos[1].id==0 and v.cargos[2].id==1 and #v.targets==1)')
    def test_revoked_station_permission_stops_existing_route(self):
        self.check('E.command(state,{op="policy",kind="station",key="sg:100",mode="private",fee=0,mpfCo=1,mpfRequest="one"},0);E.update(state,200,200);assert(world[300].TRANSPORT_VEHICLE.userStopped);E.command(state,{op="policy",kind="station",key="sg:100",mode="free",fee=0,mpfCo=1,mpfRequest="two"},400);E.update(state,200,400);assert(not world[300].TRANSPORT_VEHICLE.userStopped)')

class PrivacyTests(unittest.TestCase):
    def setUp(self):
        self.L=LuaRuntime(unpack_returned_tuples=True)
        self.L.globals().P=self.L.execute((CONTENT/'mpfever_privacy.lua').read_text())
        self.L.execute('ctx={me=2,coMap={[7]=1,[8]=2},ownerOf=function(e)if e==100 then return 7 elseif e==200 then return 8 end end}; own={notification={params={entities={{200,0}}}}};foreign={notification={params={vehicles={{100,0}}}}};global={notification={params={entities={{999,0}}}}}')
    def check(self,s):self.L.execute(s)
    def test_foreign_hidden_own_and_shared_world_kept(self):
        self.check('assert(P.visible(own,ctx) and not P.visible(foreign,ctx) and P.visible(global,ctx));assert(not P.visible({params={playerEntity=7}},ctx));assert(P.visible({params={playerEntity=8}},ctx))')
    def test_projection_preserves_original_state_and_global_history(self):
        self.check('local s={notifications={[1]=own,[2]=foreign,[3]=global},history={1,2,3},version=8};local v=P.state(s,ctx);assert(#v.history==2 and v.notifications[2]==nil and v.version==8);assert(s.notifications[2]==foreign and #s.history==3)')
    def test_native_gui_history_markers_and_sound_prefilter(self):
        self.check('''local native={};local u={externalGetNotificationsState=function()return {notifications={[1]=own,[2]=foreign},history={1,2}}end,
            getNotificationEntryFromNative=function(_,id)return id==1 and own or foreign end,
            getHistoryFromNative=function()return {1,2}end,getPersistingEntity2NotificationFromNative=function()return {[100]={2},[200]={1}}end,
            preFilterOutExcessiveVehicleNotification=function()return false end}
            assert(P.install(function()return u end,function()return ctx end,function()end));assert(#u.getHistoryFromNative(native)==1);assert(u.getPersistingEntity2NotificationFromNative(native)[100]==nil);assert(u.preFilterOutExcessiveVehicleNotification(foreign) and not u.preFilterOutExcessiveVehicleNotification(own))''')
    def test_reload_rebinds_context_and_missing_module_can_retry(self):
        self.check('local u={externalGetNotificationsState=function()return {notifications={[1]=own,[2]=foreign},history={1,2}}end};assert(not P.install(function()error("missing")end,function()return ctx end,function()end));P.install(function()return u end,function()return ctx end,function()end);local nextCtx={me=1,coMap=ctx.coMap,ownerOf=ctx.ownerOf};P.install(function()return u end,function()return nextCtx end,function()end);assert(u.externalGetNotificationsState().notifications[2] and not u.externalGetNotificationsState().notifications[1])')


class BusinessUiTests(unittest.TestCase):
    def setUp(self):
        self.L=LuaRuntime(unpack_returned_tuples=True)
        self.L.globals().sources=self.L.table_from({p.name:p.read_text() for p in CONTENT.glob('*.lua')})
        self.L.execute('''
            local modules={};ug_require=function(n) n=n:gsub("^mpfever_1::/","");if not modules[n]then modules[n]=assert(load(sources[n]))()end;return modules[n]end
            C={T=function(_,en)return en end,shareFee=function(s,k)return s and s[k] or 100 end}
            U={myCo=1,coMap={[7]=1,[8]=2},shares={}}
            U.business={me=1,roads={},stations={},companies={{id=1,ent=7,name="Host"},{id=2,ent=8,name="Guest"}},roadCatalog={{key="road:A",owner=1,label="Road A"}},targets={{key="sg:A",owner=1,label="Station A"}},cargos={{id=0,name="Passengers"},{id=1,name="Goods"}},orders={},payments={},notices={}}
            R={};sent={};api={cmd={makeScriptingSendEventCmd=function(...)return {...}end,sendCommand=function(cmd)sent[#sent+1]=cmd end},engine={getComponent=function()return nil end},type={ComponentType={}}}
            recipes={};states={};cursor=0
            react={RegisterWrapperRecipe=function(name,base,fn)recipes[name]=fn;return name end,onStepTimer=function()end,
                useState=function(default)cursor=cursor+1;local index=cursor
                    if not states[index]then states[index]={value=default}end
                    return {old=function()return states[index].value end,set=function(_,v)states[index].pending=v end}
                end}
            builtin={type={Orientation={Horizontal=1,Vertical=2}}};setmetatable(builtin,{__index=function(_,k)return function(p)p.__kind=k;return p end end})
            windows={};wapi={addSingletonWindow=function(recipe,p)windows[#windows+1]=recipe end,removeAllWindows=function()end}
            V=ug_require("mpfever_business_ui.lua");V.configure(C,R,U,function()end);V.install(react,builtin,function()return wapi end)
            function render()
                for _,s in ipairs(states)do if s.pending~=nil then s.value=s.pending;s.pending=nil end end
                cursor=0;root=recipes.MPFeverBusinessWindow({})
            end
            function nodes(kind)
                local out={};local function walk(v)if type(v)~="table"then return end;if v.__kind==kind then out[#out+1]=v end
                    if v.content then walk(v.content)end;for _,c in ipairs(v.children or {})do walk(c)end
                end;walk(root);return out
            end
            function click(label)for _,b in ipairs(nodes("Button"))do if b.content.text==label then b.onClick();return end end;error("missing button "..label)end
        ''')
    def check(self,s):self.L.execute(s)
    def test_window_opens_and_road_policy_controls_send_stamped_event(self):
        self.check('U.openBusiness();assert(windows[1]=="MPFeverBusinessWindow");render();nodes("ComboBox")[1].onValueChange("road:road:A");render();nodes("ComboBox")[2].onValueChange("free");render();click("Connection for everyone: No");render();click("Save rules");local c=sent[1];assert(c[2]=="mpfever" and c[3]=="business" and c[4].key=="road:A" and c[4].kind=="road" and c[4].mode=="free" and c[4].connect)')
    def test_company_specific_grants_are_independent_controls(self):
        self.check('render();nodes("ComboBox")[1].onValueChange("road:road:A");render();nodes("ComboBox")[3].onValueChange(2);render();click("Use: No");render();click("Connect: No");render();click("Save rules");local p=sent[1][4];assert(p.company==2 and p.use and p.connectGrant and not p.edit and p.key=="road:A")')
    def test_create_order_inputs_send_destination_cargo_amount_reward_duration(self):
        self.check('render();click("Orders");render();nodes("TextInputField")[1].onValueChange("City goods");render();nodes("ComboBox")[1].onValueChange("sg:A");render();nodes("ComboBox")[2].onValueChange(1);render();click("Create and reserve reward");local p=sent[1][4];assert(p.op=="create" and p.title=="City goods" and p.target=="sg:A" and p.cargo==1 and p.amount==100 and p.reward==10000 and p.duration==12)')
    def test_blank_order_form_is_local_error_and_does_not_disconnect_peer(self):
        self.check('render();click("Orders");render();click("Create and reserve reward");render();assert(#sent==0 and U.businessMessage=="invalid")')
    def test_invalid_toll_is_not_sent_over_network(self):
        self.check('render();nodes("ComboBox")[1].onValueChange("road:road:A");render();nodes("ComboBox")[2].onValueChange("toll");render();nodes("TextInputField")[1].onValueChange("oops");render();click("Save rules");assert(#sent==0 and U.businessMessage=="invalid")')
    def test_order_accept_cancel_and_pagination(self):
        self.check('U.business.orders={{id="order1",issuer=2,status="open",title="A",target="sg:A",progress=0,amount=2,reward=500}};render();click("Orders");render();click("Accept");assert(sent[1][4].op=="accept" and sent[1][4].id=="order1");U.business.orders[1].issuer=1;render();click("Cancel");assert(sent[2][4].op=="cancel")')
    def test_free_station_selection_and_specific_denial(self):
        self.check('local B=ug_require("mpfever_business.lua");U.business.stations["sg:A"]={key="sg:A",owner=2,mode="free",fee=0,grants={}};assert(V.stationFee("sg:A",100)==-1);U.business.stations["sg:A"].grants["1"]={use=false};assert(V.stationFee("sg:A",100)==0)')

    def test_personal_development_shows_own_rank_and_sends_only_earned_target(self):
        self.check('''C.int=tostring;U.progression={version=1,ranks={{},{},{}},companies={["1"]={level=1,potentialLevel=2,experience=120},["2"]={level=3,potentialLevel=3,experience=999}}}
            render();click("Development");render();local texts={};for _,v in ipairs(nodes("TextView"))do texts[v.text]=true end
            assert(texts["Personal rank: 1 / 3"] and texts["Personal experience: 120"] and not texts["Personal experience: 999"])
            click("Apply earned rank · 2");assert(sent[1][3]=="progression" and sent[1][4].level==2 and sent[1][4].mpfCo==nil)''')

    def test_no_rank_button_before_personal_experience_reaches_next_rank(self):
        self.check('''C.int=tostring;U.progression={version=1,ranks={{},{}},companies={["1"]={level=1,potentialLevel=1,experience=100}}}
            render();click("Development");render();for _,b in ipairs(nodes("Button"))do assert(not b.content.text:find("Apply earned rank",1,true))end''')

    def test_development_tab_reports_unavailable_without_claiming_shared_rank_as_own(self):
        self.check('''render();click("Development");render();local found=false
            for _,v in ipairs(nodes("TextView"))do if v.text=="Personal development is not available yet."then found=true end end
            assert(found and #sent==0)''')

if __name__=='__main__':unittest.main()
