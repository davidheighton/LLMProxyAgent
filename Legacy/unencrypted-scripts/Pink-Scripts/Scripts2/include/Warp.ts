:Warp
sys_check
  
setVar $Abort 0
  
if ($pay = 0)
if ($Move~Attack = 3)
setVar $pay "Y"
end
end
if ($mode = "S")
goto :Scan
end
  
if ($mode = "T")
:testTWarp
send "cf*" $dest "*q"
setTextLineTrigger getDist :getDist "The shortest path ("
pause
:getDist
getWord CURRENTLINE $dist 4
stripText $dist "("
multiply $dist 3
  
gosub :PlayerInfo~InfoQuick
    
if ($PlayerInfo~Holds < $dist)
      
if ($useNearFig = 2)
setVar $Abort 1
return
else
setVar $mode "E"
goto :Warp
end
end
    
if ($PlayerInfo~Ore >= $dist)
if (SECTORS > 5000) or ($dest < 600)
send $dest "*"
else
send $dest ""
end
      
if ($dist = 3)
goto :Arrived
end
      
send "y"
      
setTextLineTrigger canTWarp :canTWarp "Locating beam pinpointed, TransWarp Locked."
setTextLineTrigger cannotTWarp :cannotTWarp " No locating beam found"
pause
      
:canTWarp
killTrigger cannotTWarp
send "y*"      
goto :Arrived
      
:cannotTWarp
killTrigger canTWarp
send "n"
      
if ($useNearFig = 1)
setVar $NearFig~Sector $Dest
gosub :NearFig~NearFig
        
if (SECTORS > 5000) or ($NearFig~NearFig < 600)
send $NearFig~NearFig "*y"
else
send $NearFig~NearFig "y"
end
        
setTextLineTrigger canTWarp2 :canTWarp2 "Locating beam pinpointed, TransWarp Locked."
setTextLineTrigger cannotTWarp2 :cannotTWarp2 " No locating beam found"
pause
      
:canTWarp2
killTrigger cannotTWarp2
send "y*"      
setVar $Mode "E"
goto :Warp
      
:cannotTWarp2
killTrigger canTWarp2
send "n"
setVar $Mode "E"
goto :Warp
        
elseif ($useNearFig = 2)
setVar $Abort 1
return
else
setVar $mode "E"
goto :Warp
end
else
if ($dist > $PlayerInfo~Holds)
setVar $dist $PlayerInfo~Holds
end
      
setVar $SeekProduct~Product 1
setVar $SeekProduct~IgnoreList ""
setVar $Move~ScanHolo 2
setVar $Move~Evasion 1
setVar $Move~PortPriority 1
gosub :SeekProduct~SeekProduct
      
goto :testTWarp
end
end
  
if (SECTORS > 5000) or ($dest < 600)
send $dest "*"
else
send $dest
end
setTextLineTrigger Arrived :Arrived "You are already in that sector!"
setTextLineTrigger Begin :Begin "<Move>"
pause
  
:Begin  
killTrigger Arrived
setTextTrigger Start :Start "Engage the Autopilot?"
setTextTrigger TWarp :TWarp "Do you want to engage"
setTextLineTrigger Single :Single "Sector  :"
pause
:TWarp
if ($mode = "T")
goto :SmartTWarp
end
send "n"
:Start
send "e"
:Single
killTrigger Start
killTrigger TWarp
killTrigger Abort
killTrigger Single
  
setVar $stopPrompt 1
setVar $minePrompt 1
:MidWarp
killTrigger TollFigs
killTrigger Figs
killTrigger StopPrompt
killTrigger Mines
killTrigger NextSector
killTrigger Arrived
setTextLineTrigger NextSector :NextSector "Sector  :"
setTextLineTrigger TollFigs :TollFigs "You have to destroy the fighters or pay"
setTextLineTrigger Figs :Figs "You have to destroy the fighters to remain"
setTextTrigger StopPrompt :StopPrompt "Stop in this sector"
setTextTrigger Mines :MinePrompt "Mined Sector:"
setTextTrigger Arrived :Arrived "Command [TL="
pause
:NextSector
setVar $stopPrompt 1
setVar $minePrompt 1
goto :MidWarp
:TollFigs
if ($pay = "Y")
send "py"
else
send "a9999*"
end
goto :MidWarp
:Figs
send "a9999*"
goto :MidWarp
:StopPrompt
if ($stopPrompt)
send "n"
setVar $stopPrompt 0
end
goto :MidWarp
:MinePrompt
if ($minePrompt)
send "n"
setVar $minePrompt 0
end
goto :MidWarp
:Arrived
killTrigger Arrived
killTrigger NextSector
killTrigger TollFigs
killTrigger Figs
killTrigger StopPrompt
KillTrigger Mines
killTrigger Begin
  
setVar $noAvoid 0
return
:Scan
send "cf*" $dest "*q"
waitFor "<Computer activated>"
setVar $ArrayCount 1
:NextCalcLine
killTrigger GetLane
killTrigger GotLane
killTrigger NoLane
setTextLineTrigger GetLane :GetLane " > "
setTextLineTrigger GotLane :GotLane "<Computer deactivated>"
setTextLineTrigger NoLane :NoLane "No route within"
pause
:NoLane
killTrigger GotLane
setVar $noAvoid 1
send "yq"
goto :Scan
:GetLane
setVar $Count 1
killTrigger NoLane
:NextCalcSector
getWord CURRENTLINE $Testword $Count
if ($Testword = "0")
goto :NextCalcLine
end
if ($Testword <> ">")
stripText $Testword "("
stripText $Testword ")"
setVar $Sector[$ArrayCount] $Testword
add $ArrayCount 1
end
        
add $Count 1
goto :NextCalcSector
:GotLane
setVar $Count $ArrayCount
killTrigger GetLane
killTrigger NoLane
subtract $Count 1
if ($Sector[$Count] <> $dest)
add $Count 1
setVar $Sector[$Count] $dest
end
setVar $Count 1
:Warp
if ($Sector[$Count] = $dest)
goto :Arrived
end
        
getSector $Sector[$count] $Sector
if ($Sector.beacon = "FedSpace, FedLaw Enforced")
send "sd"
else
send "f1*ctsd"
end
waitFor "Relative Density"
waitFor "Command [TL="
add $Count 1
if ($Sector[$Count] <> 0)
getSector $Sector[$count] $NextSect
if ($NextSect.density <> 0) and ($NextSect.density <> 5) and ($NextSect.density <> 100) and ($NextSect.density <> 105) and ($noAvoid <> 1) and ($NextSect.index <> $class0) and ($NextSect.density <> 1) and ($NextSect.density <> 101) and ($NextSect.density <> 6) and ($NextSect.density <> 106) and ($NextSect.density <> 50) and ($NextSect.index <> $dest)
send "cv" $NextSect.index "*q"
waitFor "<Computer deactivated>"
goto :Scan
end
else
waitFor "Command [TL="
goto :Arrived
end
send $Sector[$Count] "*"
waitFor "<Move>"
if ($NextSect.density <> 100) and ($NextSect.density <> 0) and ($NextSect.density <> 1) and ($NextSect.density <> 101)
send "na9999*"
end
setVar $noAvoid 0
waitFor "Command [TL="
goto :Warp
  
  
include "include\playerInfo"
include "include\nearFig"
include "include\findProduct"
include "include\haggle"
