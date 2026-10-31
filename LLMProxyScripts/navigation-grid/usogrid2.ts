# WarpGrid 1.0 - Beta 03/20/03: Pink, Grid By: Gravedigr, Xide
# Set's Avoids while going and replots course, on anomolys and density.
# Note: Avoids Stardock Sectors and Fedspace at startup.
# Note: Avoids Ship Number Planet Number Sectors at startup.

# Alternative Setup Mode using getInput (not Hardcoded)
# Setup
#:Setup
# Leave Ship Above Planet
#	getInput $planetship "Enter Ship Number Left Above Planet" 0
# Start In This Ship
#	getInput $walkship "Enter Ship Number You Will Be Walking With" 0
# Planet to use
#	getInput $planet "Enter Planet Number To Land On" 0
# Max Density 
#	getInput $avoiddensity "Avoid Density Above" 0
# Escape Sector
#	getInput $escapesector "Enter Safe Escape Sector (incase needed)" 0
# StarDock Sector
#	getInput $stardock "Enter Stardock Sector" 0


# Setup (Hardcoded)
:Setup
# Leave Ship Above Planet
	setVar $planetship 130	
# Start In This Ship
	setVar $walkship 22
# Planet to use
	setVar $planet 38
# Max Density 
	setVar $avoiddensity 499
# Escape Sector
	setVar $escapesector 5028
# StarDock Sector
	setVar $stardock 1232

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
waitFor "Citadel command"
#Avoid Fed Sectors
:AvoidFed
	send "^s1*s2*s3*s4*s5*s6*s7*s8*s9*s10*q"
waitFor "Citadel command"
	send "^s" $stardock "*s" $planetship "*s" $walkship "*s" $planet "*q"
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
echo ANSI_11 "*WarpGrid path: "
setvar $displayloop 1
:DisplayLoop
ECHO ANSI_15 ">" ANSI_12  $warppath[$displayloop] 
if ($displayloop = $distance)
	Goto :DisplayDone
end
add $displayloop 1
goto :DisplayLoop
:DisplayDone
if ($distance = 2)
	goto :Grid
end
:EngageNow
if ($engageprompt = 1)
echo ANSI_12 "* Engage Now? "
GetConsoleInput $engage YES
if ($engage = "Y")
	goto :Grid
end
else
	goto :Grid
end
goto :Main
:Grid
killalltriggers
setVar $warploop 1
setTextLineTrigger photonhit :photonhit "damaging your ship."
setTextLineTrigger NoTurnsLeft :NoTurnsLeft "You don't have any turns left!"
if ($distance = 2)
   Goto :SingleStepMode
end
:GridLoop
if ($warploop < $distance)
	add $warploop 1	
	getsector $warppath[$warploop] $sectorinfobefore
	send "qqsd"
	waitfor "Command [TL="
	waitfor "Command [TL="
	getsector $warppath[$warploop] $sectorinfoafter
	if ($sectorinfoafter.ANOMOLY = "YES")	
		send "l" $planet "*c^s" $warppath[$warploop] "*q"
		waitFor "Citadel command"
		waitFor "Citadel command"
		ECHO ANSI_12 "*Anomoly Sector: " $warppath[$warploop] "*"
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $engageprompt 0
		killalltriggers
		goto :WarpPathBuilder
	end
	if ($sectorinfoafter.DENSITY > $sectorinfobefore.DENSITY) or ( $sectorinfoafter.DENSITY > $avoiddensity)
		send "l" $planet "*c^s" $warppath[$warploop] "*q"
		waitFor "Citadel command"
		waitFor "Citadel command"
		ECHO ANSI_12 "*Density In Sector!: " $warppath[$warploop] " of " $sectorinfoafter.DENSITY "*"
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $engageprompt 0
		killalltriggers
		goto :WarpPathBuilder
	end
send $warppath[$warploop] "* at9999** f9*cdx " $planetship "**  l" $planet "*cp" $warppath[$warploop] "*yqq x" $walkship "**sdl" $planet "*c"
waitFor "Citadel command"
waitFor "Citadel command"
goto :GridLoop
end
goto :Renew
:SingleStepMode
getsector $destination $sectorinfobefore
send "qqsd"
waitfor "Command [TL="
waitfor "Command [TL="
getsector $destination $sectorinfoafter
:MoveIn
if ($sectorinfoafter.ANOMOLY = "YES")
	send "l" $planet "*c^s" $destination "*q"
	waitFor "Citadel command"
	waitFor "Citadel command"
	ECHO ANSI_12 "*Anomoly in Sector: " $destination "*"
	killalltriggers
	goto :Main
end
if ($sectorinfoafter.DENSITY > $sectorinfobefore.DENSITY) or ($sectorinfoafter.DENSITY > $avoiddensity)
	send "l" $planet "*c^s" $destination "*q"
	waitFor "Citadel command"
	waitFor "Citadel command"
	ECHO ANSI_12 "*Density In Sector: " $destination " of " $sectorinfoafter.DENSITY "*"
	killalltriggers
	goto :Main
end
send $destination "* at9999** f9*cdx " $planetship "**  l" $planet "*cp" $destination "*yqq x" $walkship "**sdl" $planet "*c"
waitFor "Citadel command"
waitFor "Citadel command"
:Renew
killalltriggers
send "i"
setTextLineTrigger GetTurnAmount2 :getturns2 "Turns left"
pause
:getturns2
killAllTriggers
getWord CURRENTLINE $currentturns 4
waitFor "Citadel command"
setVar $currentsector $destination
goto :Main
:noRoute
killalltriggers
send "nq"
ECHO "*No route to Sector: " $destination "*"
goto :Main
:NoTurnsLeft
:photonhit
send "'.pbot come*"
send "'i just got hit by a photon.*"
:END
