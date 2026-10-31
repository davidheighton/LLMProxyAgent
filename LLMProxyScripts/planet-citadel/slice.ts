# WarpSlice 1.0 - Beta 03/19/03: Pink
# Set's Avoids while going and replots course, on anomolys and density.
# Note: Avoids Stardock Sectors and Fedspace at startup.
# Note: Avoids Ship Number Planet Number Sectors at startup.

# Setup
:Setup
# Leave Ship Above Planet
	setVar $currentship 1
# Start In This Ship
	setVar $escapeship 6
# Max Density 
	setVar $avoiddensity 499
# StarDock Sector
	setVar $stardock 3106

# Get Turns
:GetTurnAmount
send "i"
setTextLineTrigger GetTurnAmount :getturns "Turns left"
pause
:getturns
killAllTriggers
getWord CURRENTLINE $currentturns 4
waitFor "Command [TL="
#Avoid Fed Sectors
:AvoidFed
	send "^s1*s2*s3*s4*s5*s6*s7*s8*s9*s10*q"
	send "^s" $stardock "*s" $currentship "*s" $escapeship "*q"
# Main
:Main
send "d"
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
settexttrigger pastminedsector :pastminedsector "Command [TL="
pause
:minedsector
send "y"
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
			send "x" $escapeship "*"
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
echo ANSI_11 "**WarpSlice path: "
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
killalltriggers
setVar $warploop 1
setTextLineTrigger photonhit :photonhit "damaging your ship."
setTextLineTrigger NoTurnsLeft :NoTurnsLeft "You don't have any turns left!"
if ($distance = 2)
   Goto :SingleStepMode
end
:SliceLoop
if ($warploop < $distance)
	add $warploop 1	
	getsector $warppath[$warploop] $sectorinfo
	if ($sectorinfo.ANOMOLY = "YES")	
		send "^s" $warppath[$warploop] "*q"
		waitfor "Command [TL="
		ECHO ANSI_12 "*Anomoly in Sector: " $warppath[$warploop] "*"
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $engageprompt 0
		killalltriggers
		goto :WarpPathBuilder
	end
	if ($sectorinfo.DENSITY > $avoiddensity)
		send "^s" $warppath[$warploop] "*q"
		waitfor "Command [TL="
		ECHO ANSI_12 "*Density In Sector!: " $warppath[$warploop] " of " $sectorinfo.DENSITY "*"
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $engageprompt 0
		killalltriggers
		goto :WarpPathBuilder
	end
send "m" $warppath[$warploop] "*atny9999**f9*cdsd"
waitFor "Long Range Scan"
waitFor "Command [TL="
goto :SliceLoop
end
goto :Renew
:SingleStepMode
getsector $destination $sectorinfo
:MoveIn
if ($sectorinfo.ANOM = "YES")
	send "^s" $destination "*q"
	waitfor "Command [TL="
	ECHO ANSI_12 "**Anomoly in Sector: " $destination "*"
	killalltriggers
	goto :Main
end
if ($sectorinfo.DENSITY > $avoiddensity)
	send "^s" $destination "*q"
	waitfor "Command [TL="
	ECHO ANSI_12 "*Density In Sector: " $destination " of " $sectorinfo.DENSITY "*"
	killalltriggers
	goto :Main
end
send "m" $destination "*atny9999**f9*cdsd"
waitFor "Long Range Scan"
waitFor "Command [TL="
:Renew
killalltriggers
send "i"
setTextLineTrigger GetTurnAmount2 :getturns2 "Turns left"
pause
:getturns2
killAllTriggers
getWord CURRENTLINE $currentturns 4
waitFor "Command [TL="
setVar $currentsector $destination
goto :Main
:noRoute
killalltriggers
send "nq"
send "' no route to " $destination "*"
goto :Main
:NoTurnsLeft
:photonhit
send "'.pbot come*"
send "'i just got hit by a photon.*"
:END
