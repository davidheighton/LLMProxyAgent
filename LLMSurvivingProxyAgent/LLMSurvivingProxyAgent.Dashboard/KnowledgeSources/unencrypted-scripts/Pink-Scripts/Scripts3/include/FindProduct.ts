:FindProduct
sys_check
  
if ($Sector = 0)
send "d"
setTextLineTrigger getSector :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $Sector 3
end
setVar $Location 0
  
if (SECTOR.PLANETCOUNT[$Sector] > 0)
setVar $PlanetCheck~IgnoreList $IgnoreList
setVar $PlanetCheck~PlanetCheckSub :FindProduct~checkProduct    
gosub :PlanetCheck~PlanetCheck
    
if ($PlanetCheck~Found > 0)
setVar $Location $PlanetCheck~Found
      
if ($StayOnPlanet = 0)
send "q"
end
      
return
end
end
  
if (PORT.CLASS[$Sector] > 0) and (PORT.CLASS[$Sector] < 8)
if ((PORT.BUYFUEL[$Sector] = 0) and ($Product = 1)) or ((PORT.BUYORG[$Sector] = 0) and ($Product = 2)) or ((PORT.BUYEQUIP[$Sector] = 0) and ($Product = 3))
send "cr*q"
      
waitOn "Commerce report for "
      
if ($Product = 1)
setTextLineTrigger getProduct :getProduct "Fuel Ore   "
elseif ($Product = 2)
setTextLineTrigger getProduct :getProduct "Organics   "
else
setTextLineTrigger getProduct :getProduct "Equipment  "
end
pause
      
:getProduct
if ($Product = 1)
getWord CURRENTLINE $ProdAmount 4
else
getWord CURRENTLINE $ProdAmount 3
end
      
if ($ProdAmount >= $Quantity)
setVar $Location "P"
end
end
end
  
return
  
  
:checkProduct
setVar $PlanetInfo~NoHeader 1
gosub :PlanetInfo~PlanetInfo
  
if ($Product = "C")
    
setVar $i 1
while ($i <= 3)
if ($PlanetInfo~Colo[$i] >= $Quantity)
setVar $Category $i
setVar $PlanetCheck~Found 1
return
end
add $i 1
end
else
if ($PlanetInfo~Amount[$Product] >= $Quantity)
setVar $PlanetCheck~Found 1
end
end
  
if ($PlanetCheck~Found = 0)
setVar $IgnoreList ($IgnoreList & " " & $PlanetCheck~Check_Planet)
end
  
return
  
include "include\planetCheck"
include "include\planetInfo"
