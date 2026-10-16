-- Local presentation only. All changes travel through the stamped scripting event.
local function req(n) local ok,m=pcall(ug_require,n);if ok then return m end;return ug_require("mpfever_1::/"..n) end
local B=req("mpfever_business.lua")
local E=req("mpfever_business_engine.lua")
local V={}
local C,R,U,log
function V.configure(common,refs,context,logger)
	C,R,U,log=common,refs,context,logger
	R.businessOwner=function(e)
		local reg=V.registry();local p=B.roadFor(reg,E.geometry(e))
		if p then for ent,id in pairs(U.coMap or {}) do if id==p.owner then return ent end end end
	end
end
function V.registry()
	local v=U.business;if not v then return nil end
	return {mode="separate",list=v.companies,business={version=1,roads=v.roads,stations=v.stations}}
end
function V.poll()
	local f=C.IO.open(C.DIR..C.BS.."business.txt","rb");if not f then return end
	local txt=f:read("*a");f:close();if txt==U.businessTxt then return end
	if txt=="" then U.businessTxt=txt;U.business=nil;return end
	local view,err=C.deser(txt)
	if type(view)~="table" or view.me~=U.myCo then if err then log("business view: "..tostring(err)) end;return end
	U.businessTxt=txt;U.business=view;U.businessMessage=nil;U.businessVersion=(U.businessVersion or 0)+1;U.shareVersion=(U.shareVersion or 0)+1
end
function V.roadDecision(entity,cap)
	local reg=V.registry();if not reg or not U.myCo then return nil end
	local p=B.roadFor(reg,E.geometry(entity))
	if not p then
		local ok,node=pcall(function()return api.engine.getComponent(entity,api.type.ComponentType.BASE_NODE)end)
		if ok and node then
			for _,key in ipairs(B.sorted(reg.business.roads)) do local r=reg.business.roads[key]
				if B.onCurve(r.geometry,node.position) then p=r;break end
			end
		end
	end
	if p then return B.can(reg,p.key,U.myCo,cap) end
end
function V.stationFee(key,fee)
	local reg=V.registry();local p=reg and reg.business.stations[key]
	if p then if not B.can(reg,key,U.myCo,"use","station") then return 0 end;return p.mode=="toll" and p.fee or -1 end
	return fee
end
local function command(p)
	local function number(n,lo,hi)return type(n)=="number" and n==math.floor(n) and n>=lo and n<=hi end
	local function text(s,max)return type(s)=="string" and #s>0 and #s<=max and not s:find("%c")end
	local valid=false
	if p.op=="policy"then
		if p.mode~="toll"then p.fee=0 end
		if p.label and not text(p.label,120)then p.label=nil end
		valid=text(p.key,240) and (p.kind=="road" or p.kind=="station") and (p.mode=="private" or p.mode=="free" or p.mode=="toll")
			and number(p.fee,p.mode=="toll" and 1 or 0,1000000000) and (p.company==nil or number(p.company,1,4096))
	elseif p.op=="create"then valid=text(p.title,120) and text(p.target,240) and number(p.cargo,0,4096) and number(p.amount,1,1000000) and number(p.reward,1,10000000000) and number(p.duration,1,120)
	elseif p.op=="accept" or p.op=="cancel"then valid=text(p.id,120)end
	if not valid then
		U.businessMessage="invalid";U.businessVersion=(U.businessVersion or 0)+1;return false
	end
	U.businessMessage="pending";U.businessVersion=(U.businessVersion or 0)+1
	api.cmd.sendCommand(api.cmd.makeScriptingSendEventCmd("mpfever","mpfever","business",p));return true
end
function V.install(react,builtin,windowApi)
	if U.openBusiness then return end
	local T=C.T
	local H,W=builtin.type.Orientation.Horizontal,builtin.type.Orientation.Vertical
	local function text(t) return builtin.TextView{meta={class="font-scale-body"},text=tostring(t or "")} end
	local function row(t) return builtin.BoxLayout{orientation=H,children=t} end
	local function button(label,fn) return builtin.Button{meta={class="secondary"},content=text(label),onClick=fn} end
	local function combo(value,choices,fn)
		local items={};for _,c in ipairs(choices) do items[#items+1]=builtin.ComboBoxItem{value=c.id,content=text(c.name)} end
		return builtin.ComboBox{value=value,items=items,onValueChange=fn}
	end
	local function input(value,fn,max) return builtin.TextInputField{value=tostring(value or ""),maxLength=max or 120,onTyping=fn,onValueChange=fn,acceptOnFocusLoss=false} end
	local labels={private=T("Privée","Private"),free=T("Gratuite","Free"),toll=T("Payante","Toll")}
	local states={funding=T("Réservation","Reserving"),open=T("Disponible","Available"),active=T("En cours","In progress"),paying=T("Paiement","Paying"),completed=T("Terminée","Completed"),refunding=T("Remboursement","Refunding"),cancelled=T("Annulée","Cancelled"),expired=T("Expirée","Expired")}
	local Window=react.RegisterWrapperRecipe("MPFeverBusinessWindow",builtin.Window,function(wp)
		local refresh=react.useState(U.businessVersion or 0)
		local state=react.useState({tab="access",page=1,kind="road",mode="private",fee="100",connect=false,use=true,connectGrant=false,edit=false,
			title="",amount="100",reward="10000",duration="12",cargo=0})
		react.onStepTimer(function()if refresh:old()~=(U.businessVersion or 0) then refresh:set(U.businessVersion or 0) end end,0.5)
		local draft=state:old();local m=draft;local v=U.business or {orders={},companies={},roadCatalog={},targets={},cargos={},roads={},stations={},payments={}}
		local function set(k,value) local n={};for a,b in pairs(draft)do n[a]=b end;n[k]=value;draft=n;state:set(n) end
		local function name(id) for _,c in ipairs(v.companies or {}) do if c.id==id then return c.name end end;return tostring(id or "") end
		local rows={row{button(T("Développement","Development"),function()set("tab","development")end),button(T("Infrastructure","Infrastructure"),function()set("tab","access")end),button(T("Commandes","Orders"),function()set("tab","orders")end),button(T("Paiements","Payments"),function()set("tab","payments")end)}}
		if m.tab=="development" then
			local progress=U.progression
			local own=progress and progress.companies and progress.companies[tostring(U.myCo)]
			if progress and progress.version==1 and own then
				rows[#rows+1]=text(T("Rang personnel","Personal rank")..": "..own.level.." / "..#progress.ranks)
				rows[#rows+1]=text(T("Expérience personnelle","Personal experience")..": "..C.int(own.experience))
				rows[#rows+1]=text(T("Gagnez 1 XP par passager ou unité de fret sur votre étape de transport payante.","Earn 1 XP per passenger or cargo item on your paid transport leg."))
				if own.potentialLevel>own.level then
					rows[#rows+1]=button(T("Confirmer le rang acquis","Apply earned rank").." · "..own.potentialLevel,function()
						api.cmd.sendCommand(api.cmd.makeScriptingSendEventCmd("mpfever","mpfever","progression",{level=own.potentialLevel}))
					end)
				end
			else rows[#rows+1]=text(T("Le développement personnel n’est pas encore disponible.","Personal development is not available yet."))end
		elseif not U.business then rows[#rows+1]=text(T("En attente des entreprises…","Waiting for companies…"))
		elseif m.tab=="access" then
			local choices={};local selected
			for _,list in ipairs({v.roadCatalog or {},v.targets or {}}) do for _,r in ipairs(list) do if r.owner==U.myCo then
				local kind=list==v.roadCatalog and "road" or "station";local id=kind..":"..r.key
				choices[#choices+1]={id=id,name=(kind=="road" and T("Route","Road") or T("Arrêt","Stop")).." · "..r.label}
				if id==m.selected then selected=r end
			end end end
			local function choose(id)
				local kind,key=id:match("^(%a+):(.*)$");local p=(kind=="road" and v.roads or v.stations)[key]
				local fee=kind=="station" and C.shareFee(U.shares,key) or 0
				set("selected",id);set("kind",kind);set("key",key);set("mode",p and p.mode or (fee>0 and "toll" or fee==-1 and "free" or "private"));set("fee",tostring(p and p.fee or math.max(100,fee)));set("connect",p and p.connect or false)
			end
			rows[#rows+1]=combo(m.selected,choices,choose)
			if selected then
				rows[#rows+1]=row{text(T("Accès","Access")),combo(m.mode,{{id="private",name=labels.private},{id="free",name=labels.free},{id="toll",name=labels.toll}},function(x)set("mode",x)end)}
				rows[#rows+1]=row{text(T("Tarif","Fee")),input(m.fee,function(x)set("fee",x)end,10)}
				rows[#rows+1]=text(m.kind=="road" and T("Tarif par entrée sur cette route ; propriétaire gratuit.","Fee per entry onto this road; owner travels free.") or T("Tarif par arrêt ; propriétaire gratuit.","Fee per stop; owner stops free."))
				local function toggle(key,label) return button(label..": "..(m[key] and T("Oui","Yes") or T("Non","No")),function()set(key,not m[key])end) end
				if m.kind=="road" then rows[#rows+1]=toggle("connect",T("Connexion pour tous","Connection for everyone")) end
				local cos={{id=0,name=T("Règle générale","General rule")}};for _,co in ipairs(v.companies)do if co.id~=U.myCo then cos[#cos+1]={id=co.id,name=co.name}end end
				rows[#rows+1]=combo(m.company or 0,cos,function(x)
					set("company",x);local p=(m.kind=="road" and v.roads or v.stations)[m.key];local g=p and p.grants[tostring(x)]
					set("use",g and g.use or false);set("connectGrant",g and g.connect or false);set("edit",g and g.edit or false)
				end)
				if m.company and m.company~=0 then
					rows[#rows+1]=toggle("use",T("Utiliser","Use"))
					if m.kind=="road" then rows[#rows+1]=row{toggle("connectGrant",T("Raccorder","Connect")),toggle("edit",T("Reconstruire et supprimer","Rebuild and delete"))}end
				end
				rows[#rows+1]=button(T("Enregistrer les règles","Save rules"),function()
					command{op="policy",key=m.key,kind=m.kind,label=selected.label,mode=m.mode,fee=tonumber(m.fee),connect=m.connect,
						company=m.company~=0 and m.company or nil,use=m.use,connectGrant=m.connectGrant,edit=m.edit}
				end)
			end
		elseif m.tab=="orders" then
			rows[#rows+1]=row{text(T("Titre","Title")),input(m.title,function(x)set("title",x)end)}
			local targets={};for _,t in ipairs(v.targets or {})do targets[#targets+1]={id=t.key,name=t.label}end
			rows[#rows+1]=row{text(T("Destination","Destination")),combo(m.target,targets,function(x)set("target",x)end)}
			local cargos={};for _,c in ipairs(v.cargos or {})do cargos[#cargos+1]={id=c.id,name=c.name}end
			rows[#rows+1]=row{text(T("Cargaison","Cargo")),combo(m.cargo,cargos,function(x)set("cargo",x)end)}
			rows[#rows+1]=row{text(T("Quantité","Quantity")),input(m.amount,function(x)set("amount",x)end,7),text(T("Récompense","Reward")),input(m.reward,function(x)set("reward",x)end,11),text(T("Mois","Months")),input(m.duration,function(x)set("duration",x)end,3)}
			rows[#rows+1]=button(T("Créer et réserver la récompense","Create and reserve reward"),function()command{op="create",title=m.title,target=m.target,cargo=tonumber(m.cargo),amount=tonumber(m.amount),reward=tonumber(m.reward),duration=tonumber(m.duration)}end)
			rows[#rows+1]=text(T("Une entreprise transporteuse par commande ; livraisons réelles à destination.","One carrier per order; actual deliveries at the destination."))
			local count=#v.orders;local pages=math.max(1,math.ceil(count/5));local page=math.min(m.page,pages)
			rows[#rows+1]=row{button("‹",function()set("page",math.max(1,page-1))end),text(page.." / "..pages),button("›",function()set("page",math.min(pages,page+1))end)}
			for i=(page-1)*5+1,math.min(count,page*5) do
				local o=v.orders[i];local fields={text(o.title.." · "..name(o.issuer).." → "..(o.targetLabel or o.target).." · "..o.progress.."/"..o.amount.." · "..o.reward.." · "..(states[o.status] or o.status))}
				if o.status=="open" and o.issuer~=U.myCo then fields[#fields+1]=button(T("Accepter","Accept"),function()command{op="accept",id=o.id}end)end
				if o.status=="open" and o.issuer==U.myCo or o.status=="active" and o.carrier==U.myCo then fields[#fields+1]=button(T("Annuler","Cancel"),function()command{op="cancel",id=o.id}end)end
				rows[#rows+1]=row(fields)
			end
		else
			local purposes={road=T("Péage routier","Road toll"),station=T("Usage d’arrêt","Stop usage"),escrow=T("Récompense réservée","Reserved reward"),reward=T("Récompense versée","Reward payment"),refund=T("Remboursement","Refund"),maintenance=T("Entretien des routes","Road maintenance")}
			local statuses={new=T("En attente","Pending"),submitted=T("En attente","Pending"),done=T("Payé","Paid"),blocked=T("Échec du paiement","Payment failed"),uncertain=T("À vérifier dans le journal","Check in the journal")}
			for i=math.max(1,#v.payments-11),#v.payments do local p=v.payments[i];rows[#rows+1]=text((purposes[p.purpose] or "").." · "..p.amount.." · "..(statuses[p.status] or ""))end
		end
		local last=v.notices and v.notices[#v.notices]
		if U.businessMessage=="invalid"then rows[#rows+1]=text(T("Refusé : vérifiez les droits, les valeurs et le solde.","Refused: check permissions, values and balance."))
		elseif U.businessMessage=="pending"then rows[#rows+1]=text(T("Modification en cours pour tous les joueurs…", "Applying for every player…"))
		elseif last and last.code=="request_failed"then rows[#rows+1]=text(T("Refusé : vérifiez les droits, les valeurs et le solde.","Refused: check permissions, values and balance."))
		elseif last and last.code=="payment_failed"then rows[#rows+1]=text(T("Échec du paiement","Payment failed"))
		elseif last then rows[#rows+1]=text(T("Accord mis à jour.","Agreement updated."))end
		return builtin.Window{title="MPFever · "..T("Entreprises et accords","Companies and agreements"),id="window.mpfever.business",movable=true,closable=true,onClose=wp.onClose,
			content=builtin.BoxLayout{orientation=W,children=rows}}
	end)
	U.openBusiness=function()
		local apiWindow=windowApi();if not apiWindow then return false end
		apiWindow.addSingletonWindow(Window,{onClose=function()local a=windowApi();if a then a.removeAllWindows(Window)end end});return true
	end
end
return V
