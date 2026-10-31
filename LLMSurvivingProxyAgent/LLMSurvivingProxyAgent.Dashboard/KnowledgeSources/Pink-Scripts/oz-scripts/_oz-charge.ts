# oz-charge
# optional pickup


setVar $OZ_Charge_Version "OZ-Charge 2.0"

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Command")
        clientMessage "This script must be run from the Command Prompt"
        halt
end

loadVar $ozchargeSaved

if ($ozchargeSaved)
	loadVar $figs
	loadVar $type
	loadVar $mines
	loadVar $limp
	loadVar $photon
	loadVar $pickup
	loadVar $saveme
	setVar $TWarp "OFF"
else
	setVar $sec_list 0
	setVar $figs 1
	setVar $type D
	setVar $mines 0
	setVar $limp 0
	setVar $photon "OFF"
	setVar $pickup "OFF"
	setVar $saveme "N/A"
	setVar $twarp "OFF"	

	saveVar $figs
	saveVar $type
	saveVar $mines
	saveVar $limp
	saveVar $photon
	saveVar $pickup
	saveVar $saveme
	
	
	setVar $ozchargeSaved 1
	saveVar $ozchargeSaved
end

addMenu "" "OZ-Charge" "OZ-Charge Settings" "." "" "Main" FALSE
addMenu "OZ-Charge" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-Charge" "Sector List" "Sector List" "1" :Menu_List "" FALSE
addMenu "OZ-Charge" "Figs to Lay" "Figs to Lay" "2" :Menu_Figs "" FALSE
addMenu "OZ-Charge" "Type of Figs" "Type of Figs" "3" :Menu_Type "" FALSE
addMenu "OZ-Charge" "Mines to Lay" "Mines to Lay" "4" :Menu_Mines "" FALSE
addMenu "OZ-Charge" "Limpets to Lay" "Limpets to Lay" "5" :Menu_Limps "" FALSE
addMenu "OZ-Charge" "Pickup-Photon" "Pickup-Photon" "6" :Menu_Photon "" FALSE
addMenu "OZ-Charge" "Pickup-Done" "Pickup-Done" "7" :Menu_Done "" FALSE
addMenu "OZ-Charge" "Save-Me Call" "Save-Me Call" "8" :Menu_Saveme "" FALSE
addMenu "OZ-Charge" "TWarp-Done" "TWarp-Done" "9" :Menu_Twarp "" FALSE


setMenuHelp "Run" "This Option Activates the Script."
setMenuHelp "Sector List" "This Option Sets the sectors to clear.*Please enter the sectors seperated by a semicolon.*Example - 399;455;677"
setMenuHelp "Figs to Lay" "This Option Sets the number of figs to lay."
setMenuHelp "Type of Figs" "This Option Sets the type of figs."
setMenuHelp "Mines to Lay" "This Option Sets the number of mines to lay."
setMenuHelp "Limpets to Lay" "This Option Sets the number of limpets to lay."
setMenuHelp "Pickup-Photon" "This Option Sets the Save-Me call to sound when photoned."
setMenuHelp "Pickup-Done" "This Option Sets the Save-Me call to sound when you arrive at last sector."
setMenuHelp "Save-Me Call" "This Option Sets subspace call for Auto-Pickup.*If you need the sector included enter it like this - 'sector'.* Example in sector 444-* saveme 'sector':: - would send - saveme 444::"
setMenuHelp "TWarp-Done" "This Option Sets a Sector to TWarp to after last charge sector is cleared."

gosub :sub_setMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"

:OZ_ChargeHeader
ECHO ANSI_2 "*----------------------------------*"
ECHO ANSI_10 "           " $OZ_Charge_Version " "
ECHO ANSI_2 "*----------------------------------*"
return

:Menu_List
getInput $sec_list "Enter the sectors to clear seperated by a semicolon."
saveVar $sec_list
gosub :sub_SetMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"

:Menu_Figs
getInput $figs "Enter the number of figs to lay per sector."
isNumber $number $figs
	if ($number = 0)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Figs
	end
saveVar $figs
gosub :sub_SetMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"

:Menu_Type
getInput $type "Enter the type of figs to lay (D)efensive, (O)ffensive, or (T)oll."
saveVar $type
gosub :sub_SetMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"

:Menu_Mines
getInput $mines "Enter the number of mines to lay per sector."
isNumber $number $mines
	if ($number = 0)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Mines
	end
saveVar $mines
gosub :sub_SetMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"

:Menu_Limps
getInput $limp "Enter the number of limpets to lay per sector."
isNumber $number $limp
	if ($number = 0)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Limps
	end
saveVar $limp
gosub :sub_SetMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"

:Menu_Photon
add $menu_photon 1
if ($menu_photon = 1)
	setVar $photon "ON"
elseif ($photon = "ON")
	setVar $menu_photon 0
	setVar $photon "OFF"
else
	setVar $menu_photon 0
	setVar $photon "OFF"
end
saveVar $photon
gosub :sub_SetMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"

:Menu_Done
add $menu_pickup 1
if ($menu_pickup = 1)
	setVar $pickup "ON"
elseif ($pickup = "ON")
	setVar $menu_pickup 0
	setVar $pickup "OFF"
else
	setVar $menu_pickup 0
	setVar $pickup "OFF"
end
saveVar $pickup
gosub :sub_SetMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"

:Menu_Twarp
add $menu_twarp 1
if ($menu_twarp = 1)
	getInput $twarp "Enter Sector to TWarp to when done."
	isNumber $number $twarp
	if ($number = 0)
		echo ANSI_12 "*Invalid Number*"
		setVar $menu_twarp 0
		goto :Menu_Twarp
	end
	setVar $menu_pickup 0
	setVar $pickup "OFF"
else
	setVar $menu_twarp 0
	setVar $twarp "OFF"
end
saveVar $pickup
gosub :sub_SetMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"

:Menu_Saveme
getInput $saveme "Enter saveme call-use- 'sector' -for the sector number"
saveVar $saveme
gosub :sub_SetMenu
gosub :OZ_ChargeHeader
openMenu "OZ-Charge"




:sub_setMenu
	setMenuValue "Sector List" $sec_list
	setMenuValue "Figs to Lay" $figs
	setMenuValue "Type of Figs" $type
	setMenuValue "Mines to Lay" $mines
	setMenuValue "Limpets to Lay" $limp
	setMenuValue "Pickup-Photon" $photon
	setMenuValue "Pickup-Done" $pickup
	setMenuValue "Save-Me Call" $saveme
	setMenuValue "TWarp-Done" $twarp
	
return

:Menu_Run
send "V"
setTextLineTrigger sd :getSDsec1 "The StarDock is located in sector"
pause
:getSDsec1
getWord CURRENTLINE $SD 7
stripText $SD "."

:Main
send "d"
setTextLineTrigger SectorCheck :SectorCheck "Sector  :"
pause
:SectorCheck
killalltriggers
setVar $SectorLine CURRENTLINE
getWord $SectorLine $currentsector 3
getSector $currentsector $info
setVar $charge[0] $currentsector

:parse_list
setVar $parse 0
replaceText $sec_list ";" " "
:parse_it
add $parse 1
getWord $sec_list $charge[$parse] $parse
if ($charge[$parse] = 0)
	subtract $parse 1
	goto :end_parse
else
	goto :parse_it
end
:end_parse
setVar $from_count 0
setVar $to_count 1
setArray $warppath 200
setVar $plot 1
# -=-=-=-=-- warp course getter -=-=-=-=-=-=-=-=-
:WarpPathBuilder
setVar $distance 2
send "^f" $charge[$from_count] "*" $charge[$to_count] "*q"
setTextlinetrigger BuildPath :BuildPath $charge[$from_count]&" > "
setTextLineTrigger NoRoute :NoRoute "No route within"
pause
:BuildPath
killAllTriggers
setVar $routeline CURRENTLINE
striptext $routeline " >"
striptext $routeline "("
striptext $routeline ")"
:PathLoop
getWord $routeline $warppath[$plot] $distance
if ($warppath[$plot] = $charge[$to_count])
	add $plot 1
	add $distance 1
	Goto :gotPath
end
if ($warppath[$plot] = "0")
	settextlinetrigger NextLine :NextLine " "
	pause
end
add $plot 1
add $distance 1
goto :PathLoop
:NextLine
setVar $distance 1
setVar $routeline CURRENTLINE
striptext $routeline " >"
striptext $routeline ")"
striptext $routeline "("
goto :PathLoop

:gotPath
if ($to_count = $parse)
	goto :done_course
end
add $from_count 1
add $to_count 1
goto :WarpPathBuilder

# -=-=-=-=-=-=-=- got warp data -=--=-=-=-=-=-=-=-=-
:done_course
# send "'Charge!!!*"
:Mower
setVar $move_count 0
killalltriggers
if ($photon = "ON")
	setTextLineTrigger photonhit :photonhit "damaging your ship."
	setTextLineTrigger NoTurnsLeft :NoTurnsLeft "You don't have any turns left!"
end
:bang_it
add $move_count 1
if ($move_count = $plot)
	goto :at_last_sec
end
send $warppath[$move_count] "*zea999555*zn"
if ($warppath[$move_count] > 10) and ($warppath[$move_count] <> $sd) and ($mines > 0) and ($limp > 0) and ($figs > 0)
	send "h1z" $mines "*zcznh2z" $limp "*zcznf  z" $figs "*zc" $type "zn"
elseif ($warppath[$move_count] > 10) and ($warppath[$move_count] <> $sd) and ($mines = 0) and ($limp > 0) and ($figs > 0)
	send "h2z" $limp "*zcznf  z" $figs "*zc" $type "zn"
elseif ($warppath[$move_count] > 10) and ($warppath[$move_count] <> $sd) and ($mines > 0) and ($limp = 0) and ($figs > 0)
	send "h1z" $mines "*zcznf  z" $figs "*zc" $type "zn"
elseif ($warppath[$move_count] > 10) and ($warppath[$move_count] <> $sd) and ($mines = 0) and ($limp = 0) and ($figs > 0)
	send "f  z" $figs "*zc" $type "zn"
end
goto :bang_it

# -=-=-=-=-=-=-=-=- made it to last sec -=-=-=-=-=-=-=-=-
:at_last_sec
if ($pickup = "ON")
	goto :save_me
elseif ($menu_twarp = 1)
	goto :twarp
end
halt



# -=-=-=-=-=-=-=-twarp subroutine-=-=-=-=-=-=-=-=-=-
:twarp
send "m" $twarp "*y"
setTextLineTrigger twarp_lock :twarp_lock "TransWarp Locked"
setTextLineTrigger no_twrp_lock :no_twarp_lock "No locating beam found"
setTextLineTrigger twarp_adj :twarp_adj "<Set NavPoint>"
setTextLineTrigger no_ore :no_ore "You do not have enough Fuel Ore"
pause


:no_ore
send "'No Fuel for Twarp*"
halt


:twarp_adj
send "**"
killAllTriggers
halt

:twarp_lock
KillAlltriggers
send "y*"
waitFor "Warps to Sector(s)"
halt

:no_twarp_lock
killAllTriggers
send "n*"
send "'No fig lock for return twarp*"
halt
# -=-=-=-=-=-=- save me -=-=-=-=-=-=-=-=-
:save_me
send "/"
waitFor " Sect"
getWord CURRENTLINE $savesec 2
stripText $savesec "¦"
stripText $savesec "T"
stripText $savesec "u"
stripText $savesec "r"
stripText $savesec "n"
stripText $savesec "s"
replaceText $saveme "'sector'" $savesec
send "'" $saveme "*"
halt


# -=-=-=-=-=-=- no route in course -=-=-=-=-=-=-=
:noRoute
killalltriggers
send "nq"
send "' no route to " $charge[$to_count] "*"
halt



# -=-=-=-=-=-=- photoned !! -=-=-=-=-=-=-=-=-
:NoTurnsLeft
:photonhit
goto :save_me
