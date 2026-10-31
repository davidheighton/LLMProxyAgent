:MakePlanet
sys_check
  
setVar $Failed 0
if ($Sector = 0)
send "d"
setTextLineTrigger getSector :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $Sector 3
end
  
gosub :PlayerInfo~InfoQuick
setVar $Credits $PlayerInfo~Credits
setVar $holds $PlayerInfo~Holds
setVar $torps $PlayerInfo~GenTorps
setVar $dets $PlayerInfo~Dets
setVar $figs $PlayerInfo~Figs
setVar $shield $PlayerInfo~Shields
  
if ((SECTOR.FIGS.QUANTITY[$Sector] <= 0) or ((SECTOR.FIGS.OWNER[$Sector] <> "belong to your Corp") and (SECTOR.FIGS.OWNER[$Sector] = "yours")) or ($PlayerInfo~TWarp = 0) or ($PlayerInfo~Align < 1000)) and ($WarpType = "T")
setVar $WarpType E
end
  
:bust
if ($torps <= 0) or ($dets <= 1)
gosub :sub_Resupply
end
  
if ($Failed > 0)
return
end
send "uy n " #8 #8
subtract $torps 1
setTextLineTrigger 1 :Bust_TestPlanet "What do you want to name"
pause
:Bust_TestPlanet
getWord CURRENTLINE $Type 11
stripText $Type ")"
  
setVar $i 0
while ($i < $WantedPlanetCount)
if ($WantedPlanets[$i] = $Type)
goto :Bust_Wanted
end
add $i 1
end
  
getRnd $name 1000 99999
mergeText "Kill-" $name $longName
send $longName "*cl"
waitFor "Command [TL="
setTextLineTrigger 1 :Bust_Landed "Landing sequence engaged..."
setTextLineTrigger 2 :Bust_GetID $longName
pause
:Bust_GetID
setVar $line CURRENTLINE
stripText $line "<"
stripText $line ">"
getWord $line $planetID 1
send $planetID "* "
killTrigger 1
:Bust_Landed
killTrigger 2
send "zdy  "
subtract $dets 1
goto :bust
:Bust_Wanted
getRnd $Name 1000 99999
setVar $Name "TWX-" & $type & "-" & $Name
send $Name "*cl"
  
waitOn "Should this be a"
setTextLineTrigger 1 :Bust_Landed2 "Landing sequence engaged..."
setTextLineTrigger 2 :Bust_GetID2 $Name
pause
:Bust_GetID2
setVar $line CURRENTLINE
stripText $line "<"
stripText $line ">"
getWord $line $PlanetID 1
send "q*"
killTrigger 1
return
:Bust_Landed2
setTextLineTrigger 1 :Bust_Landed3 "Planet #"
pause
:Bust_Landed3
getWord CURRENTLINE $PlanetID 2
stripText $PlanetID "#"
killTrigger 2
send "q"  
return
:sub_Resupply
if ($Credits < $CreditLimit)
setVar $Failed 1
return
end
  
gosub :PlayerInfo~InfoQuick
setVar $buyFigs ($figs - $PlayerInfo~Figs)
setVar $buyShield ($shield - $PlayerInfo~Shield)
setVar $Credits $PlayerInfo~Credits
  
send "v"
setTextLineTrigger getStardock :getStardock "The StarDock is located in sector"
pause
:getStardock
getWord CURRENTLINE $stardock 7
stripText $stardock "."
  
if ($WarpType = "T")
setVar $SeekProduct~Product 1
setVar $SeekProduct~Holds $holds
gosub :SeekProduct~SeekProduct
    
if ($stardock < 600) or (SECTORS > 5000)
send $stardock "*yy"
else
send $stardock "yy"
end
else
setVar $Warp~Mode $WarpType
setVar $Warp~Dest $stardock
gosub :Warp~Warp
end
send "ps  g yg qh t"
waitFor "Planning on starting a colony eh?"
setTextTrigger Resupply_GetTorps :Resupply_GetTorps ") [0] ?"
pause
  
:Resupply_GetTorps
getWord CURRENTLINE $Resupply_Torps 9
stripText $Resupply_Torps ")"
if ($torps >= 20)
send "*a"
elseif ($Resupply_Torps < (20 - $torps))
send $Resupply_Torps "*a"
else
send (20 - $torps) "*a"
end
add $torps $Resupply_Torps
  
waitFor "We have the standard Nuerevy Atomic Detonator"
setTextTrigger Resupply_GetDets :Resupply_GetDets ") [0] ?"
pause
  
:Resupply_GetDets
getWord CURRENTLINE $Resupply_Dets 9
stripText $Resupply_Dets ")"
send $Resupply_Dets "*"
add $dets $Resupply_Dets
  
if ($buyFigs > 0) or ($buyShield > 0)
send "qs p "  
  
if ($buyFigs > 0)
send "b" $buyFigs "*"
end
if ($buyFigs > 0)
send "c" $buyShield "*"
end
    
send "q"
end
send "qq"  
  
if ($WarpType = "T")
if ($Sector < 600) or (SECTORS > 5000)
send $Sector "*yy"
else
send $Sector "yy"
end
else
setVar $Warp~Mode $WarpType
setVar $Warp~Dest $Sector
gosub :Warp~Warp
end
  
return
  
  
include "..\includes\aliases\pink-scripts-scripts2-include\playerInfo.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\warp.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\seekProduct.ts"
