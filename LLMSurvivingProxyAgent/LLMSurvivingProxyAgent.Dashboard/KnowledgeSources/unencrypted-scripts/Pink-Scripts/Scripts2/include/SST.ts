:SST
sys_check
  
setVar $GamePrefs~Bank "SST"
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
setVar $SellSteal~Experience $PlayerInfo~Experience
setVar $firstRun 1
setVar $sector 1
setVar $sector[1] $PlayerInfo~Sector
setVar $sector[2] $otherShipSector
setVar $SellSteal~ShipHolds[1] 0
setVar $SellSteal~ShipHolds[2] 0
  
:cycle
setVar $SellSteal~Sector $sector[$sector]
setVar $Haggle~Sector $sector[$sector]
setVar $SellSteal~PortClass PORT.CLASS[$sector[$sector]]
gosub :SellSteal~SellSteal
  
if ($SellSteal~Success = "0")
setVar $Result 1
setVar $HoldsLost $SellSteal~HoldsLost
setVar $Ship $ship[$sector]
setVar $Sector $sector[$sector]
return
elseif ($SellSteal~Success = "2")
setVar $Result 2
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
  
if ($sector = "1")
setVar $sector "2"
else
setVar $sector "1"
end
  
send "x " $ship[$sector] "* *"
  
goto :cycle
include "include\sellSteal"  
include "include\playerInfo"
include "include\SST"
