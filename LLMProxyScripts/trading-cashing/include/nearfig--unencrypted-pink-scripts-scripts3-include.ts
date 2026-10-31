:NearFig
sys_check
  
setVar $SeekSectors[1] $Sector
setVar $SeekSectorsSize 1
setVar $SeekIndex 1
  
if ($Cache~Figs[$Sector] = 1)
setVar $NearFig $Sector
return
end
  
:CheckNext
if ($SeekIndex > $SeekSectorsSize)
setVar $NearFig 0
return
end
  
setVar $WarpIndex 1
:NextWarp
setVar $TestSector SECTOR.WARPSIN[$SeekSectors[$SeekIndex]][$WarpIndex]
if ($TestSector > 0)
if ($Cache~Figs[$TestSector] = 1)
setVar $NearFig $TestSector
return      
end
  
add $SeekSectorsSize 1
setVar $SeekSectors[$SeekSectorsSize] $TestSector
add $WarpIndex 1
goto :NextWarp
end
  
add $SeekIndex 1
goto :CheckNext
