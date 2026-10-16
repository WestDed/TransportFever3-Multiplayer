-- Company notification projection. Never removes simulation notifications or journal entries.
local P={}
local function clone(t) local r={};for k,v in pairs(t or {})do r[k]=v end;return r end
local function entity(v)
	if type(v)=="number"then return v end
	if type(v)=="table"then return v.entity or v[1]end
end
function P.visible(entry,ctx)
	if not ctx or not ctx.me or not ctx.coMap then return true end
	local n=entry and (entry.notification or entry);if type(n)~="table"then return true end
	local p=n.params or {};local explicit=p.mpfCo or p.companyId
	if explicit then return explicit==ctx.me end
	for _,k in ipairs({"playerEntity","player","ownerPlayer"})do
		local co=ctx.coMap[entity(p[k])];if co then return co==ctx.me end
	end
	if p.uid and ctx.contractOwner then local co=ctx.contractOwner(p.uid);if co then return co==ctx.me end end
	local mine,foreign=false,false
	local function consider(v)
		local e=entity(v);if not e then return end
		local co=ctx.coMap[e]
		if not co and ctx.ownerOf then local ok,o=pcall(ctx.ownerOf,e);if ok then co=ctx.coMap[o]end end
		if co then if co==ctx.me then mine=true else foreign=true end end
	end
	for _,k in ipairs({"entity","vehicleEntity","lineEntity","constructionEntity","stationGroupEntity","stationEntity"})do consider(p[k])end
	for _,k in ipairs({"entities","vehicles","entitiesAndRevisions0"})do for _,v in ipairs(p[k] or {})do consider(v)end end
	for _,v in ipairs(entry.persisting or {})do consider(v)end
	-- Towns, industries, weather and unclaimed offers are shared. A message
	-- involving this company's assets stays visible even if it also names others.
	return mine or not foreign
end
function P.state(state,ctx)
	if not state then return state end
	local out=clone(state);out.notifications={};out.history={}
	for id,e in pairs(state.notifications or {})do if P.visible(e,ctx)then out.notifications[id]=e end end
	for _,id in ipairs(state.history or {})do if out.notifications[id]then out.history[#out.history+1]=id end end
	return out
end
function P.install(requireModule,context,logger)
	local ok,u=pcall(requireModule,"/game_mechanics/notifications/notification_util.tl")
	if not ok or type(u)~="table"then return false end
	local original=u.__mpfPrivacyOriginal
	if not original then
		original={};for _,k in ipairs({"externalGetNotificationsState","getHistoryFromNative","getPersistingEntity2NotificationFromNative","getNotificationFromNative","getNotificationEntryFromNative","preFilterOutExcessiveVehicleNotification","filterOutExcessiveVehicleNotification"})do original[k]=u[k]end
		u.__mpfPrivacyOriginal=original
	end
	if type(original.externalGetNotificationsState)=="function"then u.externalGetNotificationsState=function(...)return P.state(original.externalGetNotificationsState(...),context())end end
	local function visible(native,id)
		local fn=original.getNotificationEntryFromNative or original.getNotificationFromNative
		if not fn then return true end
		local success,entry=pcall(fn,native,id);return not success or P.visible(entry,context())
	end
	if type(original.getHistoryFromNative)=="function"then u.getHistoryFromNative=function(native,...)
		local out={};for _,id in ipairs(original.getHistoryFromNative(native,...)or {})do if visible(native,id)then out[#out+1]=id end end;return out
	end end
	if type(original.getPersistingEntity2NotificationFromNative)=="function"then u.getPersistingEntity2NotificationFromNative=function(native,...)
		local out={};for entity,ids in pairs(original.getPersistingEntity2NotificationFromNative(native,...)or {})do local list={};for _,id in ipairs(ids)do if visible(native,id)then list[#list+1]=id end end;if #list>0 then out[entity]=list end end;return out
	end end
	-- These are the documented GUI prefilters used before popup mounting/sound.
	for _,key in ipairs({"preFilterOutExcessiveVehicleNotification","filterOutExcessiveVehicleNotification"})do
		local fn=original[key];if type(fn)=="function"then u[key]=function(entry,...)if not P.visible(entry,context())then return true end;return fn(entry,...)end end
	end
	logger("companies: notification history, popups and map markers filtered for local company")
	return true
end
return P
