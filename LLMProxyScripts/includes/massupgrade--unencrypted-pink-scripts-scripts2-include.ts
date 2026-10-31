:MassUpgrade
sys_check
  
setVar $GamePrefs~Bank "MassUpgrade"
setVar $GamePrefs~Animation[$GamePrefs~Bank] OFF
setVar $GamePrefs~AbortDisplayAll[$GamePrefs~Bank] OFF
setVar $GamePrefs~ScreenPauses[$GamePrefs~Bank] OFF
gosub :GamePrefs~SetGamePrefs
  
if ($Sector = 0)
send "d"
setTextLineTrigger getSector :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $Sector 3
waitOn "Command [TL="
end
  
if (SECTOR.PLANETCOUNT[$Sector] = 0)
return
end
  
send "jy"
gosub :PlayerInfo~InfoQuick
setVar $holds $PlayerInfo~Holds
  
setVar $PlanetLoop~LoopSub :MassUpgrade~checkPlanet
setVar $PlanetLoop~IgnoreList $IgnoreList
gosub :PlanetLoop~PlanetLoop
  
return
:checkPlanet
setVar $PlanetUpgrade~PlanetID $PlanetLoop~ID
setVar $PlanetUpgrade~Sector $Sector
setVar $PlanetUpgrade~Seek $Seek
gosub :PlanetUpgrade~PlanetUpgrade
return
:Menu
addMenu $Menu "IgnoreList" "Planets to ignore" "I" :Menu_IgnoreList "" FALSE
addMenu $Menu "Seek" "Seek products" "S" :Menu_Seek "" FALSE
setMenuHelp "IgnoreList" "This is a list of planet numbers (seperated by spaces) for the script to avoid upgrading."
setMenuHelp "Seek" "If this option is enabled, the script will leave the current sector in search of materials to upgrade the planets (if it needs to)."
  
gosub :sub_SetMenu
return
  
:Menu_Seek
if ($Seek)
setVar $Seek 0
else
setVar $Seek 1
end
saveVar $Seek
gosub :sub_SetMenu
openMenu $Menu
  
:Menu_IgnoreList
getInput $IgnoreList "Enter a list of planets to ignore, separated by spaces"
saveVar $IgnoreList
gosub :sub_SetMenu
openMenu $Menu
  
:sub_SetMenu
if ($Seek)
setMenuValue "Seek" "ON"
else
setMenuValue "Seek" "OFF"
end
  
setMenuValue "IgnoreList" $IgnoreList
return  
  
include "..\includes\aliases\pink-scripts-scripts2-include\planetUpgrade.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\playerInfo.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\planetLoop.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\gamePrefs.ts"
