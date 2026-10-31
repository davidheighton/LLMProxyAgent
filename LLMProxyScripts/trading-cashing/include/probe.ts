:Probe
sys_check
  
gosub :PlayerInfo~InfoQuick
setVar $Probes $PlayerInfo~Probes
setVar $Credits $PlayerInfo~Credits
setVar $Turns $PlayerInfo~Turns
setVar $probeMax 0
setVar $Result 0
setVar $Index ($Start - 1)
setVar $Sector 0
setVar $lastBuy 0
  
if ($ListFile = "")
setVar $ListFile 0
end
  
if ($UnreachedFile = "")
setVar $UnreachedFile 0
end
  
if ($BlockedFile = "")
setVar $BlockedFile 0
end
  
setArray $Blocked SECTORS
setArray $Unreached SECTORS
  
setVar $GamePrefs~Bank "Probe"
setVar $GamePrefs~AbortDisplayAll[$GamePrefs~Bank] OFF
  
gosub :GamePrefs~SetGamePrefs
  
if ($Probes <= 0)
goto :buyProbes
end
  
:launchProbe
  
if ($ListFile = "0")
setVar $Sector ($Index + 1)
else
read $ListFile $Sector ($Index + 1)
end
  
if ($Sector = "EOF")
gosub :sub_ProbeEnd
return
end
  
if ($Sector > SECTORS)
gosub :sub_ProbeEnd
return
end
  
if ((SECTOR.EXPLORED[$Sector] = YES) and ($ProbeExplored = 0)) or ($Blocked[$Sector])
add $Index 1
goto :launchProbe
end
  
if ($ProbeSub <> "0")
setVar $LaunchProbe 0
gosub $ProbeSub
else
setVar $LaunchProbe 1
end
  
if ($LaunchProbe)
send "e" $Sector "*"
setVar $curProbeSector 0
setVar $madeIt 1
waitOn "Please enter a destination for this probe "    
setTextLineTrigger noRoute :noRoute " Error - No route within"
:nextSector
setTextLineTrigger getSector :getSector "Probe entering sector : "
setTextLineTrigger probeDestroyed :probeDestroyed "Probe Destroyed!"
setTextTrigger getPrompt :getPrompt "Command [TL="
pause
    
:noRoute
killTrigger getSector
killTrigger probeDestroyed
killTrigger getPrompt
    
if ($Unreached[$Sector] = 0)
setVar $Unreached[$Sector] 1
write $UnreachedFile $Sector
end
    
send "n"
add $Index 1
goto :launchProbe
      
:probeDestroyed
killTrigger getSector
killTrigger getPrompt
killTrigger noRoute
setVar $madeIt 0
goto :probeDone
    
:getSector
killTrigger getPrompt
killTrigger probeDestroyed
killTrigger noRoute
setVar $ProbeSector $curProbeSector
getWord CURRENTLINE $curProbeSector 5
gosub :sub_TestSector
if ($Found)
setVar $Result 3
return
end
    
goto :nextSector
    
:getPrompt
killTrigger getSector
killTrigger probeDestroyed
killTrigger noRoute
setVar $ProbeSector $curProbeSector
gosub :sub_TestSector
    
if ($Found)
setVar $Result 3
return
end
    
:probeDone
    
subtract $Probes 1
    
if ($madeIt)
add $Index 1
else
      
setVar $Blocked[$curProbeSector] 1
write $BlockedFile $curProbeSector
        
if ($AvoidBlocks)
send "cv" $curProbeSector "*q"
          
if ($curProbeSector = $Sector)
if ($Unreached[$Sector] = 0)
setVar $Unreached[$Sector] 1
write $UnreachedFile $curProbeSector
end
add $Index 1
end
else
if ($Unreached[$Sector] = 0)
setVar $Unreached[$Sector] 1
write $UnreachedFile $curProbeSector
end
add $Index 1
end
end
end  
:buyProbes
  
if ($Probes <= 0)
    
if ($Turns <= $TurnLimit) and ($TurnLimit > 0)
setVar $Result 2
return
end
    
if ($lastBuy)
setVar $Result 1
gosub :sub_ProbeEnd
return
end
    
if ($probeMax = 0)
send "ps"
      
setTextLineTrigger limpet :limpet "A port official runs up"
setTextTrigger onDock :onDock "<StarDock> Where to? (?=Help)"
pause
      
:limpet
send "y"
pause
      
:onDock
killTrigger limpet
send "h e"
waitOn "<Hardware Emporium> So what are you looking"
      
setTextLineTrigger getProbePrice :getProbePrice "We sell them for"
pause
      
:getProbePrice
getWord CURRENTLINE $probePrice 5
stripText $probePrice ","
      
waitOn ") [0] ?"
getWord CURRENTLINE $maxProbes 8
stripText $maxProbes ")"
      
gosub :sub_CalcProbeBuy
      
if ($buy <= 0)
setVar $Result 1
send "*qq"
gosub :sub_ProbeEnd
return
end
      
send $buy "*qq"
else
gosub :sub_CalcProbeBuy
      
if ($buy <= 0)
setVar $Result 1
gosub :sub_ProbeEnd
return
end
      
send "p s h e " $buy "*qq"
end
    
setVar $Probes $buy
subtract $Turns 1
end
  
goto :launchProbe
:sub_TestSector
if ($ProbeSector > 0) and ($CheckSub <> "0")
setVar $Found 0
gosub $CheckSub
end
  
return
:sub_CalcProbeBuy
setVar $buy $maxProbes
  
if ((($buy * $probePrice) + $CreditLimit) > $Credits)
setVar $buy (($Credits - $CreditLimit) / $probePrice)
setVar $lastBuy 1
end
  
subtract $Credits ($buy * $probePrice)
return  
:sub_ProbeEnd
setVar $GamePrefs~Bank "Probe"
gosub :GamePrefs~SetGamePrefs
  
return
  
include "..\includes\aliases\pink-scripts-scripts2-include\playerInfo.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\gamePrefs.ts"
