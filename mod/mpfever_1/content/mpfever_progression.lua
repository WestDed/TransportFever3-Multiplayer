-- Separate-company development. Plain saved data uses stable company numbers,
-- never player entities. Engine integration is below the pure rules.
local P = { VERSION = 1, MAX_SEEN = 8192, MAX_XP = 1000000000000 }
local function integer(n, lo, hi)
	return type(n) == "number" and n == n and n >= lo and n <= hi and n == math.floor(n)
end
local function company(reg, id)
	for _, c in ipairs(reg and reg.list or {}) do if c.id == id then return c end end
end
local function copy(t)
	local out = {}; for k, v in pairs(t or {}) do out[k] = type(v) == "table" and copy(v) or v end; return out
end
local function potential(s, xp)
	local rank = 1
	for i, r in ipairs(s.ranks) do if xp >= r.xp then rank = i end end
	return rank
end
local function ticket(s, c)
	c.ticketPriceMultiplier = s.ranks[c.level].ticket
end
function P.ensure(reg, config, seeds)
	if not reg or reg.mode ~= "separate" then return false end
	local s, initial = reg.progression, reg.progression == nil
	if s and s.version ~= P.VERSION then error("unsupported company progression version") end
	if not s then
		assert(integer(config.basePopulation, 0, P.MAX_XP), "invalid base population")
		assert(type(config.ranks) == "table" and #config.ranks >= 1 and #config.ranks <= 256, "invalid rank list")
		local previous = -1
		for _, r in ipairs(config.ranks) do
			assert(integer(r.xp, 0, P.MAX_XP) and r.xp >= previous, "invalid XP threshold")
			assert(r.ticket == nil or type(r.ticket) == "number" and r.ticket > 0 and r.ticket <= 100, "invalid ticket multiplier")
			previous = r.xp
		end
		assert(config.enforcedLevel == nil or integer(config.enforcedLevel, 1, #config.ranks), "invalid enforced rank")
		s = { version = P.VERSION, basePopulation = config.basePopulation, ranks = copy(config.ranks), enforcedLevel = config.enforcedLevel,
			companies = {}, seen = { map = {}, ring = {}, next = 1 } }
		reg.progression = s
	end
	local changed = initial
	for _, c in ipairs(reg.list or {}) do
		local key = tostring(c.id)
		if not s.companies[key] then
			local seed = initial and seeds and seeds[c.id] or nil
			local xp = seed and integer(seed.experience, 0, P.MAX_XP) and seed.experience or s.basePopulation
			local level = s.enforcedLevel or (seed and integer(seed.level, 1, #s.ranks) and seed.level or 1)
			-- Preserve existing unlocks even if an old save's XP is below their threshold.
			if not s.enforcedLevel then xp = math.max(xp, s.ranks[level].xp) end
			local own = { experience = xp, level = level, potentialLevel = s.enforcedLevel or math.max(level, potential(s, xp)), deliveries = 0 }
			ticket(s, own); s.companies[key] = own; changed = true
		end
	end
	return changed
end
function P.view(reg, id)
	local s = reg and reg.progression
	if not s or s.version ~= P.VERSION or not company(reg, id) then return nil end
	local c = s.companies[tostring(id)]; return c and copy(c) or nil
end
-- A point is earned for one passenger/cargo item on the company's paid leg.
-- Tokens are local engine identities, saved only for replay suppression. They
-- are deliberately absent from the cross-machine digest and the GUI view.
function P.credit(reg, id, token, now)
	local s = reg and reg.progression
	local c = P.view(reg, id)
	if not c or type(token) ~= "string" or #token == 0 or #token > 512 or not integer(now, 0, P.MAX_XP) then return false end
	local key = tostring(id) .. ":" .. token
	if s.seen.map[key] then return false end
	local slot = s.seen.next
	local previous = s.seen.ring[slot]
	-- Do not evict another delivery from this step: a repeated oversized batch
	-- would otherwise walk the ring and earn all its XP a second time.
	if previous and previous.at >= now then return false end
	if previous then s.seen.map[previous.key] = nil end
	s.seen.ring[slot] = { key = key, at = now }; s.seen.map[key] = true; s.seen.next = slot % P.MAX_SEEN + 1
	c.experience = math.min(P.MAX_XP, c.experience + 1)
	c.deliveries = math.min(P.MAX_XP, c.deliveries + 1)
	c.potentialLevel = s.enforcedLevel or math.max(c.level, potential(s, c.experience))
	s.companies[tostring(id)] = c
	return true
end
function P.apply(reg, id, level)
	local c = P.view(reg, id)
	if not c then return false, "company progression is not ready" end
	if not integer(level, 1, #reg.progression.ranks) or level < c.level or level > c.potentialLevel then return false, "rank is not earned" end
	if level == c.level then return true, false end
	c.level = level; ticket(reg.progression, c); reg.progression.companies[tostring(id)] = c
	return true, true
end
function P.sharedState(reg)
	local s = reg and reg.progression
	if not s then return {} end
	return { version = s.version, basePopulation = s.basePopulation, ranks = copy(s.ranks), enforcedLevel = s.enforcedLevel, companies = copy(s.companies) }
end

-- Adapter: documented company progression modules and the existing game-script
-- wrapper mechanism. No new native hooks or guessed memory offsets.
local E = {}; P.engine = E
local C, R, CO, log
local function req(name)
	for _, root in ipairs({ "/game_mechanics/company/", "::/game_mechanics/company/" }) do
		local ok, m = pcall(ug_require, root .. name)
		if ok and type(m) == "table" then return m end
	end
end
local function registry()
	local st = E.state and E.state:get(); return st and st.co
end
local function comp(e, ct)
	if type(e) ~= "number" or e < 0 then return nil end
	local ok, value = pcall(api.engine.getComponent, e, api.type.ComponentType[ct])
	return ok and value or nil
end
local function owner(e)
	local po = comp(e, "PLAYER_OWNED"); return po and po.player
end
local function read(entity)
	local reg = registry(); local id = CO.idOfEntity(reg, entity)
	if id and reg.progression then return P.view(reg, id), true end
	return nil, false, id and reg.canon or nil
end
function E.configure(common, refs, companies, logger)
	C, R, CO, log = common, refs, companies, logger
	E.state, E.warned = nil, nil
end
local function active()
	local reg = registry()
	return reg and reg.mode == "separate" and reg.progression and reg.progression.version == P.VERSION and reg
end
-- Keep the native script's documented companyState mirror current for save
-- metadata and modules that read it directly. The authoritative data remains
-- reg.progression, keyed by stable company numbers.
function E.syncNative(state, reg)
	local data = state:get() or {}
	data.companyState = data.companyState or {}
	for _, co in ipairs(reg.list or {}) do
		if co.ent then data.companyState[co.ent] = P.view(reg, co.id) end
	end
	state:set(data)
end
function P.nativeDigest(data, reg)
	local result = {}
	for k, v in pairs(data or {}) do if k ~= "companyState" then result[k] = v end end
	result.companyState = {}
	for entity, v in pairs(data and data.companyState or {}) do
		local id = CO.idOfEntity(reg, entity)
		result.companyState[id and "company:" .. tostring(id) or "unregistered:" .. tostring(entity)] = v
	end
	return result
end
-- Called from the game's own price handler: it must return multipliers, not
-- just observe the event in our bridge (whose event return is otherwise lost).
function E.prices(reg, param)
	local out, any = {}, false
	local list = C.marshal(param)
	list = type(list) == "table" and (list.param or list) or {}
	if list.vehicleEntity then list = { list } end
	for i, item in ipairs(list) do
		local id = CO.idOfEntity(reg, owner(item.lineEntity))
		local c = id and P.view(reg, id)
		if c and c.ticketPriceMultiplier then out[i] = c.ticketPriceMultiplier; any = true end
	end
	return any and out or nil
end
function E.install()
	local g = type(game) == "table" and (game["::/game_mechanics/company/company_growth.script"] or game["::game_mechanics/company/company_growth.script"])
	local util, cu = req("company_progression_util.tl"), req("company_util.tl")
	if not (type(g) == "table" and type(g.update) == "function" and type(g.handleEvent) == "function" and util and cu
		and type(util.getCompanyProgressionState) == "function"
		and type(util.getXpNeededForRank) == "function" and type(util.getMaxRank) == "function"
		and type(util.getTicketPriceMultiplierForLevel) == "function" and type(cu.getBasePopulation) == "function") then return false end
	E.util, E.cu = util, cu
	CO.patchProgression(req, read, log)
	if not g.__mpfProgression then
		local oldUpdate, oldEvent = g.update, g.handleEvent
		g.update = function(u, st, ...)
			local reg = active()
			if not reg then return oldUpdate(u, st, ...) end
			E.syncNative(st, reg)
			-- The original update raises XP from WORLD population. It must not also
			-- advance company 1 after the saved independent model is active.
		end
		g.handleEvent = function(u, st, src, id, name, param, ...)
			local reg = active()
			if reg then
				if name == "applyLevel" or name == "_debugAddExperience" then return end
				if name == "OnCalcTicketPrice" and (id == "TransportVehicleSystem" or id == "SimEntityAtVehicleSystem") then
					return E.prices(reg, param)
				end
				if name == "storeSavegameMetadata" then E.syncNative(st, reg) end
			end
			return oldEvent(u, st, src, id, name, param, ...)
		end
		g.__mpfProgression = true
		log("companies: independent XP/rank handler installed")
	end
	return true
end
function E.update(state)
	E.state = state
	local st = state:get(); local reg = st and st.co
	if not reg or reg.mode ~= "separate" then return end
	if not E.install() then
		if not E.warned then E.warned = true; log("companies: independent progression unavailable (game modules not ready); shared legacy progression retained") end
		return
	end
	local changed
	if not reg.progression then
		local count = E.util.getMaxRank()
		assert(integer(count, 1, 256), "unsupported company rank count")
		local config = { basePopulation = E.cu.getBasePopulation(), ranks = {} }
		for i = 1, count do config.ranks[i] = { xp = E.util.getXpNeededForRank(config.basePopulation, i), ticket = E.util.getTicketPriceMultiplierForLevel(i) } end
		local rep = api.res and api.res.genericRep
		if rep and rep.find and rep.get then
			local id = rep.find("::/game_mechanics/company/company_growth_config.res")
			if id and id >= 0 then config.enforcedLevel = rep.get(id).data.enforcedLevel end
		end
		local seeds = {}
		local canonical = CO.originalProgression(reg.canon)
		-- A just-created game's native progression may not be initialized yet.
		if not canonical or not integer(canonical.experience, 0, P.MAX_XP) or not integer(canonical.level, 1, count) then return end
		for _, co in ipairs(reg.list or {}) do seeds[co.id] = co.ent and CO.originalProgression(co.ent) or canonical; seeds[co.id] = seeds[co.id] or canonical end
		changed = P.ensure(reg, config, seeds)
		log("companies: saved personal progression initialized; existing ranks preserved")
	else
		changed = P.ensure(reg)
	end
	if changed then state:set(st) end
end
function E.command(state, p)
	E.state = state
	local st = state:get(); local reg = st and st.co
	if not reg or reg.mode ~= "separate" or type(p) ~= "table" then return end
	local ok, changed = P.apply(reg, p.mpfCo, p.level)
	if ok and changed then state:set(st) end
	log("companies: apply rank for company " .. tostring(p.mpfCo) .. ": " .. tostring(ok) .. " " .. tostring(changed))
end
function E.engineEvent(state, id, name, param, now)
	E.state = state
	if name ~= "OnCalcTicketPrice" or id ~= "TransportVehicleSystem" and id ~= "SimEntityAtVehicleSystem" then return end
	local reg = active(); if not reg then return end
	local st = state:get(); reg = st.co
	local plain = C.marshal(param); local list = plain and (plain.param or plain)
	if type(list) ~= "table" then return end
	if list.vehicleEntity then list = { list } end
	local changed = false
	for _, p in ipairs(list) do
		local lineOwner, vehicleOwner = owner(p.lineEntity), owner(p.vehicleEntity)
		local co = lineOwner and lineOwner == vehicleOwner and CO.idOfEntity(reg, lineOwner)
		local item = comp(p.simEntity, id == "TransportVehicleSystem" and "SIM_CARGO" or "SIM_PERSON")
		if co and item and integer(item.pickupTime, 0, P.MAX_XP) and type(p.basePrice) == "number" and p.basePrice > 0 and type(p.distance) == "number" and p.distance > 0 then
			-- Boarding identity remains available after unloading too. Requiring
			-- SIM_ENTITY_AT_VEHICLE here would lose XP when that component is gone.
			-- One item on one boarding leg earns one point, not one per stop.
			-- SimPerson has pickupTime, but startTime belongs to SimCargo only.
			-- A native component can throw on a field that is not in its type.
			local startTime = id == "TransportVehicleSystem" and item.startTime or 0
			local token = table.concat({ tostring(p.simEntity), tostring(startTime), tostring(item.pickupTime), tostring(p.lineEntity) }, ":")
			changed = P.credit(reg, co, token, now) or changed
		end
	end
	if changed then state:set(st) end
end
return P
