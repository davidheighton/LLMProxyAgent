:WorldSSM
sys_check
  
setVar $Checked 0
setVar $Credits 0
setVar $holdsLost 0
setVar $otherHoldsLost 0
setVar $Move~CheckSub :WorldSSM~moveCheck
setVar $Move~PortPriority "1"
if ($BustFile <> "")
fileExists $exists $BustFile
  
if ($exists)
setArray $BustList SECTORS
setVar $i 1
read $BustFile $bust $i
while ($bust <> EOF)
setVar $BustList[$bust] 1
add $i 1
read $BustFile $bust $i
end
end
end
setVar $GamePrefs~Bank 0
setVar $GamePrefs~Animation[0] OFF
setVar $GamePrefs~AbortDisplayAll[0] OFF
gosub :GamePrefs~SetGamePrefs
  
:cycle
  
add $holdsLost $otherHoldsLost
  
if ($holdsLost > 0)
setVar $Refurb~HoldsLost $holdsLost
gosub :Refurb~Refurb
    
if ($Refurb~Failed)
setVar $otherHoldsLost $holdsLost
else
setVar $otherHoldsLost 0
end
    
setVar $Warp~Mode "E"
getRnd $Warp~Dest 11 SECTORS
gosub :Warp~Warp
end
  
send "d"
gosub :Move~Move
  
gosub :PlayerInfo~InfoQuick
setVar $Credits $PlayerInfo~Credits
  
if ($Credits >= $Quota) and ($Quota > 0)
return
end
  
goto :cycle
 
  
:moveCheck
if ($Checked)
setVar $Checked 0
else
setVar $PortCheck~Sector $Move~CurSector
setVar $PortCheck~Scanned 0
gosub :PortCheck~PortCheck
setVar $Move~NoScan $PortCheck~Scanned
setVar $activeBust 0
    
if ($BustFile <> "")
      
if ($BustList[$Move~CurSector]) or ($BustList[$PortCheck~Pair])
setVar $activeBust 1
end
end
    
if ($PortCheck~Pair > 0) and ($activeBust = 0)
send "cf" $PortCheck~Pair "*" $Move~CurSector "*q"
setTextLineTrigger getDist :getDist "The shortest path ("
pause
:getDist
getWord CURRENTLINE $dist 4
if ($dist = "(1")
if ($Move~CurSector > 10) 
send "jy"
end
        
setVar $SSM~OtherSector $PortCheck~Pair
setVar $SSM~DropFigs 1
gosub :SSM~SSM
setVar $HoldsLost $SSM~HoldsLost
setVar $Move~Found 1    
setVar $Checked 1
        
if ($BustFile <> "")
setVar $BustList[$SSM~Sector] 1
write $BustFile $SSM~Sector
end
end
end
end
  
return 
  
  
include "..\includes\aliases\pink-scripts-scripts2-include\refurb.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\SSM.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\move.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\playerInfo.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\warp.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\portCheck.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\gamePrefs.ts"
