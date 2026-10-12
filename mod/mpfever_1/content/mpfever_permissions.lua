-- Pure permission rules; ownerOf is supplied AFTER entity references are resolved.
-- Unknown ownership denies fleet/line mutations. World graph rights need engine tests.
local P = {}
P.MUTATE = {
	makeVehicleSellCmd=true, makeVehicleSetLineCmd=true, makeVehicleSendToDepotCmd=true,
	makeVehicleReverseCmd=true, makeVehicleReplaceCmd=true, makeVehicleSetStoppedByUserCmd=true,
	makeVehicleTryToDepartCmd=true, makeVehicleSetManualDepartureCmd=true, makeVehicleSetModifiersCmd=true,
	makeLineUpdateCmd=true, makeLineDestroyCmd=true, makeEntitySetNameCmd=true, makeEntitySetColorCmd=true,
}
function P.check(reg, action, ownerOf)
	if not reg or reg.mode ~= "separate" then return true end
	local actor
	for _, c in ipairs(reg.list or {}) do if c.id == action.co then actor = c.ent end end
	if type(actor) ~= "number" then return false, "company is not ready" end
	local args = action.args or {}
	local function own(entity)
		if type(entity) ~= "number" or entity < 0 then return false end
		local ok, owner = pcall(ownerOf, entity)
		return ok and owner == actor
	end
	if P.MUTATE[action.fn] and not own(args[1]) then return false, "foreign or unknown mutation target" end
	if action.fn == "makeVehicleBuyCmd" then
		if args[1] ~= actor or not own(args[2]) then return false, "foreign player or depot" end
	elseif action.fn == "makeLineCreateCmd" and args[3] ~= actor then return false, "foreign line owner"
	elseif action.fn == "makeVehicleSetLineCmd" and not own(args[2]) then return false, "foreign line"
	elseif action.fn == "makeVehicleSendToDepotCmd" and args[2] ~= nil and args[2] ~= -1 and not own(args[2]) then return false, "foreign depot" end
	return true
end
return P
