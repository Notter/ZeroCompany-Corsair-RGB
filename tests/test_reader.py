"""Exercise the actual Lua reader, replacing engine and file/process side effects."""
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).parent / 'vendor'))
from lupa.lua54 import LuaRuntime

lua = LuaRuntime(unpack_returned_tuples=True)
lua.execute(r'''
written = ""
searches = {}; attributePasses=0
logs = {}
files = {}; diagnosticWrites = 0
print = function(s) logs[#logs+1] = s end
os.execute = function(s) launch = s; return true, "exit", 0 end
io.open = function(path, mode)
    if mode=="w" then files[path]="" end
    return {write=function(self,s)
        written=s;files[path]=(files[path] or "")..s
        if path:find("character-diagnostics.log",1,true) then diagnosticWrites=diagnosticWrites+1 end
    end, close=function() end}
end
LoopAsync = function(ms,fn) assert(ms==250); tick=fn end
ExecuteInGameThread = function(fn) fn() end
local function object(t)
    t.IsValid = function() return true end
    t.GetFullName = function() return t.name or "object" end
    return t
end
local function attribute(class,props)
    props.GetClass=function() return {GetFName=function() return {ToString=function() return class end} end} end
    return object(props)
end
health = attribute("BitReactorHealthSet",{Health={CurrentValue=42},MaxHealth={CurrentValue=100}})
combat = attribute("BitReactorCombatSet",{ActionPoints={CurrentValue=3}})
asc=object({name="asc",AvatarActor=object({name="unit"}),SpawnedAttributes={ForEach=function(self,fn)
    fn(1,{get=function()return health end});fn(2,{get=function()return combat end})
end}})
vm=object({ActiveASC=asc})
team="BitReactor.Team.Player"
state=object({name="world",CurrentTurnTeamTag={TagName={ToString=function() return team end}}})
states={state};vms={vm};selectors={};systems={asc};abilityVMs={}
FindAllOf=function(class)
    searches[class]=(searches[class] or 0)+1
    if class=="TacticalGameState" then return states end
    if class=="BrunoActiveCharacterViewModel" then return vms end
    if class=="SelectedCharacterComponent" then return selectors end
    if class=="AbilitySystemComponent" then return systems end
    if class=="BitReactorHealthSet" then return {health} end
    if class=="BitReactorCombatSet" then return {combat} end
    if class=="BrunoSelectableGameplayAbilityViewModel" then return abilityVMs end
    error("Unexpected lookup "..class)
end
''')
source = (Path(__file__).parents[1] / 'mod/Scripts/main.lua').read_text()
lua.execute(source, name="@C:/Game/ue4ss/Mods/ZeroCompanyRGB/Scripts/main.lua")
assert "-WindowStyle Hidden" in lua.globals().launch
count = 1

def sample(setup=''):
    lua.execute(setup)
    # Four samples cross the discovery refresh boundary; gameplay values still
    # update every single tick, tested separately below.
    for _ in range(4): lua.globals().tick()
    return dict(row.split('=',1) for row in lua.globals().written.splitlines())

s=sample();assert s['hp']=='42' and s['maxhp']=='100' and s['ap']=='3';count+=1
assert s['end']==s['sequence'] and s['unit']=='unit';count+=1
s=sample('team="BitReactor.Team.Enemy"');assert s['team']=="BitReactor.Team.Enemy" and s['hp']=='-1';count+=1
s=sample('team="BitReactor.Team.Player";vms={}');assert s['unit']=='' and s['hp']=='-1';count+=1
s=sample('vms={vm};states={state,state}');assert s['scene']=='ambiguous' and s['unit']=='';count+=1
s=sample('states={}');assert s['team']=='None';count+=1
s=sample('states={state};health.Health=nil');assert s['hp']=='-1' and s['team']=='BitReactor.Team.Player';count+=1
previous_sequence=int(s['sequence'])
s=sample();assert int(s['sequence'])==previous_sequence+4;count+=1
s=sample('health.Health={CurrentValue=42};vms={};selector={IsValid=function() return true end,SelectedCharacter=asc.AvatarActor};selectors={selector}')
assert s['unit']=='unit' and s['hp']=='42' and s['ap']=='3';count+=1
s=sample('selector.SelectedCharacter=nil;vms={vm}')
assert s['unit']=='' and s['ap']=='-1';count+=1
s=sample('selector.SelectedCharacter=asc.AvatarActor;systems={}')
assert s['unit']=='';count+=1
s=sample('systems={asc};asc.SpawnedAttributes.ForEach=function(self,fn) fn(1,health);fn(2,combat) end')
assert s['hp']=='42' and s['ap']=='3';count+=1
s=sample('health.Health=nil')
assert s['hp']=='-1' and s['ap']=='3';count+=1
s=sample('selectors={selector,{IsValid=function() return true end,SelectedCharacter={IsValid=function() return true end,GetFullName=function() return "other" end}}}')
assert s['unit']=='';count+=1
lua.execute('ExecuteInGameThread=function(fn) pending_callback=fn end')
before=s['sequence'];lua.globals().tick();lua.globals().tick()
assert dict(row.split('=',1) for row in lua.globals().written.splitlines())['sequence']==before
lua.globals().pending_callback();count+=1
lua.execute('ExecuteInGameThread=function(fn) fn() end')
s=sample('selectors={selector};health.Health={CurrentValue=28};selectedTarget=asc.AvatarActor;selector.SelectedCharacter={get=function() return selectedTarget end};asc.AvatarActor.AbilitySystemComponent=asc;systems={}')
assert s['unit']=='unit' and s['hp']=='28' and s['ap']=='3';count+=1
s=sample('selectedTarget=nil')
assert s['unit']=='' and s['hp']=='-1';count+=1
s=sample('selectedTarget={IsValid=function() return false end}')
assert s['unit']=='';count+=1
s=sample('selectedTarget=asc.AvatarActor')
assert s['unit']=='unit' and s['ap']=='3';count+=1
lua.execute('for i=1,150 do tick() end')
assert lua.globals().diagnosticWrites==0;count+=1
lua.execute('''
advantageSet={IsValid=function()return true end,
GetClass=function()return {GetFName=function()return {ToString=function()return "BitReactorAdvantageSet" end}end}end,
Advantage={CurrentValue=50},MaximumAdvantage={CurrentValue=100}}
asc.SpawnedAttributes.ForEach=function(self,fn)fn(1,health);fn(2,combat);fn(3,advantageSet)end
squadArray={ForEach=function(self,fn)fn(1,{get=function()return asc.AvatarActor end})end}
state.PlayerSquad={IsValid=function()return true end,SquadMembers=squadArray}
abilityKind="BitReactor.Ability.Type.Advantage"
abilityInfo={IsValid=function()return true end,OwnerActor=asc.AvatarActor,
AbilityType={TagName={ToString=function()return abilityKind end}}}
abilityVM={IsValid=function()return true end,GameplayAbilityVM=abilityInfo,bCanAbilityActivate=true}
abilityVMs={abilityVM}
''')
s=sample();assert s['advantage']=='50' and s['maxadvantage']=='100' and s['advready']=='1';count+=1
assert s['squadcount']=='1' and s['squadcomplete']=='1' and s['member1'].startswith('unit|28|');count+=1
s=sample('abilityVM.bCanAbilityActivate=false');assert s['advready']=='0';count+=1
s=sample('abilityKind="BitReactor.Ability.Type.Standard";abilityVM.bCanAbilityActivate=true');assert s['advready']=='-1';count+=1
s=sample('team="BitReactor.Team.Enemy";health.Health.CurrentValue=15');assert s['member1'].startswith('unit|15|') and s['unit']=='';count+=1
s=sample('squadArray.ForEach=function(self,fn)fn(1,{get=function()return nil end})end');assert s['squadcomplete']=='0';count+=1
s=sample('state.PlayerSquad=nil');assert s['squadcomplete']=='0' and s['squadcount']=='0';count+=1
lua.execute('''
team="BitReactor.Team.Player";state.PlayerSquad={IsValid=function()return true end,SquadMembers=squadArray}
squadArray.ForEach=function(self,fn)fn(1,asc.AvatarActor)end
asc.SpawnedAttributes.ForEach=function(self,fn)attributePasses=attributePasses+1;fn(1,health);fn(2,combat);fn(3,advantageSet)end
searches={};attributePasses=0
for i=1,40 do tick() end
''')
assert lua.globals().searches['TacticalGameState']==40;count+=1
assert lua.globals().searches['SelectedCharacterComponent']==10;count+=1
assert lua.globals().searches['BrunoSelectableGameplayAbilityViewModel']==10;count+=1
assert lua.globals().attributePasses==40;count+=1
lua.execute('combat.ActionPoints.CurrentValue=0;tick()')
latest=dict(row.split('=',1) for row in lua.globals().written.splitlines())
assert latest['ap']=='0';count+=1
before_searches=lua.globals().searches['SelectedCharacterComponent']
lua.execute('state.name="new world";tick()')
assert lua.globals().searches['SelectedCharacterComponent']==before_searches+1;count+=1
before_searches=lua.globals().searches['SelectedCharacterComponent']
lua.execute('selector.IsValid=function()return false end;selectors={};tick()')
assert lua.globals().searches['SelectedCharacterComponent']==before_searches+1;count+=1
print('40-sample workload: 60 global searches (previously 120); 40 attribute passes (previously 80).')
print(f'{count} Lua reader checks passed (Lua 5.4).')
