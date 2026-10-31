# PMower 2.0 - Beta 05/05/03: Pink
# Set's Avoids while going and replots course, on anomolys and density.
# Note: Avoids Stardock Sectors and Fedspace at startup.
# Note: Avoids Ship Number Planet Number Sectors at startup.

setVar $PMower_Version "PMower 2.0beta - By Pink 05/05/03"

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Command")
        clientMessage "This script must be run from the Command Prompt"
        halt
end

loadVar $PMowerSaved

if ($PMowerSaved)
	loadVar $PMower_Escape
	loadVar $PMower_Density
	loadVar $PMower_DropFigs
	loadVar $PMower_Dock
else
	setVar $PMower_Escape 0
	setVar $PMower_Density 499
	setVar $PMower_DropFigs 1
	setVar $PMower_Dock 1
	
	saveVar $PMower_Escape
	SaveVar $PMower_Density
	saveVar $PMower_DropFigs
	saveVar $PMower_Dock
	
	setVar $PMowerSaved 1
	saveVar $PMowerSaved
end

addMenu "" "PMower" "PMower Settings" "." "" "Main" FALSE
addMenu "PMower" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "PMower" "StarDock" "StarDock" "S" :Menu_Dock "" FALSE
addMenu "PMower" "Escape" "Escape Ship" "E" :Menu_Escape "" FALSE
addMenu "PMower" "Density" "Avoid Density" "A" :Menu_Density "" FALSE
addMenu "PMower" "DropFigs" "Drop Figs" "D" :Menu_DropFigs "" FALSE

setMenuHelp "Run" "This Option Puts Script into Runtime mode."
setMenuHelp "StarDock" "This Option Sets the Stardock Sector for Avoid During Runtime."
setMenuHelp "Escape" "This Option Sets Escape Ship Number to Transport to if X Option is Used."
setMenuHelp "Density" "This Option Sets the Density at which you will avoid a sector rather than entering."
setMenuHelp "DropFigs" "This Option sets the amount of fighters you will drop per sector."

gosub :sub_setMenu
gosub :PHeader
openMenu "PMower"

:PHeader
ECHO ANSI_2 "*----------------------------------*"
ECHO ANSI_10 " " $PMower_Version " "
ECHO ANSI_2 "*----------------------------------*"
return

:Menu_Dock
getInput $PMower_Dock "Enter StarDock Sector."
saveVar $PMower_Dock
gosub :sub_SetMenu
gosub :PHeader
openMenu "PMower"

:Menu_Escape
getInput $PMower_Escape "Enter the Number of ship to Escape too."
saveVar $PMower_Escape
gosub :sub_SetMenu
gosub :PHeader
openMenu "PMower"

:Menu_Density
getInput $PMower_Density "Enter the Density to Avoid."
saveVar $PMower_Density
gosub :sub_SetMenu
gosub :PHeader
openMenu "PMower"

:Menu_DropFigs
getInput $PMower_DropFigs "Enter the amount of fighters to drop."
saveVar $PMower_DropFigs
gosub :sub_SetMenu
gosub :PHeader
openMenu "PMower"

:sub_setMenu
	setMenuValue "Escape" $PMower_Escape
	setMenuValue "Density" $PMower_Density
	setMenuValue "DropFigs" $PMower_DropFigs
	setMenuValue "StarDock" $PMower_Dock
return

:Menu_Run
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
	send "^s" $PMower_Dock "*s" $PMower_Escape "*q"
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
		ECHO ANSI_3 "density: " ANSI_12 SECTOR.DENSITY[$currentwarps[$displaycheckloop]] ANSI_9 " - "
		ECHO ANSI_3 "warps: " ANSI_12 SECTOR.WARPCOUNT[$currentwarps[$displaycheckloop]] ANSI_9 " ]"
		add $displaycheckloop 1
		goto :DisplayCheckLoop
	:DoneDisplayCheckLoop
	getInput $destination "[ Enter Sector to Route ]: "
	isnumber $canroute $destination
	if ($canroute = 0)
		if ($destination = "end") or ($destination = "q") or ($destination = "Q") or ($destination = "quit")
			goto :End
		end
		if ($destination = "x") or ($destination = "escape") or ($destination = "X")
			send "x" $PMower_Escape "*"
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
setArray $warppath 45
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
echo ANSI_11 "**WarpMower path: "
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
	goto :Mower
end
:EngageNow
if ($engageprompt = 1)
echo ANSI_12 " Engage Now? "
GetConsoleInput $engage YES
if ($engage = "Y") or ($engage = "y")
	goto :Mower
end
else
	goto :Mower
end
goto :Main
:Mower
killalltriggers
setVar $warploop 1
setTextLineTrigger photonhit :photonhit "damaging your ship."
setTextLineTrigger NoTurnsLeft :NoTurnsLeft "You don't have any turns left!"
if ($distance = 2)
   Goto :SingleStepMode
end
:MowerLoop
while ($warploop < $distance)
	add $warploop 1	
	if (SECTOR.DENSITY[$warppath[$warploop]] > $PMower_Density)
		send "^s" $warppath[$warploop] "*q"
		waitfor "Command [TL="
		ECHO ANSI_12 "*Density In Sector!: " $warppath[$warploop] " of " SECTOR.DENSITY[$warppath[$warploop]] "*"
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $engageprompt 0
		killalltriggers
		goto :WarpPathBuilder
	end
send "m" $warppath[$warploop] "*atny9999**f" $PMower_DropFigs "*cdsd"
waitFor "Relative Density Scan"
waitFor "Command [TL="
end
goto :Renew
:SingleStepMode
if (SECTOR.DENSITY[$destination] > $PMower_Density)
	send "^s" $destination "*q"
	waitfor "Command [TL="
	ECHO ANSI_12 "*Density In Sector: " $destination " of " SECTOR.DENSITY[$destination] "*"
	killalltriggers
	goto :Main
end
send "m" $destination "*atny9999**f" $PMower_DropFigs "*cdsd"
waitFor "Relative Density Scan"
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
#UnAvoid Fed Sectors
:UnAvoidFed
send "^c1*c2*c3*c4*c5*c6*c7*c8*c9*c10*q"
send "^c" $PMower_Dock "*c" $PMower_Escape "*q"
