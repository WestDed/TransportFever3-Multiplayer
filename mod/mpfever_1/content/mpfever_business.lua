-- Saved company agreements. No engine dependency; the adapter supplies facts and journal acknowledgements.
local B = { VERSION = 1, MAX_ROADS = 2048, MAX_ORDERS = 256, MAX_TRANSACTIONS = 2048 }
local function integer(x, lo, hi) return type(x) == "number" and x == math.floor(x) and x >= lo and x <= hi end
local function text(x, limit) return type(x) == "string" and #x > 0 and #x <= limit and not x:find("%c") end
local function count(t) local n=0; for _ in pairs(t or {}) do n=n+1 end; return n end
local function copy(t) local r={}; for k,v in pairs(t or {}) do r[k]=type(v)=="table" and copy(v) or v end; return r end
function B.sorted(t) local keys={}; for k in pairs(t or {}) do keys[#keys+1]=k end; table.sort(keys,function(a,b) return tostring(a)<tostring(b) end); return keys end
function B.company(reg,id) for _,c in ipairs(reg and reg.list or {}) do if c.id==id and type(c.ent)=="number" then return c end end end
function B.ensure(reg)
	if not reg or reg.mode~="separate" then return nil end
	if not reg.business then reg.business={version=B.VERSION,seq=0,roads={},stations={},orders={},transactions={},visits={},requests={},requestOrder={},notices={}} end
	assert(reg.business.version==B.VERSION,"unsupported business state")
	return reg.business
end
local function nextId(s, prefix) s.seq=s.seq+1; return prefix..s.seq end
local function notice(s,co,code,ref)
	s.notices[tostring(co)]=s.notices[tostring(co)] or {}
	local list=s.notices[tostring(co)]; list[#list+1]={code=code,ref=ref,seq=s.seq}
	while #list>30 do table.remove(list,1) end
end
function B.available(reg,co,balance)
	local s=B.ensure(reg); local have=balance(co)
	if have==nil then return math.huge end -- the game's infinite-money mode
	for _,t in pairs(s.transactions) do
		if t.status~="done" and t.status~="failed" then
			for _,leg in ipairs(t.legs) do if leg.co==co and leg.amount<0 and leg.status~="done" then have=have+leg.amount end end
		end
	end
	return have
end
local function transaction(reg,purpose,reference,legs)
	local s=B.ensure(reg)
	if count(s.transactions)>=B.MAX_TRANSACTIONS then
		for _,id in ipairs(B.sorted(s.transactions)) do
			local t=s.transactions[id]
			if t.status=="done" or t.status=="failed" then s.transactions[id]=nil end
			if count(s.transactions)<B.MAX_TRANSACTIONS then break end
		end
	end
	if count(s.transactions)>=B.MAX_TRANSACTIONS then return nil,"transaction queue full" end
	local id=nextId(s,"tx")
	for _,leg in ipairs(legs) do leg.status="new" end
	s.transactions[id]={id=id,purpose=purpose,reference=reference,legs=legs,status="pending"}
	return id
end
function B.ack(reg,id,index,success)
	local s=B.ensure(reg); local t=s.transactions[id]; local leg=t and t.legs[index]
	if not leg or leg.status~="submitted" or t.status~="pending" then return false end
	if not success then
		-- A failed credit is retried, never another debit. No silent creation/loss of money.
		leg.attempts=(leg.attempts or 0)+1
		leg.status=leg.attempts<3 and "new" or "blocked"
		notice(s,leg.co,"payment_failed",id)
		return true
	end
	leg.status="done"
	for _,l in ipairs(t.legs) do if l.status~="done" then return true end end
	t.status="done"
	local o=s.orders[t.reference]
	if t.purpose=="escrow" and o and o.status=="funding" then o.status="open"; notice(s,o.issuer,"order_open",o.id)
	elseif t.purpose=="reward" and o and o.status=="paying" then o.status="completed";o.escrow=0;notice(s,o.issuer,"order_completed",o.id);notice(s,o.carrier,"reward_paid",o.id)
	elseif t.purpose=="refund" and o and o.status=="refunding" then o.status=o.closeReason or "cancelled";o.escrow=0;notice(s,o.issuer,"order_refunded",o.id) end
	return true
end
function B.nextLeg(reg)
	local s=B.ensure(reg)
	for _,id in ipairs(B.sorted(s.transactions)) do
		local t=s.transactions[id]
		if t.status=="pending" then
			for i,l in ipairs(t.legs) do
				if l.status~="done" then if l.status=="new" then return t,i,l end; break end
			end
		end
	end
end
function B.recover(reg)
	local s=B.ensure(reg)
	-- A saved in-flight engine booking cannot safely be submitted twice.
	-- Keep it visible and blocked until its account/journal can be reconciled.
	for _,t in pairs(s.transactions) do for _,l in ipairs(t.legs) do if l.status=="submitted" then l.status="uncertain" end end end
end
local function permission(p,actor,cap)
	if actor==p.owner then return true end
	local g=p.grants and p.grants[tostring(actor)]
	if g and g[cap]~=nil then return g[cap]==true end
	if cap=="use" then return p.mode~="private" end
	if cap=="connect" then return p.connect==true end
	return false
end
function B.can(reg,key,actor,cap,kind)
	local s=B.ensure(reg);local p=s and s[kind=="station" and "stations" or "roads"][key]
	return p and permission(p,actor,cap) or false
end
function B.setPolicy(reg,actor,p,owner,geometry)
	if p.kind~="road" and p.kind~="station" then return false,"invalid infrastructure kind" end
	local s=B.ensure(reg);local list=p.kind=="station" and s.stations or s.roads
	local old=list[p.key]
	if not text(p.key,240) or not B.company(reg,actor) or (old and old.owner or owner)~=actor then return false,"not infrastructure owner" end
	if p.mode~="private" and p.mode~="free" and p.mode~="toll" then return false,"invalid access mode" end
	if not integer(p.fee or 0,0,1000000000) or p.mode=="toll" and (p.fee or 0)==0 then return false,"invalid fee" end
	if not old and count(list)>=B.MAX_ROADS then return false,"infrastructure limit" end
	local grants=copy(old and old.grants or {})
	if p.company~=nil then
		if not B.company(reg,p.company) or p.company==actor then return false,"invalid grantee" end
		grants[tostring(p.company)]={use=p.use==true,connect=p.connectGrant==true,edit=p.edit==true}
	end
	list[p.key]={key=p.key,kind=p.kind or "road",owner=actor,mode=p.mode,fee=p.mode=="toll" and p.fee or 0,
		connect=p.connect==true,grants=grants,geometry=copy(geometry or old and old.geometry),label=p.label or old and old.label}
	if p.kind=="station" then
		reg.shares=reg.shares or {};reg.shares[p.key]=p.mode=="private" and 0 or (p.mode=="free" and -1 or p.fee)
	end
	return true
end
function B.visit(reg,actor,vehicle,key,balance)
	local s=B.ensure(reg);local old=s.visits[vehicle]
	if not key then if old then old.road=nil; old.unpaid=nil end; return true end
	local road=s.roads[key];if not road then return true end
	if not permission(road,actor,"use") then return false,"road access denied" end
	if old and old.road==key and not old.unpaid then
		local t=old.tx and s.transactions[old.tx]
		return not t or t.status=="done",t and t.status~="done" and "payment pending" or nil
	end
	if not old and count(s.visits)>=16384 then return false,"vehicle visit limit" end
	local seq=(old and old.seq or 0)+((not old or old.road~=key) and 1 or 0)
	local current={road=key,seq=seq};s.visits[vehicle]=current
	if actor==road.owner or road.mode~="toll" then return true end
	if B.available(reg,actor,balance)<road.fee then current.unpaid=true;return false,"insufficient funds for road" end
	local tx,err=transaction(reg,"road",key,{{co=actor,amount=-road.fee},{co=road.owner,amount=road.fee}})
	if not tx then current.unpaid=true;return false,err end
	current.tx=tx;return false,"payment pending"
end
function B.createOrder(reg,actor,p,now,balance)
	local s=B.ensure(reg)
	if not B.company(reg,actor) or not text(p.target,240) or not text(p.title,120) then return false,"invalid order" end
	if not integer(p.cargo,0,4096) or not integer(p.amount,1,1000000) or not integer(p.reward,1,10000000000) or not integer(p.duration,1,120) then return false,"invalid order terms" end
	if count(s.orders)>=B.MAX_ORDERS then
		for _,id in ipairs(B.sorted(s.orders)) do
			local o=s.orders[id]
			if o.status=="completed" or o.status=="cancelled" or o.status=="expired" then s.orders[id]=nil end
			if count(s.orders)<B.MAX_ORDERS then break end
		end
	end
	if count(s.orders)>=B.MAX_ORDERS then return false,"order limit" end
	if B.available(reg,actor,balance)<p.reward then return false,"insufficient escrow funds" end
	local id=nextId(s,"order")
	local tx,err=transaction(reg,"escrow",id,{{co=actor,amount=-p.reward}})
	if not tx then return false,err end
	s.orders[id]={id=id,issuer=actor,title=p.title,target=p.target,targetLabel=p.targetLabel,cargo=p.cargo,amount=p.amount,
		reward=p.reward,escrow=p.reward,progress=0,status="funding",created=now,months=p.duration,deadline=now+(p.monthDuration or 1)*p.duration,tx=tx}
	return true,id
end
function B.acceptOrder(reg,actor,id,now)
	local s=B.ensure(reg);local o=s.orders[id]
	if not o or o.status~="open" or o.deadline<=now or actor==o.issuer or not B.company(reg,actor) then return false,"order unavailable" end
	o.carrier=actor;o.status="active";notice(s,o.issuer,"order_taken",id);notice(s,actor,"order_taken",id);return true
end
local function refund(reg,o,reason)
	local tx,err=transaction(reg,"refund",o.id,{{co=o.issuer,amount=o.escrow}})
	if not tx then return false,err end
	o.status="refunding";o.closeReason=reason;o.tx=tx;return true
end
function B.cancelOrder(reg,actor,id)
	local s=B.ensure(reg);local o=s.orders[id]
	if not o then return false,"unknown order" end
	if actor==o.carrier and o.status=="active" then o.carrier=nil;o.progress=0;o.status="open";return true end
	if actor~=o.issuer or o.status~="open" then return false,"order cannot be cancelled" end
	return refund(reg,o,"cancelled")
end
function B.delivery(reg,actor,event,now)
	local s=B.ensure(reg)
	if not B.company(reg,actor) or not text(event.token,512) or not text(event.target,240) or not integer(event.amount,1,1000000) or not integer(event.cargo,0,4096) then return false end
	-- These entity/revision tokens are local to each copy of the world. Their
	-- semantic outcome is shared; they are deliberately excluded from its hash.
	s.localSeen=s.localSeen or {};s.localSeenOrder=s.localSeenOrder or {}
	if s.localSeen[event.token] then return false end
	s.localSeen[event.token]=now;s.localSeenOrder[#s.localSeenOrder+1]=event.token
	while #s.localSeenOrder>16384 do s.localSeen[table.remove(s.localSeenOrder,1)]=nil end
	for _,id in ipairs(B.sorted(s.orders)) do
		local o=s.orders[id]
		if o.status=="active" and o.carrier==actor and o.target==event.target and o.cargo==event.cargo and o.deadline>now then
			o.progress=math.min(o.amount,o.progress+event.amount)
			if o.progress==o.amount then
				local tx=transaction(reg,"reward",id,{{co=actor,amount=o.escrow}})
				if tx then o.status="paying";o.tx=tx end
			end
			return true -- one delivery cannot complete multiple orders
		end
	end
	return false
end
function B.expire(reg,now)
	local s=B.ensure(reg)
	for _,id in ipairs(B.sorted(s.orders)) do
		local o=s.orders[id]
		if (o.status=="open" or o.status=="active") and o.deadline<=now then refund(reg,o,"expired") end
	end
end
function B.maintenance(reg,owner,key,amount)
	if not integer(amount,1,1000000000) then return nil end
	return transaction(reg,"maintenance",key,{{co=owner,amount=-amount}})
end
function B.curve(g,t)
	local t2,t3=t*t,t*t*t;local out={}
	for _,k in ipairs({"x","y","z"}) do
		local a,b=g.p0[k] or 0,g.p1[k] or 0
		local u,v=(g.t0 and g.t0[k]) or b-a,(g.t1 and g.t1[k]) or b-a
		out[k]=(2*t3-3*t2+1)*a+(t3-2*t2+t)*u+(-2*t3+3*t2)*b+(t3-t2)*v
	end
	return out
end
function B.roadKey(g)
	local function key(p) return string.format("%.2f,%.2f,%.2f",p.x,p.y,p.z or 0) end
	local a,b=key(g.p0),key(g.p1);if a>b then a,b=b,a end
	return "road:"..a.."~"..b.."~"..key(B.curve(g,0.5))
end
local function distance2(a,b) local x,y,z=a.x-b.x,a.y-b.y,(a.z or 0)-(b.z or 0);return x*x+y*y+z*z end
function B.onCurve(g,p)
	local best,at=math.huge,0
	for i=0,32 do local d=distance2(B.curve(g,i/32),p);if d<best then best,at=d,i/32 end end
	local lo,hi=math.max(0,at-1/32),math.min(1,at+1/32)
	for _=1,16 do local a,b=lo+(hi-lo)/3,hi-(hi-lo)/3;if distance2(B.curve(g,a),p)<distance2(B.curve(g,b),p) then hi=b else lo=a end end
	local t=(lo+hi)/2
	return math.min(best,distance2(B.curve(g,t),p))<=0.25,t
end
function B.matches(root,g)
	return root and g and B.onCurve(root,g.p0) and B.onCurve(root,g.p1) and B.onCurve(root,B.curve(g,0.5))
end
function B.roadFor(reg,g)
	local s=reg and reg.business
	if not s or not g then return nil end
	local exact=s.roads[B.roadKey(g)];if exact then return exact end
	for _,key in ipairs(B.sorted(s.roads)) do local r=s.roads[key];if B.matches(r.geometry,g) then return r end end
end
-- A connection may split an existing segment, but must preserve its entire
-- curve. Two short remnants do not authorize deleting the middle of a road.
function B.preserves(original,candidates)
	local intervals={}
	for _,g in ipairs(candidates) do
		if B.matches(original,g) then
			local _,a=B.onCurve(original,g.p0);local _,b=B.onCurve(original,g.p1)
			intervals[#intervals+1]={math.min(a,b),math.max(a,b)}
		end
	end
	table.sort(intervals,function(a,b)return a[1]<b[1]end)
	local reached=0
	for _,r in ipairs(intervals) do if r[1]>reached+0.0001 then return false end;reached=math.max(reached,r[2]) end
	return reached>=0.9999
end
function B.stationCharge(reg,from,to,key,fee)
	if from==to or not B.company(reg,from) or not B.company(reg,to) or not integer(fee,1,1000000000) then return nil end
	return transaction(reg,"station",key,{{co=from,amount=-fee},{co=to,amount=fee}})
end
function B.command(reg,actor,p,ctx)
	local s=B.ensure(reg)
	if not s or type(p)~="table" or not text(p.request,240) or not B.company(reg,actor) then return false,"invalid business command" end
	if s.requests[p.request] then return s.requests[p.request].ok,s.requests[p.request].result end
	local ok,result
	if p.op=="policy" then ok,result=B.setPolicy(reg,actor,p,ctx.owner,ctx.geometry)
	elseif p.op=="create" then local q=copy(p);q.monthDuration=ctx.monthDuration;ok,result=B.createOrder(reg,actor,q,ctx.now,ctx.balance)
	elseif p.op=="accept" then ok,result=B.acceptOrder(reg,actor,p.id,ctx.now)
	elseif p.op=="cancel" then ok,result=B.cancelOrder(reg,actor,p.id)
	else ok,result=false,"unknown business operation" end
	s.requests[p.request]={ok=ok,result=result};s.requestOrder[#s.requestOrder+1]=p.request
	while #s.requestOrder>4096 do s.requests[table.remove(s.requestOrder,1)]=nil end
	notice(s,actor,ok and "request_ok" or "request_failed",result or p.key or p.op)
	return ok,result
end
function B.sharedState(reg)
	local s=B.ensure(reg);if not s then return nil end
	-- Hash semantic obligations and delivery progress; callback timing, local
	-- entity tokens and local engine bookkeeping are checked by account hashes.
	local out={roads=copy(s.roads),stations=copy(s.stations),orders={}}
	for id,o in pairs(s.orders) do
		out.orders[id]={issuer=o.issuer,carrier=o.carrier,title=o.title,target=o.target,cargo=o.cargo,amount=o.amount,reward=o.reward,
			progress=o.progress,deadline=o.deadline,closeReason=o.closeReason}
	end
	return out
end
function B.view(reg,me)
	local s=B.ensure(reg); if not s then return nil end
	local r={version=s.version,roads=copy(s.roads),stations=copy(s.stations),orders={},notices=copy(s.notices[tostring(me)] or {}),payments={}}
	for _,id in ipairs(B.sorted(s.orders)) do local o=s.orders[id];if o.status=="open" or o.issuer==me or o.carrier==me then r.orders[#r.orders+1]=copy(o) end end
	for _,id in ipairs(B.sorted(s.transactions)) do local t=s.transactions[id];for _,l in ipairs(t.legs) do if l.co==me then r.payments[#r.payments+1]={id=id,purpose=t.purpose,amount=l.amount,status=l.status} end end end
	while #r.payments>30 do table.remove(r.payments,1) end
	return r
end
return B
