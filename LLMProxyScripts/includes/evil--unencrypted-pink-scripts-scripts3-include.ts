:Evil
sys_check
  
setVar $GamePrefs~Bank "Evil"
setVar $GamePrefs~Animation[$GamePrefs~Bank] OFF
setVar $GamePrefs~PageMessages[$GamePrefs~Bank] OFF
setVar $GamePrefs~AbortDisplayAll[$GamePrefs~Bank] OFF
setVar $GamePrefs~ScreenPauses[$GamePrefs~Bank] OFF
gosub :GamePrefs~SetGamePrefs
  
setVar $Bust 0
  
setVar $i 1
getWord $ShipSetup $set $i
getLength $set $len
while (($set <> 0) and ($len > 1))
cutText $set $op 1 1
cutText $set $id 2 $len
    
setVar $op[$i] $op
setVar $ship[$i] $id
    
add $i 1
getWord $shipSetup $set $i
end
  
setVar $shipCount ($i - 1)
  
if ($shipCount < 2)
return
end
  
send "czq"
waitOn "---------------------------"
  
:nextLine
setTextLineTrigger getShipLine :getShipLine
pause
  
:getShipLine
getWord CURRENTLINE $id 1
if ($id > 0)
setVar $line CURRENTLINE
replaceText $line "+" " "
    
setVar $i 1
while (($i <= $shipCount) and ($ship[$i] <> $id))
add $i 1
end
    
if ($i <= $shipCount)
getWord $line $sect[$i] 2
end
    
goto :nextLine
end
  
setVar $i 1
while ($i <= $shipCount)
if ($sect[$i] = 0)
setVar $Failed 1
return
end
add $i 1
end
  
gosub :PlayerInfo~InfoQuick
  
setVar $startShip $PlayerInfo~Ship
setVar $experience $PlayerInfo~Experience
setVar $ship 1
while ($ship[$ship] > 0)
if ($ship[$ship] = $startShip)
goto :run
end
add $ship 1
end
setVar $ship 1
  
if ($PlayerInfo~Sector <= 10) or (PORT.CLASS[$PlayerInfo~Sector] = 9)
send "x*" $ship[$ship] "**"
else
send "x" $ship[$ship] "**"
end
:run
  
setVar $fromSect $sect[$ship]
if ($op[$ship] = "D")
setVar $StealDump~Experience $experience
setVar $StealDump~StealFactor $StealFactor
setVar $StealDump~Sector $sect[$ship]
gosub :StealDump~StealDump
setVar $success $StealDump~Success
setVar $holdsLost $StealDump~HoldsLost
setVar $experience $StealDump~Experience
setVar $turns $StealDump~Turns
elseif ($op[$ship] = "S")
setVar $SellSteal~Experience $experience
setVar $SellSteal~StealFactor $StealFactor
setVar $SellSteal~Sector $sect[$ship]
gosub :SellSteal~SellSteal
setVar $success $SellSteal~Success
setVar $holdsLost $SellSteal~HoldsLost
setVar $experience $SellSteal~Experience
setVar $turns $SellSteal~Turns
elseif ($op[$ship] = "R")
setVar $Rob~Experience $experience
setVar $Rob~RobFactor $RobFactor
gosub :Rob~Rob
setVar $success $Rob~Success
setVar $holdsLost $Rob~HoldsLost
setVar $experience $Rob~Experience
end
if ($success = 0)
if ($Broadcast)
if ($op[$ship] = "D")
setVar $op "SDT"
elseif ($op[$ship] = "S")   
setVar $op "SST"
elseif ($op[$ship] = "R")
setVar $op "RTR"
end
    
send "'Busted in ship " $ship[$ship] " (" $op "), holds lost: " $holdsLost "*"
end
if ($Continue)
gosub :sub_ShuffleList
else
halt
end
elseif ($success = 2)
if ($method[$ship] = RTR)
gosub :sub_ShuffleList    
else
clientMessage "Insufficient holds available for SDT"
halt
end
else
add $ship 1
end
if ($ship > $shipCount)
setVar $ship 1
end
  
if ($fromSect <= 10) or (PORT.CLASS[$fromSect] = 9) 
send "x* " $ship[$ship] "* *"
else
send "x " $ship[$ship] "* *"
end
  
goto :run
:sub_ShuffleList
setVar $x1 $ship
setVar $j1 $ship
add $j1 1
:nextShip1
if ($x1 <= $shipCount)
setVar $ship[$x1] $ship[$j1]
setVar $op[$x1] $op[$j1]
setVar $sect[$x1] $sect[$j1]
add $x1 1
add $j1 1
goto :nextShip1
end
  
subtract $shipCount 1
if ($shipCount <= 1)
halt
end
  
return
include "..\includes\aliases\pink-scripts-scripts3-include\playerInfo.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\sellSteal.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\stealDump.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\rob.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\gamePrefs.ts"
