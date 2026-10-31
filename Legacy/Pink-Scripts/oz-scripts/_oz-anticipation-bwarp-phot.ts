# OZ beam anticipation photon - for those pesky chargers
# see if we can run here...

setVar $oz_bantphot_version "OZ-Beam Anticipation Photon 2.0"

cutText CURRENTLINE $location 1 7
striptext $location " "
if ($location = "Citadel")
	goto :start_info
else
	echo ANSI_12 "**NEED TO BE AT CIT PROMPT!!**"
	send "'OZ Anticipation Beam Photon: Need to be at Citadel Prompt...*"
	send "'OZ Anticipation Beam Photon: Powering Down...*"
	halt
end

:start_info

loadVar $OzbantphotSaved

if ($ozbantphotSaved)
	loadVar $scan
	loadVar $return
	loadVar $delay
else 
	setVar $auto_scan "OFF"
	setVar $auto_return "OFF"
	setVar $scan 0
	setVar $return 0
	setVar $delay 1

	saveVar $scan
	saveVar $return
	saveVar $delay

	setVar $ozbantphotSaved 1
	saveVar $ozbantphotSaved
end

addMenu "" "OZ-Beam Anticipation Photon" "OZ-Beam Anticipation Photon Settings" "." "" "Main" FALSE
addMenu "OZ-Beam Anticipation Photon" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-Beam Anticipation Photon" "Auto-Scan" "Auto-Scan" "S" :Menu_Scan "" FALSE
addMenu "OZ-Beam Anticipation Photon" "Auto-Return"  "Auto-Return" "R" :Menu_Return "" FALSE
addMenu "OZ-Beam Anticipation Photon" "Delay-ms" "Delay-ms" "D" :Menu_Delay "" FALSE

setMenuHelp "Run" "This Option Activates the Beam Anticipation Photon."
setMenuHelp "Auto-Scan" "This Option Toggles the Holo-Scan SubSpace Broadcast After Photon."
setMenuHelp "Auto-Return" "This Option Toggles the Return Home After Photon."
setMenuHelp "Delay-ms" "This Option Sets the Manual Fire Delay in ms."

:start_menu
gosub :sub_setMenu
gosub :ozHeader
openMenu "OZ-Beam Anticipation Photon"



:ozHeader
ECHO ANSI_2 "*----------------------------------------*"
ECHO ANSI_10 "    " $oz_bantphot_version "       "
ECHO ANSI_2 "*----------------------------------------*"
return

:Menu_Scan
add $scan 1
	if ($scan = 1)
		setVar $auto_scan "ON"
	elseif ($scan = 0) 
		setVar $scan 0
		setVar $auto_scan "OFF"
	else
		setVar $scan 0
		setVar $auto_scan "OFF"
	end
saveVar $scan
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-Beam Anticipation Photon"

:Menu_Return
add $return 1
	if ($return = 1)
		setVar $auto_return "ON"
	elseif ($return = 0) 
		setVar $return 0
		setVar $auto_return "OFF"
	else
		setVar $return 0
		setVar $auto_return "OFF"
	end
saveVar $return
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-Beam Anticipation Photon"

:Menu_Delay
getInput $delay "Enter the fire delay ms time"
isNumber $number $delay
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Delay
	end
saveVar $delay
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-Beam Anticipation Photon"


:sub_setMenu
	if ($scan = 1)
		setVar $auto_scan "ON"
	elseif ($scan = 0) 
		setVar $scan 0
		setVar $auto_scan "OFF"
	end
	if ($return = 1)
		setVar $auto_return "ON"
	elseif ($return = 0) 
		setVar $return 0
		setVar $auto_return "OFF"
	end
	setMenuValue "Auto-Scan" $auto_scan
	setMenuValue "Auto-Return" $auto_return
	setMenuValue "Delay-ms" $delay
return

:menu_run
:mode5

send "' Activating OZ Anticipation Beam Photon*"
waitFor "(?="
:get_info
send "I"
setTextLineTrigger home :home "Current Sector :"
setTextLineTrigger ptorps :ptorps5 "Photon Missiles:"
setTextLineTrigger notorps :notorps5 "Credits"
pause

:home
getWord CURRENTLINE $homesec 4
pause

:notorps5
echo ANSI_12 "**No Photons!!!**"
halt

:ptorps5
killtrigger notorps
getWord CURRENTLINE $ptorps 3
send "q"
:getplanetnum
send "D"
setTextLineTrigger pnum :pnum "Planet #"
pause

:pnum
getWord CURRENTLINE $planet 2
stripText $planet "#"
send "c"
send "qq"

:letswait
:refresh
:glist
:get_da_figs
setVar $fig_count 0
send "'OZ-Anticipation Beam Photon: Fig refresh in progress...*"
send "G"
:update
setVar $fig_sec 0
setArray $figgies SECTORS
waitfor "==============="
:get_figs
getWord CURRENTLINE $sector 1
getWord CURRENTLINE $stopper 2
isNumber $secnum $sector
if ($secnum = 1)
	add $fig_count 1
	setVar $figgies[$sector] 1
end
if ($stopper = "Total")
	goto :doneglist
end
setTextLineTrigger grab :get_figs
pause


:doneglist
send "'OZ-Anticipation Beam Photon: Fig refresh complete*"
setVar $total_figs $fig_count
multiply $fig_count 100
divide $fig_count SECTORS
send "'OZ-Anticipation Beam Photon: Fig count: " $total_figs " sector(s) : " $fig_count "%*"
send "l" $planet "*mnt*tnt1*c"
:hehe5
killTrigger hitfig
setTextLineTrigger hitfig :get_distance5 "Deployed Fighters Report Sector"
pause

:get_distance5
getWord CURRENTLINE $activated_sector 5
stripText $activated_sector ":"
getWord CURRENTLINE $deadFig 5
stripText $deadfig ":"
setVar $figgies[$deadfig] 0

:checkforwarps
getSector $activated_sector $info

if ($info.warps = 2) 
	goto :checkfig
	
else
	goto :hehe5
end

:checkfig
setVar $adjs 0

if ($figgies[$info.warp[1]] = 1)
	setVar $target $info.warp[1]
	goto :check1
elseif ($figgies[$info.warp[2]] = 1)
	setVar $target $info.warp[2]
	goto :check2
else
	goto :hehe5
end

:check1
getSector $info.warp[1] $chk
:subcheck1
add $adjs 1
if ($adjs > $chk.warps)
	goto :hehe5
elseif ($figgies[$chk.warp[$adjs]] = 1)
	setVar $beamme $chk.warp[$adjs]
	goto :beam5
else
	goto :subcheck1
end

:check2
getSector $info.warp[2] $chk
:subcheck2
add $adjs 1
if ($adjs > $chk.warps)
	goto :hehe5
elseif ($figgies[$chk.warp[$adjs]] = 1)
	setVar $beamme $chk.warp[$adjs]
	goto :beam5
else
	goto :subcheck2
end


:beam5
	killAllTriggers
	send "b" $beamme "*"
	setTextTrigger go :go5 "TransWarp Locked"
	setTextTrigger no :no5 "No locating beam found"
	pause

:no5
killTrigger go
send "n"
waitfor "Transporter shutting down."
setVar $figgies[$beamme] 0
goto :hehe5

:go5
killTrigger no
setDelayTrigger lag :lag $delay
pause
:lag
send "ycpy" $target "*q"
subtract $ptorps 1
send "'Anticipation Photon Fired - Sector " $target "!*"
if ($scan = 1)
	goto :scanme
elseif ($scan = 0) and ($return = 1)
	goto :subno5
else 
	goto :restock5
end





:scanme
setvar $liner 1
send "sh"
settextlinetrigger scan :scan "One turn deducted,"
pause

:scan
if ($newlocation = "Warps")
	goto :messageit 
else 
	settextlinetrigger scanner :scanner
	pause
end

:scanner
if (CURRENTLINE <> "")
	setvar $sline[$liner] CURRENTLINE
	cutText CURRENTLINE $newlocation 1 5
	striptext $newlocation " "
	add $liner 1 
end  
goto :scan  

:messageit
setvar $linecount 1
setvar $linecounter 1
send "'OZ-Anticipation Beam Photon: Stand by for Photon Scan :*"
send "'*"
:linecount
if ($linecount < $liner) and ($linecounter < 20)
	send $sline[$linecount] "*"
	add $linecount 1
	add $linecounter 1
	goto :linecount
elseif ($linecounter >= 20)
	send "*"
	send "'*"
	send $sline[$linecount] "*"
	add $linecount 1
	setvar $linecounter 1 
	goto :linecount
else      
	send "*"
end
if ($return = 1)
	goto :subno5
else 
	goto :restock5
end

:subno5
send "d"
waitFor "<Re-Display>"
send "m" $homesec "*y"
SetTextLineTrigger homelock :homelock5 "TransWarp Locked"
setTextLineTrigger adj :adj5 "<Set NavPoint>"
setTextLineTrigger nohomelock :nohomelock5 "No locating beam found"
setTextLineTrigger outof :nohomelock5 "not have enough Fuel"
pause

:nohomelock5
send "n"
echo ANSI_12 "**Can't Get Home!!!**"
halt




:homelock5
killTrigger adj
killTrigger nohomelock
killTrigger outof
send "y"
:nonadj5
waitFor "Warps to Sector(s)"
goto :restock5

:adj5
killTrigger homelock
killTrigger nohomelock
killTrigger outof
send "q"
goto :restock5

:restock5
send "'OZ-Anticipation Beam Photon Powering Down!*"
halt

