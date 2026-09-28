-- Corsair RGB Keyboard Integration - Zero Company 1.0.0
-- Reads reflected state on the game thread; never writes game properties.
local source = debug.getinfo(1, "S").source:gsub("^@", "")
local root = source:match("^(.*)[/\\]Scripts[/\\]main.lua$")
if not root then error("Cannot resolve Corsair mod folder: " .. source) end
local function log(s) print("[ZeroCompanyRGB] " .. s .. "\n") end
local errors = {}
local function warn(key, message)
    if not errors[key] then errors[key] = true; log(message) end
end
local function valid(obj)
    if obj == nil then return false end
    local ok, result = pcall(function() return obj:IsValid() end)
    return ok and result
end
local function clean(s) return tostring(s or ""):gsub("[\r\n]", " ") end
local sequence, pending = 0, false
local session = tostring(os.time()) .. tostring({}):gsub("[^%w]", "")
local lastSummary = ""
local sampleIndex, cachedScene = 0, nil
local discovery, attributeReadings = {}, {}
-- Cache object discovery for four samples, not their changing gameplay values.
-- Mission changes flush everything; expired objects force immediate rediscovery.
local function discover(class)
    local entry = discovery[class]
    if entry then
        for _, item in ipairs(entry.items) do
            if not valid(item) then entry=nil;break end
        end
    end
    if not entry or sampleIndex>=entry.expires then
        entry={items=FindAllOf(class) or {},expires=sampleIndex+4}
        discovery[class]=entry
    end
    return entry.items
end
local function field(obj, key)
    local ok, value = pcall(function() return obj[key] end)
    if ok then return value end
    return nil
end
local diagnostics = 0
local function diagnose(reason)
    if diagnostics < 24 and not errors[reason] then
        diagnostics = diagnostics + 1
        warn(reason, "Character reader: " .. reason)
    end
end
local cachedASC
local function object(value)
    if valid(value) then return value end
    local ok, resolved = pcall(function() return value:get() end)
    if ok and valid(resolved) then return resolved end
    return nil
end
local function attributes(active, unit)
    local key = active:GetFullName()
    if attributeReadings[key] then return attributeReadings[key] end
    local result = {unit=unit or "", hp=-1, maxhp=-1, ap=-1, advantage=-1, maxadvantage=-1}
    local list = field(active, "SpawnedAttributes")
    if not list then return result end
    list:ForEach(function(_, element)
        local attribute = object(element)
        if attribute then
            local class = attribute:GetClass():GetFName():ToString()
            local function number(key) return tonumber(field(field(attribute, key), "CurrentValue")) or -1 end
            if class == "BitReactorHealthSet" then
                result.hp, result.maxhp = number("Health"), number("MaxHealth")
            elseif class == "BitReactorCombatSet" then result.ap = number("ActionPoints")
            elseif class == "BitReactorAdvantageSet" then
                result.advantage, result.maxadvantage = number("Advantage"), number("MaximumAdvantage")
            end
        end
    end)
    attributeReadings[key]=result
    return result
end
local function squadResources(state)
    local squad = object(field(state, "PlayerSquad"))
    local members = field(squad, "SquadMembers")
    if not members then diagnose("PlayerSquad/SquadMembers unavailable; squad feedback and Space cue suppressed");return {}, false end
    local result, complete, seen = {}, true, {}
    members:ForEach(function(_, element)
        local actor = object(element)
        if not actor then complete=false;return end
        local id = actor:GetFullName()
        if seen[id] then return end
        seen[id]=true
        if #result >= 32 then complete=false;return end
        local asc = object(field(actor, "AbilitySystemComponent"))
        if not asc then complete=false;return end
        local ok, r = pcall(attributes, asc, id)
        if ok then result[#result+1]=r else complete=false end
    end)
    return result, complete and #result>0
end
-- Read readiness from the ability UI's actual activation flag, never infer it
-- just from a full Advantage meter. Only accept explicitly Advantage-tagged abilities.
local function advantageReady(unit)
    local ready, found = false, false
    local function tag(value)
        local ok, text = pcall(function() return value.TagName:ToString() end)
        return ok and text or ""
    end
    for _, vm in ipairs(discover("BrunoSelectableGameplayAbilityViewModel")) do
        if valid(vm) then
            local info = object(field(vm, "GameplayAbilityVM"))
            local owner = object(field(info, "OwnerActor"))
            if owner and owner:GetFullName()==unit then
                local kind = tag(field(info, "AbilityType"))
                local advantage = false
                for segment in kind:lower():gmatch("[^%.]+") do
                    if segment=="advantage" or segment=="adv" then advantage=true end
                end
                if advantage then
                    local can = field(vm, "bCanAbilityActivate")
                    if type(can)=="boolean" then found=true;ready=ready or can end
                end
            end
        end
    end
    if not found then return -1 end
    return ready and 1 or 0
end
local function resources()
    -- Prefer the gameplay selection component over potentially inactive UI models.
    local selected, selectedId
    local selectors = discover("SelectedCharacterComponent")
    for _, selector in ipairs(selectors) do
        if valid(selector) then
            local actor = field(selector, "SelectedCharacter")
            -- Live UE4SS exposes this property as FWeakObjectPtr, not UObject.
            if not valid(actor) then
                local ok, resolved = pcall(function() return actor:get() end)
                actor = ok and resolved or nil
            end
            if valid(actor) then
                local id = actor:GetFullName()
                if selectedId and selectedId ~= id then
                    diagnose("Multiple selected characters; indicators suppressed")
                    return nil
                end
                selected, selectedId = actor, id
            end
        end
    end
    local active, identity
    if selected then
        local function matches(asc)
            if not valid(asc) then return false end
            local avatar = field(asc, "AvatarActor")
            return valid(avatar) and avatar:GetFullName() == selectedId
        end
        local direct = field(selected, "AbilitySystemComponent")
        if matches(direct) then active = direct
        elseif matches(cachedASC) then active = cachedASC
        else
            local components = discover("AbilitySystemComponent")
            for _, asc in ipairs(components) do
                if matches(asc) then
                    local id = asc:GetFullName()
                    if identity and identity ~= id then
                        diagnose("Multiple ability systems for selected character")
                        return nil
                    end
                    active, identity = asc, id
                end
            end
            if not active then diagnose("Selected character found, but no matching AvatarActor among " .. #components .. " ability systems") end
        end
    elseif #selectors > 0 then
        diagnose("Selection components found, but SelectedCharacter is empty")
        cachedASC = nil
        return nil
    else
        local models = discover("BrunoActiveCharacterViewModel")
        for _, vm in ipairs(models) do
            if valid(vm) then
                local asc = field(vm, "ActiveASC")
                if valid(asc) and valid(field(asc, "AvatarActor")) then
                    local id = asc:GetFullName()
                    if identity and identity ~= id then diagnose("Multiple active UI ability systems");return nil end
                    active, identity = asc, id
                end
            end
        end
        if not active then diagnose("No selection component; " .. #models .. " active-character UI models, none with usable ActiveASC/AvatarActor") end
    end
    cachedASC = active
    if not active then return nil end
    return attributes(active, active.AvatarActor:GetFullName())
end
local function sample()
    sampleIndex=sampleIndex+1
    attributeReadings={}
    local state, scene, team
    for _, obj in ipairs(FindAllOf("TacticalGameState") or {}) do
        if valid(obj) then
            if state then state=nil;scene="ambiguous";team="Unknown";break end
            state=obj
        end
    end
    local sceneId = state and state:GetFullName() or (scene or "")
    if sceneId~=cachedScene then
        discovery={};cachedASC=nil;cachedScene=sceneId
    end
    local hp, maxhp, ap, unit = -1,-1,-1,""
    local advantage, maxadvantage, ready = -1,-1,-1
    local members, complete = {}, false
    if state then
        scene=state:GetFullName()
        team=state.CurrentTurnTeamTag.TagName:ToString()
        if team=="BitReactor.Team.Player" or team=="BitReactor.Team.Enemy" or team=="BitReactor.Team.Civilian" then
            local ok, list, all = pcall(squadResources, state)
            if ok then members,complete=list,all else warn("squad", "Squad reader: " .. tostring(list)) end
        end
        if team == "BitReactor.Team.Player" then
            local ok, r = pcall(resources)
            if ok and r then
                hp,maxhp,ap,unit=r.hp,r.maxhp,r.ap,r.unit
                advantage,maxadvantage=r.advantage,r.maxadvantage
                local read, value = pcall(advantageReady, unit)
                if read then ready=value else warn("advready", "ADV readiness: " .. tostring(value)) end
            elseif not ok then warn("resources", "Optional unit indicators unavailable: "..tostring(r)) end
        end
    end
    scene,team=scene or "",team or "None"
    sequence=sequence+1
    local rows={"version=1","session="..session,"sequence="..sequence,"stamp="..os.time(),
        "scene="..clean(scene),"team="..clean(team),"unit="..clean(unit),
        "hp="..tostring(hp),"maxhp="..tostring(maxhp),"ap="..tostring(ap),
        "advantage="..advantage,"maxadvantage="..maxadvantage,"advready="..ready,
        "squadcount="..#members,"squadcomplete="..(complete and "1" or "0")}
    for i,r in ipairs(members) do
        rows[#rows+1]="member"..i.."="..clean(r.unit):gsub("|", "_").."|"..r.hp.."|"..r.maxhp.."|"..r.ap
    end
    rows[#rows+1]="end="..sequence
    -- Completion marker lets the reader reject partially written snapshots.
    local f,err=io.open(root.."/state.txt","w")
    if not f then error("Snapshot write failed: "..tostring(err)) end
    f:write(table.concat(rows,"\n").."\n");f:close()
    -- Keep normal release logs small; state.txt retains the latest detailed data.
    local summary=team.." | squad="..#members.." complete="..tostring(complete)
    if summary~=lastSummary then log(summary);lastSummary=summary end
end
-- The bridge is our bundled windowless application, not a game injection DLL.
-- PowerShell only launches that fixed executable; no game actions are issued.
local exe=root.."/ZeroCompanyRGB.exe"
if exe:find('["\r\n%%!]') then error("Unsupported characters in installation path") end
local quoted=exe:gsub("'", "''")
local launch='powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -Command "Start-Process -FilePath \''..quoted..'\' -WindowStyle Hidden"'
local launchOk,launchResult=pcall(function() return os.execute(launch) end)
if not launchOk or (launchResult~=true and launchResult~=0) then
    log("Bridge auto-start failed: "..tostring(launchResult)..". Start ZeroCompanyRGB.exe manually while the game is running.")
end
log("Reader loaded. 4 Hz; optional health/AP; bridge log is in this mod's folder.")
LoopAsync(250,function()
    if pending then return false end
    pending=true
    local ok,err=pcall(function()
        ExecuteInGameThread(function()
            local sampled,sampleError=pcall(sample)
            if not sampled then warn("sample", "State reader: "..tostring(sampleError)) end
            pending=false
        end)
    end)
    if not ok then pending=false;warn("schedule",tostring(err)) end
    return false
end)
