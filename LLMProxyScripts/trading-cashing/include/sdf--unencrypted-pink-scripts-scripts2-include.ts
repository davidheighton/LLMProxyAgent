:SDF
sys_check
  
setVar $GamePrefs~Bank "SDF"
setVar $GamePrefs~Animation[$GamePrefs~Bank] OFF
setVar $GamePrefs~AbortDisplayAll[$GamePrefs~Bank] OFF
setVar $GamePrefs~ScreenPauses[$GamePrefs~Bank] OFF
gosub :GamePrefs~SetGamePrefs
  
gosub :PlayerInfo~InfoQuick
  
setVar $trigger[1] $ThisTrigger
setVar $trigger[2] $OtherTrigger
setVar $damage ($PlayerInfo~Fighters + $PlayerInfo~Shields)
setVar $StealDump~Experience $PlayerInfo~Experience
setVar $firstRun 1
setVar $sector 1
setVar $sector[1] $PlayerInfo~Sector
setVar $sector[2] "0"
setVar $planet[1] $ThisPlanet
setVar $planet[2] $OtherPlanet
setVar $haggle[1] $ThisHaggleFactor
setVar $haggle[2] $OtherHaggleFactor
setVar $StealDump~ReInit[1] 1
setVar $StealDump~ReInit[2] 1
  
if ($damage <= $DamageLimit)
setVar $HoldsLost 0
setVar $Sector $sector[$sector]
setVar $Result 1
return
end
  
:cycle
  
if ($sector[$sector] = 0)
send "d"
setTextLineTrigger getSector :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $sector[$sector] 3
end
  
setVar $StealDump~Sector $sector[$sector]
setVar $StealDump~HaggleFactor $haggle[$sector]
setVar $StealDump~Planet $planet[$sector]
gosub :StealDump~StealDump
  
if ($StealDump~Success = "0")
setVar $HoldsLost $StealDump~HoldsLost
setVar $Sector $sector[$sector]
setVar $Result 2
return
elseif ($StealDump~Success = "2")
setVar $HoldsLost 0
setVar $Sector $sector[$sector]
setVar $Result 3
return
end
  
if ($StealDump~Turns < ($TurnLimit + 3))
setVar $HoldsLost 0
setVar $Sector $sector[$sector]
setVar $Result 0
return
end
  
if ($trigger[$sector] <> "")
send "'" $trigger[$sector] "*"
end
  
waitOn "is powering up weapons"
setTextLineTrigger lostShield :lostShield " shield points and "
setTextLineTrigger lostFigs :lostFigs " fighters."
setTextLineTrigger fled :fled "You fled from"
pause
  
:lostShield
getWordPos CURRENTLINE $pos " destroyed "
cutText CURRENTLINE $line $pos 999
getWord $line $lostShield 2
getWord $line $lostFigs 6
pause
  
:lostFigs
setVar $lostShield 0
getWordPos CURRENTLINE $pos " destroyed "
cutText CURRENTLINE $line $pos 999
getWord $line $lostFigs 2
pause
  
:fled
killTrigger lostShield
killTrigger lostFigs
  
if ($sector = "1")
setVar $sector "2"
else
setVar $sector "1"
end  
  
subtract $damage ($lostShield + $lostFigs)
  
if ($damage <= $DamageLimit)
setVar $HoldsLost 0
setVar $Sector $sector[$sector]
setVar $Result 1
return
end
  
goto :cycle
include "..\includes\aliases\pink-scripts-scripts2-include\stealDump.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\playerInfo.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\gamePrefs.ts"
