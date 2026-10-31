:NearFig
sys_check
  
setVar $SeekSectors[1] $Sector
setVar $SeekSectorsSize 1
setVar $SeekIndex 1
  
if ($checkIndex = 0) or ($checkIndex >= 99)
setVar $checkIndex 1
setArray $checkedList SECTORS
else
add $checkIndex 1
end
  
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
  
if ($checkedList[$TestSector] <> $checkIndex)
add $SeekSectorsSize 1
setVar $SeekSectors[$SeekSectorsSize] $TestSector
setVar $checkedList[$TestSector] $checkIndex
end
    
add $WarpIndex 1
goto :NextWarp
end
  
add $SeekIndex 1
goto :CheckNext
:NearAOS
sys_check
  
setVar $SeekSectors[1] $Sector
setVar $SeekSectorsSize 1
setVar $SeekIndex 1
  
if ($checkIndex = 0) or ($checkIndex >= 99)
setVar $checkIndex 1
setArray $checkedList SECTORS
else
add $checkIndex 1
end
  
if ($Cache~AOS[$Sector] <> 0) and ($Cache~AOS[$Sector] <> L)
setVar $NearAOS $Sector
setVar $AOSType $Cache~AOS[$Sector]
return
end
  
:CheckNextAOS
if ($SeekIndex > $SeekSectorsSize)
setVar $NearAOS 0
return
end
  
setVar $WarpIndex 1
:NextWarpAOS
setVar $TestSector SECTOR.WARPS[$SeekSectors[$SeekIndex]][$WarpIndex]
if ($TestSector > 0)
if ($Cache~AOS[$TestSector] <> 0) and ($Cache~AOS[$TestSector] <> L)
setVar $NearAOS $TestSector
setVar $AOSType $Cache~AOS[$TestSector]
return      
end
  
if ($checkedList[$TestSector] <> $checkIndex)
add $SeekSectorsSize 1
setVar $SeekSectors[$SeekSectorsSize] $TestSector
setVar $checkedList[$TestSector] $checkIndex
end
      
add $WarpIndex 1
goto :NextWarpAOS
end
  
add $SeekIndex 1
goto :CheckNextAOS
