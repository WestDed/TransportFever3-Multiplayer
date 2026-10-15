-- Engine adapter for saved agreements. API references are recorded in docs/BUSINESS_MECHANICS.md.
local function req(n) local ok,m=pcall(ug_require,n);if ok then return m end;return ug_require("mpfever_1::/"..n) end
local B=req("mpfever_business.lua")
local E={core=B,dirty=true,stopPending={},ownerPending={},nextIndex=0}
local C,R,CO,log
local function comp(e,name)
	if type(e)~="number" or e<0 then return nil end
	local ok,v=pcall(function() return api.engine.getComponent(e,api.type.ComponentType[name]) end);return ok and v or nil
end
local function vec(v) return v and {x=v.x,y=v.y,z=v.z or 0} end
local function get(v,k) local ok,x=pcall(function()return v and v[k]end);if ok then return x end end
function E.geometry(e)
	local edge=comp(e,"BASE_EDGE");if not edge or not comp(e,"BASE_EDGE_STREET") then return nil end
	local a,b=comp(edge.node0,"BASE_NODE"),comp(edge.node1,"BASE_NODE")
	if not a or not b then return nil end
	return {p0=vec(a.position),p1=vec(b.position),t0=vec(edge.tangent0),t1=vec(edge.tangent1)}
end
local function send(cmd,callback)
	local original=C.origCmds();return (original.sendCommand or api.cmd.sendCommand)(cmd,callback)
end
function E.configure(common,refs,companies,logger)
	C,R,CO,log=common,refs,companies,logger
	R.businessOwner=function(e)
		local road=B.roadFor(R.CO,E.geometry(e));return road and CO.entityOf(R.CO,road.owner) or nil
	end
end
local function balance(reg,id)
	local co=B.company(reg,id);if not co then return -math.huge end
	return api.engine.util.finance.getPlayersBalance(co.ent)
end
function E.catalog(reg)
	if E.catalogCache and not E.dirty and os.clock()<(E.catalogAt or 0)+5 then return E.catalogCache.roads,E.catalogCache.targets end
	local roads,targets,seen={},{},{}
	for _,e in ipairs(R.allEdges()) do
		local g=E.geometry(e)
		if g then
			local policy=B.roadFor(reg,g);local owner=policy and policy.owner or CO.idOfEntity(reg,R.rawOwnerOf(e))
			if owner and not seen[policy and policy.key or B.roadKey(g)] then seen[policy and policy.key or B.roadKey(g)]=true;roads[#roads+1]={key=policy and policy.key or B.roadKey(g),entity=e,owner=owner,geometry=g,label=string.format("%.0f, %.0f",g.p0.x,g.p0.y)} end
		end
	end
	for _,e in ipairs(R.entitiesWith("STATION_GROUP")) do
		local name=tostring(e);pcall(function() name=api.engine.util.getEntityName(e) end)
		targets[#targets+1]={key=R.sgKey(e),entity=e,label=name,owner=CO.idOfEntity(reg,R.rawOwnerOf(e))}
	end
	table.sort(roads,function(a,b)return a.key<b.key end);table.sort(targets,function(a,b)return a.key<b.key end)
	E.catalogCache={roads=roads,targets=targets};E.catalogAt=os.clock()
	return roads,targets
end
function E.prepare(reg)
	if not E.recovered then E.recovered=true;B.recover(reg) end
end
function E.command(state,p,now)
	local st=state:get();local reg=st and st.co;if not reg or reg.mode~="separate" then return end
	E.prepare(reg)
	local owner,geometry,rejected
	local roads,targets=E.catalog(reg)
	if p.op=="policy" then
		for _,r in ipairs(p.kind=="station" and targets or roads) do if r.key==p.key then owner,geometry=r.owner,r.geometry;break end end
	elseif p.op=="create" then
		local found=false;for _,t in ipairs(targets) do if t.key==p.target and comp(t.entity,"STATION_GROUP") then found=true;p.targetLabel=t.label end end
		if not found then rejected="order target no longer exists" end
		local ok,res=pcall(api.res.cargoTypeRep.get,p.cargo);if not ok or not res then rejected="unknown cargo type" end
	end
	p.request=p.mpfRequest
	local ok,result=B.command(reg,p.mpfCo,p,{reject=rejected,owner=owner,geometry=geometry,now=now,monthDuration=api.util.getDefaultMonthDuration(),balance=function(id)return balance(reg,id)end})
	if ok and p.op=="policy" then reg.business.revision=(reg.business.revision or 0)+1;E.dirty=true end
	state:set(st);log("business: "..tostring(p.op).." company "..tostring(p.mpfCo).." "..tostring(ok).." "..tostring(result or ""))
end
function E.checkBuild(reg,actor,proposal)
	if not reg or reg.mode~="separate" or not reg.business then return true end
	local sp=proposal and (proposal.proposal or proposal.streetProposal) or {}
	local additions=sp.addedSegments or sp.edgesToAdd or {}
	local nodes={}
	for _,n in ipairs(sp.addedNodes or sp.nodesToAdd or {}) do nodes[n.entity]=n.comp.position end
	local function position(id) return nodes[id] or (comp(id,"BASE_NODE") or {}).position end
	local candidates={}
	for _,added in ipairs(additions) do
		local c=added.comp
		if c then
			local a,b=c.position0 or position(c.node0),c.position1 or position(c.node1)
			if a and b then candidates[#candidates+1]={p0=vec(a),p1=vec(b),t0=vec(c.tangent0),t1=vec(c.tangent1),source=c} end
			for _,n in ipairs({c.node0,c.node1}) do
				local key=E.nodeRoad and E.nodeRoad[n]
				local road=key and reg.business.roads[key]
				if road and road.owner~=actor and not B.can(reg,key,actor,"connect") and not B.can(reg,key,actor,"edit") then return false,"road connection is not permitted" end
			end
		end
	end
	for _,removed in ipairs(sp.removedSegments or sp.edgesToRemove or {}) do
		local e=type(removed)=="number" and removed or removed.entity
		local g=E.geometry(e);local road=B.roadFor(reg,g)
		if road and road.owner~=actor and not B.can(reg,road.key,actor,"edit") then
			if not B.can(reg,road.key,actor,"connect") then return false,"road connection is not permitted" end
			local original=comp(e,"BASE_EDGE");local unchanged={}
			for _,candidate in ipairs(candidates)do
				if B.matches(g,candidate)then
					for _,field in ipairs({"roadTemplate","roadStyle","roadType","type","typeIndex"})do
						if get(original,field)~=get(candidate.source,field)then return false,"road upgrade is not permitted"end
					end
					for _,field in ipairs({"laneConfigs","edgeDecorations"})do
						local a,b=get(original,field),get(candidate.source,field)
						if (a or b) and not B.equal(C.marshal(a or {}),C.marshal(b or {}))then return false,"road lane/decorations edit is not permitted"end
					end
					unchanged[#unchanged+1]=candidate
				end
			end
			if not B.preserves(g,unchanged) then return false,"road edit/deletion is not permitted" end
		end
	end
	for _,object in ipairs(sp.edgeObjectsToAdd or {})do
		local e=object.segmentEntity or object.edgeEntity;local road=B.roadFor(reg,E.geometry(e))
		if road and not B.can(reg,road.key,actor,"connect") and not B.can(reg,road.key,actor,"edit")then return false,"road stop construction is not permitted"end
	end
	for _,n in ipairs(sp.removedNodes or sp.nodesToRemove or {}) do
		local id=type(n)=="number" and n or n.entity;local key=E.nodeRoad and E.nodeRoad[id]
		if key and not B.can(reg,key,actor,"edit") then return false,"road node deletion is not permitted" end
	end
	return true
end
function E.flush(state)
	for _=1,32 do
		local st=state:get();local reg=st and st.co;if not reg or not reg.business then return end
		local tx,index,leg=B.nextLeg(reg);if not tx then return end
		local co=B.company(reg,leg.co);if not co then leg.status="blocked";state:set(st);return end
		leg.status="submitted";state:set(st)
		local txid=tx.id
		local ok,err=pcall(function()
			local entry=api.type.JournalEntry.new();entry.amount=leg.amount;entry.time=-1;entry.category.type=api.type.JournalEntry.Type.OTHER
			send(api.cmd.makeJournalBookAssetCmd(co.ent,entry,api.type.Vec3f.new(0,0,0)),function(_,success)
				local current=state:get();if current and current.co and current.co.business then B.ack(current.co,txid,index,success==true);state:set(current) end
			end)
		end)
		if not ok then local current=state:get();B.ack(current.co,txid,index,false);state:set(current);log("business: journal command failed: "..tostring(err)) end
	end
end
local function vkey(st,v)
	for k,e in pairs(st.bind or {}) do if e==v and tostring(k):sub(1,1)=="V" then return tostring(k) end end
	return "save:"..v
end
local function setOwner(e,player)
	local own=comp(e,"PLAYER_OWNED");local current=own and own.player or -1
	if current==player or E.ownerPending[e]==player then return end
	E.ownerPending[e]=player
	local ok,err=pcall(function() send(api.cmd.makeEntitySetPlayerCmd(e,player),function(_,success) E.ownerPending[e]=nil;if not success then log("business: infrastructure owner command rejected") end end) end)
	if not ok then E.ownerPending[e]=nil;log("business: infrastructure owner command: "..tostring(err)) end
end
function E.reindex(reg)
	E.edgeRoad={};E.managed={};E.nodeRoad={};local nodeOwners={}
	for _,e in ipairs(R.allEdges()) do
		local g=E.geometry(e);local road=B.roadFor(reg,g)
		if road then
			E.edgeRoad[e]=road.key;E.managed[#E.managed+1]={e=e,road=road}
			local edge=comp(e,"BASE_EDGE");local shared=road.connect
			for _,grant in pairs(road.grants) do if grant.connect or grant.edit then shared=true end end
			-- The engine has one owner, no company ACL. Shared roads become
			-- engine-public; the saved agreement still governs every mod action.
			local physical=shared and -1 or CO.entityOf(reg,road.owner)
			setOwner(e,physical)
			for _,n in ipairs({edge.node0,edge.node1}) do
				E.nodeRoad[n]=road.key
				if nodeOwners[n]==nil then nodeOwners[n]=physical elseif nodeOwners[n]~=physical then nodeOwners[n]=-1 end
			end
		end
	end
	for n,owner in pairs(nodeOwners) do setOwner(n,owner) end
	E.catalogCache=nil;E.dirty=false
end
local function stopVehicle(st,v,key,blocked)
	local tv=comp(v,"TRANSPORT_VEHICLE");if not tv then return end
	local s=B.ensure(st.co);s.stopped=s.stopped or {}
	if blocked and not s.stopped[key] then s.stopped[key]={wasStopped=tv.userStopped==true} end
	local marker=s.stopped[key]
	local want=blocked or (marker and marker.wasStopped) or false
	if not blocked and marker then s.stopped[key]=nil end
	if marker and tv.userStopped~=want and E.stopPending[v]~=want then
		E.stopPending[v]=want
		send(api.cmd.makeVehicleSetStoppedByUserCmd(v,want),function() E.stopPending[v]=nil end)
	end
end
function E.update(state,dt,now)
	local st=state:get();local reg=st and st.co;if not reg or reg.mode~="separate" then return end
	local s=B.ensure(reg)
	E.prepare(reg)
	B.expire(reg,now);state:set(st);E.flush(state)
	st=state:get();reg=st.co;s=B.ensure(reg);R.CO=reg
	if E.dirty then E.reindex(reg) end
	if not dt or dt<=0 or now<(E.nextTick or 0) then return end
	E.nextTick=now+200
	if next(s.roads) or next(s.stations) or next(reg.shares or {}) then
		for _,v in ipairs(R.entitiesWith("TRANSPORT_VEHICLE")) do
			local actor=CO.idOfEntity(reg,R.rawOwnerOf(v));local path=comp(v,"MOVE_PATH");local roadKey
			if path and path.dyn and path.dyn.pathPos then
				local pos=path.dyn.pathPos;local index=get(pos,"edgeIndex") or get(pos,"index")
				local edges=get(path.path,"edges") or path.path
				local edge=index and edges and edges[index+1]
				local edgeId=get(edge,"edgeId") or get(edge,1) or edge
				local id=type(edgeId)=="number" and edgeId or get(edgeId,"entity")
				roadKey=id and E.edgeRoad and E.edgeRoad[id]
			end
			if actor then
				local key=vkey(st,v);local allowed,reason=B.visit(reg,actor,key,roadKey,function(id)return balance(reg,id)end)
				local tv=comp(v,"TRANSPORT_VEHICLE");local line=tv and comp(tv.line,"LINE")
				local stop=line and tv.stopIndex and line.stops[tv.stopIndex+1]
				if stop then
					local sg=stop.stationGroup;local owner=CO.idOfEntity(reg,R.rawOwnerOf(sg));local stationKey=R.sgKey(sg)
					if owner and owner~=actor then
						local policy=s.stations[stationKey]
						if policy and not B.can(reg,stationKey,actor,"use","station") or not policy and C.shareFee(reg.shares,stationKey)==0 then allowed=false;reason="station access denied"end
					end
				end
				stopVehicle(st,v,key,not allowed)
				if not allowed and (not E.lastBlocked or E.lastBlocked[key]~=reason) then E.lastBlocked=E.lastBlocked or {};E.lastBlocked[key]=reason;log("business: vehicle "..key.." "..tostring(reason)) end
			end
		end
	end
	-- Shared engine-public roads retain maintenance liability to their business
	-- owner. Accumulate fractional costs; never move station ownership.
	s.maintenance=s.maintenance or {};local last=s.maintenanceAt or now;s.maintenanceAt=now
	local elapsed=math.max(0,now-last)
	local year=api.util.getDefaultYearDuration()
	for _,r in ipairs(E.managed or {}) do
		local owned=comp(r.e,"PLAYER_OWNED");local cost=comp(r.e,"MAINTENANCE_COST")
		if (not owned or owned.player==-1) and cost and cost.maintenanceCost>0 then
			local amount=(s.maintenance[r.road.key] or 0)+cost.maintenanceCost*elapsed/year
			local whole=math.min(1000000000,math.floor(amount));s.maintenance[r.road.key]=amount
			if whole>0 and B.maintenance(reg,r.road.owner,r.road.key,whole) then s.maintenance[r.road.key]=amount-whole end
		end
	end
	state:set(st);E.flush(state)
end
function E.engineEvent(state,id,name,param,now)
	if name~="OnCalcTicketPrice" or id~="TransportVehicleSystem" and id~="SimEntityAtVehicleSystem" then return end
	local st=state:get();local reg=st and st.co;if not reg or reg.mode~="separate" then return end
	local ok,plain=pcall(C.marshal,param);if not ok or type(plain)~="table" then return end
	local list=plain.param or plain
	if list.vehicleEntity then list={list} end
	local changed=false
	for _,p in ipairs(list) do
		local actor=CO.idOfEntity(reg,R.rawOwnerOf(p.lineEntity));local tv=comp(p.vehicleEntity,"TRANSPORT_VEHICLE");local line=comp(p.lineEntity,"LINE")
		local journey=comp(p.simEntity,"SIM_ENTITY_AT_VEHICLE");local cargo=comp(p.simEntity,"SIM_CARGO");local person=comp(p.simEntity,"SIM_PERSON")
		local index=journey and journey.lineStop1 or tv and tv.stopIndex;local stop=line and index and line.stops[index+1]
		if actor and stop and (cargo or person) then
			local token=table.concat({tostring(p.simEntity),tostring(cargo and cargo.startTime or 0),tostring((cargo or person).pickupTime),vkey(st,p.lineEntity),tostring(index),tostring(journey and journey.roundTripCounter or tv and tv.lastLineStopDeparture or 0)},":")
			changed=B.delivery(reg,actor,{token=token,cargo=(cargo or person).cargoType,amount=1,target=R.sgKey(stop.stationGroup)},now) or changed
		end
	end
	if changed then state:set(st);E.flush(state) end
end
function E.view(reg,me)
	R.CO=reg
	local out=B.view(reg,me);if not out then return nil end
	local roads,targets=E.catalog(reg);out.roadCatalog={};out.targets={};
	for _,r in ipairs(roads)do if r.owner==me then out.roadCatalog[#out.roadCatalog+1]={key=r.key,owner=r.owner,label=r.label}end end
	for _,r in ipairs(targets)do out.targets[#out.targets+1]={key=r.key,owner=r.owner,label=r.label}end
	out.me=me;out.companies={};out.cargos={};out.contractOwners=CO.contractOwners and CO.contractOwners() or {}
	for _,co in ipairs(reg.list) do out.companies[#out.companies+1]={id=co.id,ent=co.ent,name=co.name} end
	pcall(function() for id in pairs(api.res.cargoTypeRep.getAll(false)) do local cargo=api.res.cargoTypeRep.get(id);out.cargos[#out.cargos+1]={id=id,name=cargo.name or tostring(id)} end;table.sort(out.cargos,function(a,b)return a.id<b.id end) end)
	return out
end
return E
