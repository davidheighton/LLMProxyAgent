:SSF
sys_check
  
gosub :PlayerInfo~InfoQuick
  
setVar $trigger[1] $ThisTrigger
setVar $trigger[2] $OtherTrigger
setVar $damage ($PlayerInfo~Fighters + $PlayerInfo~Shields)
setVar $SellSteal~Experience $PlayerInfo~Experience
setVar $firstRun 1
setVar $sector 1
setVar $sector[1] $PlayerInfo~Sector
setVar $sector[2] "0"
setVar $planet[1] $ThisPlanet
setVar $planet[2] $OtherPlanet
setVar $SellSteal~ShipHolds[1] 0
setVar $SellSteal~ShipHolds[2] 0
  
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
  
setVar $SellSteal~Sector $sector[$sector]
setVar $Haggle~Sector $sector[$sector]
setVar $SellSteal~PortClass PORT.CLASS[$sector[$sector]]
gosub :SellSteal~SellSteal
  
if ($SellSteal~Success = "0")
setVar $Result 2
setVar $HoldsLost $SellSteal~HoldsLost
setVar $Ship $ship[$sector]
setVar $Sector $sector[$sector]
return
elseif ($SellSteal~Success = "2")
setVar $Result 3
setVar $HoldsLost 0
setVar $Ship $ship[$sector]
setVar $Sector $sector[$sector]
return
end
  
if ($SellSteal~Turns <= ($TurnLimit + 3))
setVar $Result 0
setVar $HoldsLost 0
setVar $Ship $ship[$sector]
setVar $Sector $sector[$sector]
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
include "..\includes\aliases\pink-scripts-scripts3-include\sellSteal.ts"  
include "..\includes\aliases\pink-scripts-scripts3-include\playerInfo.ts"
