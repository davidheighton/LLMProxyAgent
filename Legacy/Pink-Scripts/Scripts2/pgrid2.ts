# WarpGrid 2.0 - Beta (06/25/03): By Pink
# Set's Avoids while going and replots course on density.
# Note: Avoids Stardock and Fedspace at startup.
# Note: Avoids Ship Number Planet Number Sectors at startup.

setVar $PGrid_Version "PGrid 2.0beta - By Pink 06/25/03"

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Citadel")
        clientMessage "This script must be run from a Citadel"
        halt
end

loadVar $PGridSaved

if ($PGridSaved)
	loadVar $PGrid_PlanetShip
	loadVar $PGrid_WalkShip
	loadVar $PGrid_Planet
	loadVar $PGrid_Density
	loadVar $PGrid_Escape
	loadVar $PGrid_EscapeMode
	loadVar $PGrid_Dock
	loadVar $PGrid_Fighters
	loadVar $PGrid_Armids
	loadVar $PGrid_Limpets
	loadVar $PGrid_GamePassword
	loadVar $PGrid_SaveLine
else
	setVar $PGrid_PlanetShip 2
	setVar $PGrid_WalkShip 1
	setVar $PGrid_Planet 3
	setVar $PGrid_Density 499
	setVar $PGrid_Escape 0
	setVar $PGrid_EscapeMode 1
	setVar $PGrid_Dock 0
	setVar $PGrid_Fighters 5
	setVar $PGrid_Armids 1
	setVar $PGrid_Limpets 1
	setVar $PGrid_GamePassword NONESET
	setVar $PGrid_SaveLine ".pbot save"
	
	saveVar $PGrid_PlanetShip
	saveVar $PGrid_WalkShip
	saveVar $PGrid_Planet
	saveVar $PGrid_Density
	saveVar $PGrid_Escape
	saveVar $PGrid_EscapeMode
	saveVar $PGrid_Dock
	saveVar $PGrid_Fighters
	saveVar $PGrid_Armids
	saveVar $PGrid_Limpets
	saveVar $PGrid_GamePassword
	saveVar $PGrid_SaveLine	

	setVar $PGridSaved 1
	saveVar $PGridSaved
end

addMenu "" "PGrid" "PGrid Settings" "." "" "Main" FALSE
addMenu "PGrid" "Run" "Run Script" "Z" :Menu_Run "" TRUE
addMenu "PGrid" "PlanetShip" "Planet Ship Number:" "X" :Menu_PlanetShip "" FALSE
addMenu "PGrid" "WalkShip" "Walk Ship Number:" "W" :Menu_WalkShip "" FALSE
addMenu "PGrid" "Planet" "Planet Number:" "P" :Menu_Planet "" FALSE
addMenu "PGrid" "Density" "Avoid Density:" "D" :Menu_Density "" FALSE
addMenu "PGrid" "Escape" "Escape Sector:" "E" :Menu_Escape "" FALSE
addMenu "PGrid" "EscapeMode" "Escape Mode:" "M" :Menu_EscapeMode "" FALSE
addMenu "PGrid" "StarDock" "StarDock:" "S" :Menu_Dock "" FALSE
addMenu "PGrid" "Deploy Fighters" "Deploy Fighters:" "F" :Menu_Fighters "" FALSE
addMenu "PGrid" "Deploy Armids" "Deploy Armids:" "A" :Menu_Armids "" FALSE
addMenu "PGrid" "Deploy Limpets" "Deploy Limpets:" "L" :Menu_Limpets "" FALSE
addMenu "PGrid" "Game Password" "Game Password:" "G" :Menu_GamePassword "" FALSE
addMenu "PGrid" "Save Line" "Supspace Save Line:" "H" :Menu_SaveLine "" FALSE

setMenuHelp "Run" "This Option Puts Script into Runtime mode."
setMenuHelp "WalkShip" "This Option sets the ship number that you will walk with same as current ship number."
setMenuHelp "PlanetShip" "This Option sets the ship number that will remain above planet to be used to control planet."
setMenuHelp "Planet" "This Option sets the planet number that will be used to walk the grid."
setMenuHelp "Escape" "This Option Sets Escape Sector Number to Transport to if X Option is Used."
setMenuHelp "Density" "This Option Sets the Density at which you will avoid a sector rather than entering."
setMenuHelp "EscapeMode" "This Option sets the mode at which X option functions to dock or to escape."
setMenuHelp "StarDock" "This Option sets the Stardock sector number."
setMenuHelp "Deploy Fighters" "This Option sets the amount of figs to deploy per sector."
setMenuHelp "Deploy Armids" "This Option sets the amount of armids to deploy on deploy command."
setMenuHelp "Deploy Limpets" "This Option sets the amount of limpets to deploy on deploy command."
setMenuHelp "Game Password" "This Option sets the game password for sector clear."
setMenuHelp "Save Line" "This Option sets the Save line to send if photoned or turns lost."

gosub :sub_setMenu
gosub :PHeader
openMenu "PGrid"

:PHeader
ECHO ANSI_2 "*----------------------------------*"
ECHO ANSI_10 " " $PGrid_Version " "
ECHO ANSI_2 "*----------------------------------*"
return

:Menu_PlanetShip
getInput $PGrid_PlanetShip "Enter Ship Number Left Above Planet:"
saveVar $PGrid_PlanetShip
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_WalkShip
getInput $PGrid_WalkShip "Enter Ship Number You Will Be Walking With:"
saveVar $PGrid_WalkShip
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_Planet
getInput $PGrid_Planet "Enter Planet Number To Land On:"
saveVar $PGrid_Planet
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_Escape
getInput $PGrid_Escape "Enter Safe Escape Sector (incase needed):"
saveVar $PGrid_Escape
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_Density
getInput $PGrid_Density "Avoid Density Above:"
saveVar $PGrid_Density
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_EscapeMode
getInput $PGrid_EscapeMode "Enter the Escape Mode: (1):Escape Sector or (2):Dock+Escape Sector"
saveVar $PGrid_EscapeMode
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_Dock
getInput $PGrid_Dock "Enter the StarDock Sector Location:"
saveVar $PGrid_Dock
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_Fighters
getInput $PGrid_Fighters "Enter the amount of Fighters to deploy per sector:"
saveVar $PGrid_Fighters
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_Armids
getInput $PGrid_Armids "Enter the amount of Armids to deploy:"
saveVar $PGrid_Armids
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_Limpets
getInput $PGrid_Limpets "Enter the amount of Limpets to deploy:"
saveVar $PGrid_Limpets
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_GamePassword
getInput $PGrid_GamePassword "Enter the amount of Limpets to deploy:"
saveVar $PGrid_GamePassword
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"

:Menu_SaveLine
getInput $PGrid_SaveLine "Enter the text to send on Subspace for Planet Save (example: .pbot save):"
saveVar $PGrid_SaveLine
gosub :sub_SetMenu
gosub :PHeader
openMenu "PGrid"


:sub_setMenu
	setMenuValue "PlanetShip" $PGrid_PlanetShip
	setMenuValue "WalkShip" $PGrid_WalkShip
	setMenuValue "Planet" $PGrid_Planet
	setMenuValue "Density" $PGrid_Density
	setMenuValue "Escape" $PGrid_Escape
	setMenuValue "EscapeMode" $PGrid_EscapeMode
	setMenuValue "StarDock" $PGrid_Dock
	setMenuValue "Deploy Fighters" $PGrid_Fighters
	setMenuValue "Deploy Armids" $PGrid_Armids
	setMenuValue "Deploy Limpets" $PGrid_Limpets
	setMenuValue "Game Password" $PGrid_GamePassword
	setMenuValue "Save Line" $PGrid_SaveLine
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
	stripText $currentturns ","
waitFor "Citadel command"

#Avoid Fed Sectors
:AvoidFed
	send "^s1*s2*s3*s4*s5*s6*s7*s8*s9*s10*q"
waitFor "Citadel command"
	send "^s" $PGrid_Dock "*s" $PGrid_PlanetShip "*s" $PGrid_WalkShip "*s" $PGrid_Planet "*q"
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
	ECHO ANSI_9 "**[" ANSI_11 "current turns:" ANSI_12 $currentturns ANSI_9 "][" ANSI_15 "q=quit" ANSI_9 "][" ANSI_15 "x=escape" ANSI_9 "][" ANSI_15 "h=holo" ANSI_9 "][" ANSI_15 "m=move" ANSI_9 "][" ANSI_15 "d=deploy" ANSI_9 "]"
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
			if ($PGrid_EscapeMode = 1)
				send "^c" $PGrid_Escape "*qb" $PGrid_Escape "*yx" $PGrid_PlanetShip "**l" $PGrid_Planet "*cp" $PGrid_Escape "*y"
			else
				send "^c" $PGrid_Dock "*qb" $PGrid_Dock "*yx*" $PGrid_PlanetShip "**l" $PGrid_Planet "*cp" $PGrid_Escape "*y"
			end
			goto :End
		end
		if ($destination = "h") or ($destination = "H") or ($destination = "holo")
			send "qq"
			waitFor "Command [TL="
			send "sd"
			waitFor "Command [TL="
			send "sh"
			waitFor "Command [TL="
			send "l" $PGrid_Planet "*c"
			waitFor "Citadel command"
		end
		if ($destination = "d") or ($destination = "D") or ($destination = "deploy")
			send "qqh1t" $PGrid_Armids "*cq*h2t" $PGrid_Limpets "*cq*l" $PGrid_Planet "*c"
			waitFor "Citadel command"		
		end
		#if ($destination = "c") or ($destination = "C") or ($destination = "clear")
		#	send "qqqynt***" $PGrid_GamePassword "**"
		#	send "h1t" $PGrid_Armids "*cq*h2t" $PGrid_Limpets "*cq*l" $PGrid_Planet "*c"
		#	waitFor "Citadel command"		
		#end
		if ($destination = "m") or ($destination = "M") or ($destination = "move")
			ECHO ANSI_12 "**[ WARNING DO NOT ENTER A SECTOR WITHOUT A FIGHTER OR YOU WILL DIE ]"
			getInput $relocatesector "[ Enter Sector to Relocate ]: "
			isnumber $canrelocate $relocatesector
			if ($canrelocate = 1)
				send "b" $relocatesector "*yx" $PGrid_PlanetShip "**l" $PGrid_Planet "*cp" $relocatesector "*yqqx" $PGrid_WalkShip "**sdl" $PGrid_Planet "*c"
				waitFor "Citadel command"
				waitFor "Citadel command"
			else			
				ECHO "*Not a Number*"
			end
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
if ($engage = "Y") or ($engage = "y")
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

:GridLoop
if ($distance = 2)
   Goto :SingleStepMode
end
while ($warploop < $distance)
	add $warploop 1	
	setvar $startdens SECTOR.DENSITY[$warppath[$warploop]]
	send "qq"
	waitfor "Command [TL="
	send "sd"
	waitfor "Command [TL="
	if (SECTOR.DENSITY[$warppath[$warploop]] > $startdens) or (SECTOR.DENSITY[$warppath[$warploop]] > $PGrid_Density)
		send "l" $PGrid_Planet "*cs' density change in " $warppath[$warploop] "*^s" $warppath[$warploop] "*q"
		waitFor "Citadel command"
		waitFor "Citadel command"
		waitFor "Citadel command"
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $engageprompt 0
		ECHO ANSI_12 "**ALERT:" ANSI_11 "*Density In Sector!: " $warppath[$warploop] " of " SECTOR.DENSITY[$warppath[$warploop]] "**"
		killalltriggers
		goto :WarpPathBuilder
	else
		send $warppath[$warploop] "* at9999** f" $PGrid_Fighters "*cdx " $PGrid_Planetship "**  l" $PGrid_Planet "*cp" $warppath[$warploop] "*yqq x" $PGrid_WalkShip "**  l" $PGrid_Planet "*c"
		waitFor "Citadel command"
		waitFor "Citadel command"
	end
end
send "qqsd"
waitFor "Relative Density Scan"
waitFor "Command [TL="
send "l" $PGrid_Planet "*c"
goto :Renew

:SingleStepMode
setvar $startdens SECTOR.DENSITY[$destination]
send "qq"
waitfor "Command [TL="
send "sd"
waitfor "Command [TL="
:MoveIn
if (SECTOR.DENSITY[$destination] > $startdens) or (SECTOR.DENSITY[$destination] > $PGrid_Density)
	send "l" $PGrid_Planet "*cs' density change in " $destination "*^s" $destination "*q"
	waitFor "Citadel command"
	waitFor "Citadel command"
	waitFor "Citadel command"
	ECHO ANSI_12 "**ALERT:" ANSI_11 "*Density In Sector: " $destination " of " SECTOR.DENSITY[$destination] "**"
	killalltriggers
	goto :Main
else
	send $destination "* at9999** f" $PGrid_Fighters "*cdx " $PGrid_PlanetShip "**  l" $PGrid_Planet "*cp" $destination "*yqq x" $PGrid_WalkShip "**  sd"
	waitFor "Citadel command"
	waitFor "Command [TL="
	waitFor "Command [TL="
	waitFor "Command [TL="
	send "l" $PGrid_Planet "*c"
	waitFor "Citadel command"
end

:Renew
killalltriggers
# Get Turns
send "i"
setTextLineTrigger GetTurnAmount2 :getturns2 "Turns left"
pause
:getturns2
killAllTriggers
getWord CURRENTLINE $currentturns 4
stripText $currentturns ","
waitFor "Citadel command"
setVar $currentsector $destination
goto :Main

:noRoute
killalltriggers
send "nq"
send "' no route to " $destination "*"
goto :Main

:NoTurnsLeft
:photonhit
send "'" $PGrid_SaveLine "*"
send "'i just got hit by a photon.*"
:END
:AvoidFed
send "^c1*c2*c3*c4*c5*c6*c7*c8*c9*c10*q"
send "^c" $PGrid_Dock "*c" $PGrid_PlanetShip "*c" $PGrid_WalkShip "*c" $PGrid_Planet "*q"