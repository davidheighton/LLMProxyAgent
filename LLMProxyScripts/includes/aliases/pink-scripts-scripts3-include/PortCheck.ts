:PortCheck
sys_check
  
setVar $Pair "0"
setVar $Figged "0"
if ($PortType = 1)
if ((PORT.CLASS[$Sector] < 1) or (PORT.CLASS[$Sector] > 8))
setVar $Ignore "0"
return
end
else
if (PORT.BUYEQUIP[$Sector] = 0)
setVar $Ignore "0"
return
end
end
if ($Scanned = "0")
if ($DensityOnly = 1)
send "s"
else
send "sd"
end
waitOn "Relative Density Scan"
waitOn "Command [TL="
setVar $Scanned "1"
end
  
# Density-only ships cannot holo-scan: on them "sh" is a scan followed by H (mine control), which stalls at the
# mine-type prompt. Use the fresh density scan instead and require a lone port (density exactly 100) below.
if ($Scanned = "1") and ($DensityOnly = 1)
setVar $Scanned "2"
elseif ($Scanned = "1")
setVar $holoScan 0
setVar $i 1
while ($i <= SECTOR.WARPCOUNT[$Sector])
setVar $sect SECTOR.WARPS[$Sector][$i]
      
if ((PORT.CLASS[$sect] > 0) or (SECTOR.DENSITY[$sect] >= 100))
setVar $holoScan 1
end
    
add $i 1
end
    
if ($holoScan)
send "sh"
waitOn "Long Range Scan"
waitOn "Command [TL="
setVar $Scanned "2"
end
end
  
if ($Scanned = "2")
setVar $i 1
setVar $class PORT.CLASS[$Sector]
setVar $TradeProdA ""
setVar $TradeProdB ""
    
while ($i <= SECTOR.WARPCOUNT[$Sector]) and ($TradeProdA = "")
setVar $sect SECTOR.WARPS[$Sector][$i]
setVar $pairClass PORT.CLASS[$sect]
      
if (($pairClass > 0) and ($pairClass < 9) and (($DensityOnly <> 1) or (SECTOR.DENSITY[$sect] = 100)) and ((SECTOR.FIGS.QUANTITY[$sect] = "0") or (SECTOR.FIGS.OWNER[$sect] = "yours") or (SECTOR.FIGS.OWNER[$sect] = "belong to your Corp") or (($Danger = "1") and ($SECTOR.FIGS.QUANTITY[$sect] <= 20)) or ($Danger = "2")) and ((SECTOR.MINES.QUANTITY[$sect] = "0") or (SECTOR.MINES.OWNER[$sect] = "yours") or (SECTOR.MINES.OWNER[$sect] = "belong to your Corp") or (($Danger = "1") and ($SECTOR.MINES.QUANTITY[$sect] <= 5)) or ($Danger = "2")) and ((SECTOR.NAVHAZ[$sect] = 0) or ((SECTOR.NAVHAZ[$sect] <= 3) and ($Danger = "1")) or ($Danger = "2")) and ((SECTOR.PLANETCOUNT[$sect] = 0) or ($Danger = "2")) and ((SECTOR.TRADERCOUNT[$sect] = 0) or ($Danger = "2")))
        
if ($PortType = 1)
gosub :sub_PPTCheck
else
if (PORT.BUYEQUIP[$sect])
setVar $TradeProdA "Equipment"
end
end
        
if ($TradeProdA <> "")
subtract $Ignore 1
          
if ($Ignore >= 0)
setVar $TradeProdA ""
setVar $TradeProdB ""
else
setVar $Pair $sect
end
end
end
    
add $i 1
end
end
setVar $Ignore "0"
      
return
  
  
:sub_PPTCheck
if (($class = 1) and ($pairClass = 2))
setVar $TradeProdA "Equipment"
setVar $TradeProdB "Organics"
end
if (($class = 1) and ($pairClass = 3))
setVar $TradeProdA "Equipment"
setVar $TradeProdB "Fuel"
end
if (($class = 1) and ($pairClass = 4))
setVar $TradeProdA "Equipment"
setVar $TradeProdB "Organics"
end
if (($class = 2) and ($pairClass = 1))
setVar $TradeProdA "Organics"
setVar $TradeProdB "Equipment"
end
if (($class = 2) and ($pairClass = 3) and ($FuelOrganics))
setVar $TradeProdA "Organics"
setVar $TradeProdB "Fuel"
end
if (($class = 2) and ($pairClass = 5))
setVar $TradeProdA "Organics"
setVar $TradeProdB "Equipment"
end
if (($class = 3) and ($pairClass = 1))
setVar $TradeProdA "Fuel"
setVar $TradeProdB "Equipment"
end
if (($class = 3) and ($pairClass = 2) and ($FuelOrganics))
setVar $TradeProdA "Fuel"
setVar $TradeProdB "Organics"
end
if (($class = 3) and ($pairClass = 6))
setVar $TradeProdA "Fuel"
setVar $TradeProdB "Equipment"
end
if (($class = 4) and ($pairClass = 1))
setVar $TradeProdA "Organics"
setVar $TradeProdB "Equipment"
end
if (($class = 4) and ($pairClass = 5))
setVar $TradeProdA "Organics"
setVar $TradeProdB "Equipment"
end
if (($class = 4) and ($pairClass = 6))
setVar $TradeProdA "Fuel"
setVar $TradeProdB "Equipment"
end
if (($class = 5) and ($pairClass = 2))
setVar $TradeProdA "Equipment"
setVar $TradeProdB "Organics"
end
if (($class = 5) and ($pairClass = 4))
setVar $TradeProdA "Equipment"
setVar $TradeProdB "Organics"
end
if (($class = 5) and ($pairClass = 6) and ($FuelOrganics))
setVar $TradeProdA "Fuel"
setVar $TradeProdB "Organics"
end
if (($class = 6) and ($pairClass = 3))
setVar $TradeProdA "Equipment"
setVar $TradeProdB "Fuel"
end
if (($class = 6) and ($pairClass = 4))
setVar $TradeProdA "Equipment"
setVar $TradeProdB "Fuel"
end
if (($class = 6) and ($pairClass = 5) and ($FuelOrganics))
setVar $TradeProdA "Organics"
setVar $TradeProdB "Fuel"
end
  
return
:Menu
if ($PortType = 1)
setVar $portMenu "PPT"
addMenu "PPT" "FuelOrganics" "Trade fuel/organics" "R" :Menu_FuelOrganics "" FALSE
setMenuHelp "FuelOrganics" "If this option is enabled, the script will trade fuel/organics where this combination is available."
else
setVar $portMenu "SSM"
end
  
gosub :sub_SetMenu
return
  
:Menu_Danger
if ($Danger = 2)
setVar $Danger 0
else
add $Danger 1
end
saveVar $Danger
gosub :sub_SetMenu
openMenu $portMenu
  
:Menu_FuelOrganics
if ($FuelOrganics)
setVar $FuelOrganics 0
else
setVar $FuelOrganics 1
end
saveVar $FuelOrganics
gosub :sub_SetMenu
openMenu $portMenu
  
:sub_SetMenu
if ($PortType = 1)
if ($FuelOrganics)
setMenuValue "FuelOrganics" "ON"
else
setMenuValue "FuelOrganics" "OFF"
end
end
  
return
  
if ($Danger = 0)
setMenuValue "Danger" "PARANOID"
elseif ($Danger = 1)
setMenuValue "Danger" "STANDARD"
else
setMenuValue "Danger" "RECKLESS"
end
  
return  
