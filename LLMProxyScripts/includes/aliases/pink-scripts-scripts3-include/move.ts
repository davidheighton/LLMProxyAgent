:Move
sys_check
  
setTextLineTrigger 1 :GetSector "Sector  : "
pause
:GetSector
getWord CURRENTLINE $curSector 3
setVar $history[9] $history[8]
setVar $history[8] $history[7]
setVar $history[7] $history[6]
setVar $history[6] $history[5]
setVar $history[5] $history[4]
setVar $history[4] $history[3]
setVar $history[3] $history[2]
setVar $history[2] $history[1]
setVar $history[1] $curSector
if ($confirmSector = 1)
setTextLineTrigger TollFigs :TollFigs "You have to destroy the fighters or pay"
setTextLineTrigger Figs :Figs "You have to destroy the fighters to remain"
setTextTrigger Mines :MinePrompt "Mined Sector:"
setTextTrigger Arrived :Arrived "Command [TL="
pause
    
:TollFigs
if ($attack = 3)
send "py"
else
send "a9999*"
end
pause
    
:Figs
send "a9999*"
pause
    
:MinePrompt
send "n"
pause
    
:Arrived
killTrigger TollFigs
killTrigger Figs
killTrigger Mines
else
waitOn "Command [TL="
end
getSector $curSector $curSector
setVar $confirmSector 0
setVar $Found 0
setVar $NoScan 0
gosub $checkSub
  
if ($Found = 1)
return
end
if ($scanHolo = 2) and ($NoScan < 2)
setVar $scannedHolo 1
send "shsd"
waitOn "Relative Density Scan"
waitOn "Command [TL="
elseif ($NoScan = 0)
setVar $scannedHolo 0
if ($DensityOnlyScanner = 1)
send "s"
else
send "sd"
end
waitOn "Relative Density Scan"
waitOn "Command [TL="
end
getSector $curSector $curSector
:Assess
setVar $i 1
setVar $bestScore 1000
setVar $bestWarp 0
setVar $bestAttack 0
setVar $willHolo 0
:TestWarp
if ($curSector.warp[$i] > 0)
setVar $score 0
setVar $safe 1
    
getSector $curSector.warp[$i] $thisSector
  
if ($evasion <> 2)
if ($scannedHolo = 0)
        
if ($thisSector.density <> 0) and ($thisSector.density <> 100)
if ($thisSector.density = 5) or ($thisSector.density = 105)
setVar $safe 2
else
setVar $safe 0
end
end
end
if ($scannedHolo = 1)
       
if ($thisSector.anomoly = YES)
setVar $safe 0
end
if ($thisSector.figs.owner <> "belong to your Corp") and ($thisSector.figs.owner <> "yours") and ($thisSector.figs.quantity > 0)
if ($evasion = 1)
setVar $safe 0
else
setVar $safe 2
            
if ($thisSector.figs.quantity > 20)
setVar $safe 0
end
end
end
if ($thisSector.density > 0)
setVar $density $thisSector.density
          
if ($thisSector.figs.quantity > 0)
setVar $x $thisSector.figs.quantity
multiply $x 5
subtract $density $x
end
          
if (($density <> 100) or ($thisSector.port.exists = 0)) and ($density > 0)
setVar $safe 0
end
end
        
end
end
if ($safe = 2) and ($evasion = 1)
add $score 500
end
if ($safe = 0)
add $score 500
setVar $willHolo 1
end
setVar $x 1
:CheckHistory
if ($x <= 10)
if ($history[$x] = $curSector.warp[$i])
setVar $m 10
subtract $m $x
multiply $m 10
add $score $m
end
add $x 1
goto :CheckHistory
end
if ($portPriority = 1)
if (($scannedHolo = 1) and ($thisSector.port.exists = 1)) or (($scannedHolo = 0) and ($thisSector.density = 100))
subtract $score 3
end
end
    
if ($dedPriority = 1)
if ($thisSector.warps = 1)
subtract $score 3
end
end
getRnd $random 1 5
add $score $random
if ($score < $bestScore)
setVar $bestScore $score
setVar $bestWarp $curSector.warp[$i]
setVar $bestSafe $safe
end
add $i 1
goto :TestWarp
end
  
if ($bestScore > 400)
setVar $willHolo 1
end
if ($willHolo = 1) and ($scannedHolo = 0) and ($scanHolo = 1)
send "sh"
waitFor "Sector  : "
waitFor "Command [TL="
setVar $scannedHolo 1
goto :Assess
end
  
if ($bestScore > 400) and ($evasion = 1)
clientMessage "No safe options!"
halt
end
if ($ExtraSend <> "") and ($CurSector > 10) and (PORT.CLASS[$CurSector] < 9)
send $ExtraSend
end
if (SECTORS > 5000) or ($bestWarp < 600)
setVar $warpSuffix "*"
else
setVar $warpSuffix "."
end
  
if (($bestSafe = 2) and ($attack = 1)) or ($attack = 2)
send $bestWarp $warpSuffix "*na9999**"
else
send $bestWarp $warpSuffix
setVar $confirmSector 1
end
  
goto :Move
:Menu
addMenu $Menu "Move" "Navigation Settings" "M" "" "Nav" FALSE
addMenu "Move" "Holoscan" "Holo-scan frequency" "H" :Menu_Holoscan "" FALSE
addMenu "Move" "Evasion" "Evasion pattern" "E" :Menu_Evasion "" FALSE
addMenu "Move" "Attack" "Attack preference" "A" :Menu_Attack "" FALSE
addMenu "Move" "ExtraSend" "Extra commands" "X" :Menu_ExtraSend "" FALSE
  
setMenuHelp "Holoscan" "This option determines how often the script will perform holo-scans while it navigates.*There are three settings.**NEVER: The script will navigate using only a density scanner.*STANDARD: The script will use a holo-scanner only if it spots something unusual.*ALWAYS: The script will holo-scan from every sector."
setMenuHelp "Evasion" "This option adjusts the script's hazard evasion pattern.  There are three settings.**STANDARD: If it has any choice, the script will avoid unusual looking sectors.  It will still enter the sector of small groups of enemy fighters.*PARANOID: The script will avoid any possible hazard, including enemy fighters.  If the script has no safe options it will immediately shut down.*RECKLESS: The script performs no hazard checking and will enter any sector in its path."
setMenuHelp "Attack" "This option adjusts how the script behaves in response to enemy fighters.  There are four settings.**STANDARD: The script will destroy any enemy fighters that hang it up when it enters a sector.*RAPID: The script attempt to destroy the fighters as a part of its sector entry - this will make it more resistant to planet-drops.*CRAZY: The script will attempt to destroy fighters in every sector it enters.*PAY TOLLS: The same as standard, although the script will pay toll fighters rather than destroying them."
setMenuHelp "ExtraSend" "This option allows you to configure extra text the script will send before it leaves a sector.  This text can be anything from deploying a group of fighters to destroying a starport."
  
gosub :sub_SetMenu
return
  
:Menu_Holoscan
if ($ScanHolo = 2)
setVar $ScanHolo 0
else
add $ScanHolo 1
end
saveVar $ScanHolo
gosub :sub_SetMenu
openMenu "Move"
  
:Menu_Evasion
if ($Evasion = 2)
setVar $Evasion 0
else
add $Evasion 1
end
saveVar $Evasion
gosub :sub_SetMenu
openMenu "Move"
    
:Menu_Attack
if ($Attack = 3)
setVar $Attack 0
else
add $Attack 1
end
saveVar $Attack
gosub :sub_SetMenu
openMenu "Move"
  
:Menu_ExtraSend
getInput $ExtraSend "Enter the additional commands/text to be sent on sector entry"
saveVar $ExtraSend
replaceText $ExtraSend #42 "*"
gosub :sub_SetMenu
openMenu "Move"
  
:sub_SetMenu
if ($ScanHolo = 0)
setMenuValue "Holoscan" "NEVER"
elseif ($ScanHolo = 1)
setMenuValue "Holoscan" "STANDARD"
else
setMenuValue "Holoscan" "ALWAYS"
end
  
if ($Evasion = 0)
setMenuValue "Evasion" "STANDARD"
elseif ($Evasion = 1)
setMenuValue "Evasion" "PARANOID"
else
setMenuValue "Evasion" "RECKLESS"
end
  
if ($Attack = 0)
setMenuValue "Attack" "STANDARD"
elseif ($Attack = 1)
setMenuValue "Attack" "FAST"
elseif ($Attack = 2)
setMenuValue "Attack" "CRAZY"
else
setMenuValue "Attack" "PAY TOLLS"
end
  
setMenuValue "ExtraSend" $ExtraSend
  
return
