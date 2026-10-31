:SDT
sys_check
  
setVar $GamePrefs~Bank "SDT"
setVar $GamePrefs~Animation[$GamePrefs~Bank] OFF
setVar $GamePrefs~AbortDisplayAll[$GamePrefs~Bank] OFF
setVar $GamePrefs~ScreenPauses[$GamePrefs~Bank] OFF
gosub :GamePrefs~SetGamePrefs
  
send "czq"
waitOn "-----------------------------"
:getShipAgain
setTextLineTrigger getShipSector :getShipSector $OtherShip
pause
:getShipSector
getWord CURRENTLINE $test 1
if ($test <> $OtherShip)
goto :getShipAgain
end
getWord CURRENTLINE $otherShipSector 2
  
if ($otherShipSector > 0)
end
  
gosub :PlayerInfo~InfoQuick
  
setVar $ship[1] $PlayerInfo~Ship
setVar $ship[2] $OtherShip
setVar $StealDump~Experience $PlayerInfo~Experience
setVar $firstRun 1
setVar $sector 1
setVar $sector[1] $PlayerInfo~Sector
setVar $sector[2] $otherShipSector
setVar $planet[1] $ThisPlanet
setVar $planet[2] $OtherPlanet
setVar $haggle[1] $ThisHaggleFactor
setVar $haggle[2] $OtherHaggleFactor
setVar $StealDump~ReInit[1] 1
setVar $StealDump~ReInit[2] 1
  
:cycle
setVar $StealDump~Sector $sector[$sector]
setVar $StealDump~HaggleFactor $haggle[$sector]
setVar $StealDump~Planet $planet[$sector]
gosub :StealDump~StealDump
  
if ($StealDump~Success = "0")
setVar $HoldsLost $StealDump~HoldsLost
setVar $Ship $ship[$sector]
setVar $Sector $sector[$sector]
setVar $Result 1
return
elseif ($StealDump~Success = "2")
setVar $HoldsLost 0
setVar $Ship $ship[$sector]
setVar $Sector $sector[$sector]
setVar $Result 2
return
end
  
if ($StealDump~Turns < ($TurnLimit + 3))
setVar $HoldsLost 0
setVar $Ship $ship[$sector]
setVar $Sector $sector[$sector]
setVar $Result 0
return
end
  
if ($sector = "1")
setVar $sector "2"
else
setVar $sector "1"
end
  
send "x " $ship[$sector] "* *"
waitOn "<Q> Exit Transporter"
  
goto :cycle
include "include\stealDump"
include "include\playerInfo"
include "include\gamePrefs"
