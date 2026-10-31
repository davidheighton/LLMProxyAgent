# WarpGrid 1.0 - Beta : Pink, Grid Macro/Code: Gravedigr, Xide
# Set's Avoids while going and replots course, on anomolys and density.
# Warning: Avoid Stardock Sectors and Fedspace before running.
# Note: Last Sector you were in will show density with planet
#       and planet ship still in sector.


# Setup
:Setup
# Leave Ship Above Planet
	setVar $planetship 6
# Start In This Ship
	setVar $walkship 1
# Planet to use
	setVar $planet 6
# Max Density 
	setVar $avoiddensity 450
# Escape Sector
	setVar $escapesector 299
# StarDock Sector
	setVar $stardock 3106

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Citadel")
        clientMessage "This script must be run from a Citadel"
        halt
end

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
		ECHO ANSI_3 "density: " ANSI_4 $sectorstate.DENSITY ANSI_9 " - "
		ECHO ANSI_3 "warps: " ANSI_4 $sectorstate.WARPS ANSI_9 " ]"
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
echo ANSI_11 "**WarpGrid path: "
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
	goto :Grid
end
:EngageNow
if ($engageprompt = 1)
echo ANSI_12 " Engage Now? "
GetConsoleInput $engage YES
if ($engage = "Y")
	goto :Grid
end
else
	goto :Grid
end
goto :Main


:Grid
setVar $warploop 1

:GridLoop
if ($distance = 2)
   Goto :SingleStepMode
end
if ($warploop < $distance)
	add $warploop 1
	
	# Gridding script by Gravedigr and Xide
	# Macro to save ship: b4577*yx60**l86*c

	getsector $warppath[$warploop] $sectorinfo
	setvar $dens $sectorinfo.DENSITY
	send "qq"
	waitfor "Command [TL="
	send "sd"
	waitfor "Command [TL="
	getsector $warppath[$warploop] $sectorinfo1
	setvar $dens1 $sectorinfo1.DENSITY
	setvar $anom1 $sectorinfo1.ANOMOLY
	if ($anom1 = "YES")	
		send "l" $planet "*cs' anomoly in " $warppath[$warploop] "*"
		send "^s" $warppath[$warploop] "*q"
		waitFor "Citadel command"
		ECHO ANSI_12 "**Anomoly in That Sector!**"
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $engageprompt 0
		goto :WarpPathBuilder
	end
	if ($dens1 > $dens) or ( $dens1 > $avoiddensity)
		send "l" $planet "*cs' density change in " $warppath[$warploop] "*"
		send "^s" $warppath[$warploop] "*q"
		waitFor "Citadel command"
		ECHO ANSI_11 "**Density in that Sector!:" $dens1 "**"
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $engageprompt 0
		goto :WarpPathBuilder
	else
		send $warppath[$warploop] "* at9999** f9*cdsdx " $planetship "**  l" $planet "*cp" $warppath[$warploop] "*yqq x" $walkship "**sdl" $planet "*c"
		waitFor "Citadel command"
		waitFor "Citadel command"
		# *h1t4*cq
	end
	goto :GridLoop
end
goto :Renew

:SingleStepMode
getsector $destination $sectorinfo
setvar $dens $sectorinfo.DENSITY
send "qq"
waitfor "Command [TL="
send "sd"
waitfor "Command [TL="
getsector $destination $sectorinfo1
setvar $dens1 $sectorinfo1.DENSITY
setvar $anom1 $sectorinfo1.ANOMOLY
:MoveIn
if ($anom1 = "YES")
	send "l" $planet "*cs' anomoly in " $destination "*"
	send "^s" $destination "*q"
	waitFor "Citadel command"
	waitFor "Citadel command"
	waitFor "Citadel command"
	ECHO ANSI_12 "**Anomoly in That Sector!**"
	goto :Main
end
if ($dens1 > $dens) or ($dens1 > $avoiddensity)
	send "l" $planet "*cs' density change in " $destination "*"
	send "^s" $destination "*q"
	waitFor "Citadel command"
	waitFor "Citadel command"
	waitFor "Citadel command"
	ECHO ANSI_11 "**Density in that Sector!:" $dens1 "**"
	goto :Main
else
	send $destination "* at9999** f9*cdsdx " $planetship "**  l" $planet "*cp" $destination "*yqq x" $walkship "**sdl" $planet "*c"
	waitFor "Citadel command"
	waitFor "Citadel command"
	# *h1t4*cq
	
end

:Renew
# Get Turns

	send "i"
	setTextLineTrigger GetTurnAmount2 :getturns2 "Turns left"
	pause
	:getturns2
	killAllTriggers
	getWord CURRENTLINE $currentturns 4
	stripText $currentturns ","
waitFor "Citadel command"

#send "s"
#waitFor "Citadel command"
setVar $currentsector $destination
goto :Main
:noRoute
killalltriggers
send "nq"
send "' no route to " $destination "*"
goto :Main

:END
