:MassColonise
sys_check
  
setVar $Full 0
  
setVar $GamePrefs~Bank "WorldTrade"
setVar $GamePrefs~Animation[$GamePrefs~Bank] OFF
setVar $GamePrefs~AbortDisplayAll[$GamePrefs~Bank] OFF
setVar $GamePrefs~ScreenPauses[$GamePrefs~Bank] OFF
gosub :GamePrefs~SetGamePrefs
  
gosub :PlayerInfo~InfoQuick
setVar $holds $PlayerInfo~Holds
setVar $credits $PlayerInfo~Credits
setVar $align $PlayerInfo~Align
  
send "jy"
setVar $PlanetCheck~PlanetCheckSub :MassColonise~findPlanet
gosub :PlanetCheck~PlanetCheck
  
setVar $planet $PlanetCheck~Found
  
if ($planet = 0)
setVar $Full 1
return
end
  
send "q"
  
if ($align >= 1000)
setVar $FindProduct~Sector 0
setVar $FindProduct~Product 1
setVar $FindProduct~Quantity $holds
setVar $FindProduct~StayOnPlanet 0
gosub :FindProduct~FindProduct
    
if ($FindProduct~Location = "P") and ($credits < 5000)
setVar $FindProduct~Location 0
end
    
if ($FindProduct~Location = 0)
if ($EWarp)
setVar $Colonise~Method 0
else
return
end
else
setVar $Colonise~Method 1
end
else
if ($EWarp)
setVar $Colonise~Method 0
else
return
end
end
  
send "l" $planet "*"
waitOn "Planet command (?=help)"
  
setVar $Colonise~FastStart 0
setVar $Colonise~FuelSource $FindProduct~Location
setVar $Colonise~Category $bestCategory
setVar $Colonise~TurnLimit $TurnLimit
  
gosub :Colonise~Colonise
  
if ($Colonise~Result = 1)
return
end
  
send "q"
goto :MassColonise
  
:findPlanet
setVar $PlanetInfo~NoHeader 1
gosub :PlanetInfo~PlanetInfo
  
setVar $bestCategory 1
setVar $bestRate 9999
setVar $i 1
  
while ($i <= 3)
if ($PlanetInfo~Rate[$i] <> "N/A")
if ($PlanetInfo~Rate[$i] < $bestRate)
setVar $bestCategory $i
setVar $bestRate $PlanetInfo~Rate[$i]
end
end
add $i 1
end
  
if ($PlanetInfo~Full[$bestCategory] = 0) and ($bestRate < 9999)
setVar $PlanetCheck~Found 1
end
  
return
  
include "..\includes\aliases\pink-scripts-scripts2-include\colonise.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\planetCheck.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\planetInfo.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\findProduct.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\playerInfo.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\gamePrefs.ts"
