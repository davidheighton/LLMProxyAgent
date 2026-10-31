:PlanetUpgrade
sys_check
  
setVar $Failed 0
setVar $PlanetInfo~NoHeader 1
gosub :PlanetInfo~PlanetInfo
setVar $level $PlanetInfo~CitadelLevel
setVar $destCategory $PlanetInfo~DropCategory
  
if (($PlanetInfo~BuildTime > 0) or ($level = 6))
return
end
  
if ($level = 0)
send "cy"
else
send "cuyq"
end
  
setTextLineTrigger cantUpgrade :cantUpgrade "This Citadel cannot be upgraded further."
setTextLineTrigger canUpgrade :canUpgrade "the following:"
pause
  
:cantUpgrade
killTrigger canUpgrade
return
  
:canUpgrade
killTrigger cantUpgrade
setTextLineTrigger getColos :getColos "Colonists to support the construction,"
setTextLineTrigger getFuel :getFuel "units of Fuel Ore,"
setTextLineTrigger getOrg :getOrg "units of Organics,"
setTextLineTrigger getEquip :getEquip "units of Equipment and"
setTextLineTrigger getDays :getDays "days to construct."
pause
  
:getColos
getWord CURRENTLINE $colosNeeded 1
pause
  
:getFuel
getWord CURRENTLINE $fuelNeeded 1
pause
  
:getOrg
getWord CURRENTLINE $orgNeeded 1
pause
  
:getEquip
getWord CURRENTLINE $equipNeeded 1
pause
  
:getDays
getWord CURRENTLINE $daysNeeded 1
stripText $colosNeeded ","
stripText $fuelNeeded ","
stripText $orgNeeded ","
stripText $equipNeeded ","
divide $colosNeeded 1000
setVar $totalColos ($PlanetInfo~Colo[1] + $PlanetInfo~Colo[2] + $PlanetInfo~Colo[3])
subtract $colosNeeded $totalColos
subtract $fuelNeeded $PlanetInfo~Amount[1]
subtract $orgNeeded $PlanetInfo~Amount[2]
subtract $equipNeeded $PlanetInfo~Amount[3]
  
if ($colosNeeded <= 0) and ($fuelNeeded <= 0) and ($orgNeeded <= 0) and ($equipNeeded <= 0)
if ($daysNeeded = 0)
      
setDelayTrigger upgradePause :upgradePause 1000
pause
      
:upgradePause
send "d"
goto :PlanetUpgrade
end
    
return
end
  
send "q"
 
if ($fuelNeeded > 0)
setVar $Gather~Sector $Sector
setVar $Gather~Product 1
setVar $Gather~StayOnPlanet 0
setVar $Gather~IgnoreList $PlanetID
setVar $Gather~PlanetID $PlanetID
setVar $Gather~Quantity $fuelNeeded
setVar $Gather~Seek $Seek
gosub :Gather~Gather
    
if ($Gather~Failed)
setVar $Failed 1
return
end
end
  
if ($orgNeeded > 0)
setVar $Gather~Sector $Sector
setVar $Gather~Product 2
setVar $Gather~StayOnPlanet 0
setVar $Gather~IgnoreList $PlanetID
setVar $Gather~PlanetID $PlanetID
setVar $Gather~Quantity $orgNeeded
setVar $Gather~Seek $Seek
gosub :Gather~Gather
    
if ($Gather~Failed)
setVar $Failed 1
return
end
end
  
if ($equipNeeded > 0)
setVar $Gather~Sector $Sector
setVar $Gather~Product 3
setVar $Gather~StayOnPlanet 0
setVar $Gather~IgnoreList $PlanetID
setVar $Gather~PlanetID $PlanetID
setVar $Gather~Quantity $equipNeeded
setVar $Gather~Seek $Seek
gosub :Gather~Gather
    
if ($Gather~Failed)
setVar $Failed 1
return
end
end
  
if ($colosNeeded > 0)
setVar $Gather~Sector $Sector
setVar $Gather~Product C
setVar $Gather~StayOnPlanet 0
setVar $Gather~IgnoreList $PlanetID
setVar $Gather~Quantity $colosNeeded
setVar $Gather~PlanetID $PlanetID
setVar $Gather~DestCategory $destCategory
setVar $Gather~Seek $Seek
gosub :Gather~Gather
    
if ($Gather~Failed)
setVar $Failed 1
return
end
end
  
send "l" $PlanetID "*"
goto :PlanetUpgrade
  
  
include "include\planetInfo"
include "include\gather"
