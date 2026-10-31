


# Setup
	SetVar $stardock 3106


# Get Turns
:GetTurnAmount
	send "i"
	setTextLineTrigger GetTurnAmount :getturns "Turns left"
	pause
	:getturns
	killAllTriggers
	getWord CURRENTLINE $currentturns 4
	stripText $currentturns ","
waitFor "Citadel command"

#Avoid Fed Sectors
:AvoidFed
	send "^s1*s2*s3*s4*s5*s6*s7*s8*s9*s10*q"
waitFor "Citadel command"
	send "^s" $stardock "*q"
waitFor "Citadel command"

# Main
:Main
send "s"
setTextLineTrigger SectorCheck :SectorCheck "Sector  :"
pause
:SectorCheck
killalltriggers
setVar $SectorLine CURRENTLINE
getWord $SectorLine $currentsector 3
setTextLineTrigger WarpsCheck :WarpsCheck "Warps to Sector(s)"
pause
:WarpsCheck
setVar $warpsline CURRENTLINE
striptext $warpsline "-"
striptext $warpsline ":"
striptext $warpsline ")"
striptext $warpsline "("
striptext $warpsline "Warps to Sectors"
setVar $checkloop 1
:WarpsCheckLoop
	getword $warpsline $currentwarps[$checkloop] $checkloop
	if ($currentwarps[$checkloop] = 0)
		goto :WarpsCheckDone
	end
	add $checkloop 1
	goto :WarpsCheckLoop

:WarpsCheckDone
settexttrigger minedsector :minedsector "Mined Sector: Do you wish"
settexttrigger pastminedsector :pastminedsector "Citadel command"
pause
:minedsector
send "n"
:pastminedsector

killAllTriggers
	SetVar $engageprompt 1	
	ECHO ANSI_9 "**[" ANSI_11 " current turns: " ANSI_12 $currentturns ANSI_9 " ][ " ANSI_15 "q = quit" ANSI_9 " ][ " ANSI_15 "x = escape" ANSI_9 " ]"
	setVar $displaycheckloop 1
	:DisplayCheckLoop
		if ($displaycheckloop = $checkloop)
			goto :DoneDisplayCheckLoop
		end
		ECHO ANSI_9 "*[ " ANSI_11 "warp: " ANSI_14 $currentwarps[$displaycheckloop] ANSI_9 " - "
		getsector $currentwarps[$displaycheckloop] $sectorstate
		if ($sectorstate.ANOMOLY = "YES")
			ECHO ANSI_12 "ANOMOLY" ANSI_9 " - "
		end
		ECHO ANSI_3 "density: " ANSI_12 $sectorstate.DENSITY ANSI_9 " - "
		ECHO ANSI_3 "warps: " ANSI_12 $sectorstate.WARPS ANSI_9 " ]"
		add $displaycheckloop 1
		goto :DisplayCheckLoop
	:DoneDisplayCheckLoop
	getInput $destination "[ Enter Sector to Route ]: "
	isnumber $canroute $destination
	if ($canroute = "FALSE")
		if ($destination = "end") or ($destination = "q") or ($destination = "Q") or ($destination = "quit")
			goto :End
		end
		if ($destination = "x") or ($destination = "escape") or ($destination = "X")
			send "b" $escapesector "*yx" $planetship "**l" $planet "*c"
			send "p" $escapesector "*y"
			goto :End
		end
		goto :Main
	end
	if ($destination > SECTORS)
		goto :Main
	end
	getdistance $sectordistance $currentsector $destination
	if ($sectordistance = 1)
		goto :SingleStepMode
	end 

:WarpPathBuilder
send "^f" $currentsector "*" $destination "*q"
setTextlinetrigger BuildPath :BuildPath $currentsector&" > "
setTextLineTrigger NoRoute :NoRoute "No route within"
pause
:BuildPath
killAllTriggers
setVar $distance 1
setVar $routeline CURRENTLINE
striptext $routeline " >"
striptext $routeline "("
striptext $routeline ")"

:PathLoop
getWord $routeline $warppath[$distance] $distance
if ($warppath[$distance] = $destination)
	Goto :Display
end
if ($warppath[$distance] = "0")
	settextlinetrigger NextLine :NextLine " "
	pause
end
add $distance 1
goto :PathLoop
:NextLine
setVar $moredistance 1
setVar $routeline CURRENTLINE
striptext $routeline " >"
striptext $routeline ")"
striptext $routeline "("
:PathLoop2
getWord $routeline $warppath[$distance] $moredistance
if ($warppath[$distance] = $destination)
	Goto :Display
end
if ($warppath[$distance] = "0")
	settextlinetrigger NextLine :NextLine2 " "
	pause
end
add $moredistance 1
add $distance 1
goto :Pathloop2
:NextLine2
setVar $moredistance 1
setVar $routeline CURRENTLINE
striptext $routeline " >"
striptext $routeline ")"
striptext $routeline "("
:PathLoop3
getWord $routeline $warppath[$distance] $moredistance
if ($warppath[$distance] = $destination)
	Goto :Display
end
if ($warppath[$distance] = "0")
	subtract $distance 1
	Goto :Display
end
add $moredistance 1
add $distance 1
goto :Pathloop3

:Display
echo ANSI_11 "**Slice path: "
setvar $displayloop 1
:DisplayLoop
ECHO ANSI_15 ">" ANSI_12  $warppath[$displayloop] 
if ($displayloop = $distance)
	Goto :DisplayDone
end
add $displayloop 1
goto :DisplayLoop
:DisplayDone
echo ANSI_11 "**"
if ($distance = 2)
	goto :Slice
end
:EngageNow
if ($engageprompt = 1)
echo ANSI_12 " Engage Now? "
GetConsoleInput $engage YES
if ($engage = "Y")
	goto :Slice
end
else
	goto :Slice
end
goto :Main


:Slice
setVar $spanky 1
:spanky

if ($spanky < $lane)
	add $spanky 1
	#macro by Xide
	GetSector $lane[$spanky] $sectinfo
	echo "**DENSITY!!!: " $sectinfo.DENSITY "**"
	if ($sectinfo.DENSITY > $avoid_density)
	goto :top
	end
	if ($sectinfo.ANOMOLY = YES)
	echo "**ANOMOLY!!!**"
	goto :top
	end
	if ($sectinfo.NAVHAZ = $avoid_navhaz)
	echo "**NAVHAZ!!!: " $sectinfo.NAVHAZ "**"
	goto :top
	end
	send "m" $lane[$spanky] "*atny9999**f1*ctsd"
	waitFor "Long Range Scan"
	waitFor "Command [TL="
	goto :spanky
end

GetSector $destination $sectinfo
echo "**DENSITY-2!!!: " $sectinfo.DENSITY "**" 
if ($sectinfo.DENSITY > $avoid_density)
goto :top
end
if ($sectinfo.ANOMOLY = YES)
echo "**ANOMOLY-2!!!**"
goto :Main
end
if ($sectinfo.NAVHAZ = $avoid_navhaz)
echo "**NAVHAZ-2!!!: " $sectinfo.NAVHAZ "**"
goto :Main
end
send "m" $destination "*atny9999**f1*ctsd"
waitFor "Long Range Scan"
waitFor "Command [TL="
end
goto :Main
