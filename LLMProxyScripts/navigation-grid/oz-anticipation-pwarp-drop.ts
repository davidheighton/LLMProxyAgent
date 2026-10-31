# OZ beam anticipation pwarp drop - for level 6- for those pesky chargers
# see if we can run here...

setVar $oz_anticpwd_version "OZ-Anticipation PWarp-Drop 2.0"

cutText CURRENTLINE $location 1 7
striptext $location " "
if ($location = "Citadel")
	goto :start_info
else
	echo ANSI_12 "**NEED TO BE AT CIT PROMPT!!**"
	send "'OZ Anticipation Pwarp-Drop: Need to be at Citadel Prompt...*"
	send "'OZ Anticipation Pwarp-Drop: Powering Down...*"
	halt
end

:start_info
send "i"
waitFor "Fighters       :"
getWord CURRENTLINE $figs 3
stripText $figs ","
stripText $figs " "
setTextLineTrigger photons :photons "Photon Missiles:"
setTextLineTrigger credits :credits "Credits        :"
pause

:photons
killTrigger credits
getWord CURRENTLINE $phot 3
goto :get_rest_info

:credits
killTrigger photons
setVar $phot 0
:get_rest_info
send "c;q"
waitFor "Max Fighters:"
getText CURRENTLINE $max_figs "Max Fighters:" "Offensive Odds:"
stripText $max_figs ","
stripText $max_figs " "
WaitFor "Max Figs Per Attack:"
getWord CURRENTLINE $max 5
stripText $max " "
setVar $fig_waves $max_figs
divide $fig_waves $max

loadVar $OzanticpationpwdropSaved

if ($OzanticpationpwdropSaved)
	loadVar $responder_attack
	setVar $responder_waves $fig_waves
	setVar $responder_figs $max
else 
	setVar $responder_attack "OFF"
	setVar $responder_waves $fig_waves
	setVar $responder_figs $max

	saveVar $responder_attack

	setVar $ozanticpationpwdropSaved 1
	saveVar $ozanticpationpwdropSaved
end

addMenu "" "OZ-Anticipation PWarp-Drop" "OZ-Anticipation PWarp-Drop" "." "" "Main" FALSE
addMenu "OZ-Anticipation PWarp-Drop" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-Anticipation PWarp-Drop" "Figs/Wave" "Figs/Wave" "F" :Menu_Figs "" FALSE
addMenu "OZ-Anticipation PWarp-Drop" "Waves" "Waves" "W" :Menu_Waves "" FALSE
addMenu "OZ-Anticipation PWarp-Drop" "Lift/Attack" "Lift/Attack" "A" :Menu_Attack "" FALSE

setMenuHelp "Run" "This Option Activates the Beam Anticipation Photon."
setMenuHelp "Figs/Wave" "This Option Sets the Number of Figs to use per Attack Wave."
setMenuHelp "Waves" "This Option Sets the Waves of Figs to use."
setMenuHelp "Lift/Attack" "This Option Sets the Lift and Attack."

:start_menu
gosub :sub_setMenu
gosub :ozHeader
openMenu "OZ-Anticipation PWarp-Drop"



:ozHeader
ECHO ANSI_2 "*----------------------------------------*"
ECHO ANSI_10 "    " $oz_anticpwd_version "       "
ECHO ANSI_2 "*----------------------------------------*"
return


:Menu_Figs
getInput $responder_figs "Enter the Number of Figs to Launch per Wave"
isNumber $number $responder_figs
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Figs
	end
saveVar $responder_figs
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-Anticipation PWarp-Drop"

:Menu_Waves
getInput $responder_waves "Enter the Number Waves of Figs to Launch "
isNumber $number $responder_waves
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Waves
	end
saveVar $responder_waves
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-Anticipation PWarp-Drop"


:Menu_Attack
getInput $attack "Lift and Attack after PDrop? (Y/N) "
lowercase $attack
	if ($attack = "y")
		setVar $responder_attack "ON"
		saveVar $responder_attack
		gosub :sub_setMenu
		gosub :ozheader
		openMenu "OZ-Anticipation PWarp-Drop"

	elseif ($attack = "n")
		setVar $responder_attack "OFF"
 		saveVar $responder_attack
		gosub :sub_setMenu
		gosub :ozheader
		openMenu "OZ-Anticipation PWarp-Drop"

	else
		echo ANSI_12 "*Please enter y or n*"
		goto :Menu_attack
	end

		

:sub_setMenu
	setMenuValue "Figs/Wave" $responder_figs
	setMenuValue "Waves" $responder_waves
	setMenuValue "Lift/Attack" $responder_attack
return

:menu_run
:mode5

send "' Activating OZ-Anticipation PWarp-Drop*"
waitFor "(?="
:get_info
send "I"
setTextLineTrigger home :home "Current Sector :"
pause

:home
getWord CURRENTLINE $homesec 4
send "q"
:getplanetnum
send "D"
setTextLineTrigger pnum :pnum "Planet #"
pause

:pnum
getWord CURRENTLINE $responder_planet 2
stripText $planet "#"
send "c"
send "qq"

:letswait
:refresh
:glist
:get_da_figs
setVar $fig_count 0
send "'OZ-Anticipation PWarp-Drop: Fig refresh in progress...*"
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
send "'OZ-Anticipation PWarp-Drop: Fig refresh complete*"
setVar $total_figs $fig_count
multiply $fig_count 100
divide $fig_count SECTORS
send "'OZ-Anticipation PWarp-Drop: Fig count: " $total_figs " sector(s) : " $fig_count "%*"
send "l" $responder_planet "*mnt*tnt1*c"
:hehe5
killTrigger hitfig
setTextLineTrigger hitfig :get_distance5 "Deployed Fighters Report Sector"
pause

:get_distance5
getWord CURRENTLINE $target 6
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
	goto :pwarp1
elseif ($figgies[$info.warp[2]] = 1)
	setVar $target $info.warp[2]
	goto :pwarp2
else
	goto :hehe5
end


:pwarp1
setVar $pwarpsector $info.warp[1]
gosub :pwarp_exe
if ($responder_attack = "ON")
	goto :lift_attack
else
	goto :restock5
end

:pwarp2
setVar $pwarpsector $info.warp[2]
gosub :pwarp_exe
if ($responder_attack = "ON")
	goto :lift_attack
else
	goto :restock5
end

:lift_attack
setVar $attack_wave 0
send "qq"
gosub :attack
send "l" $responder_planet "*mnt*c"
goto :lift_attack


:restock5
send "'OZ-Anticipation PWarp-Drop Powering Down!*"
halt



# -=-=-=-=-=-=-=- pwarp routine -=-=-=-=-=-=-=
:pwarp_exe
killAllTriggers
send "p" $pwarpsector "*y"
setTextLineTrigger pwarp_lock :pwarp_lock "Locating beam pinpointed"
setTextLineTrigger no_pwarp_lock :no_pwarp_lock "Your own fighters must be"
pause

:pwarp_lock
killAllTriggers
waitFor "Planet is now in sector"
return

:no_pwarp_lock
killAllTriggers
goto :hehe5


# -=-=-=-=-=-=-=-=- attack routine -=-=-=-=-=-=-=-
:attack
waitFor "(?="
killAllTriggers
if ($attack_wave >= $responder_waves) 
	goto :end_attack
end
send "ad"
:popem
killAllTriggers
setTextLineTrigger notarget :notarget "<Re-Display>"
setTextLineTrigger beacon :beacon "Destroy the Marker Beacon here?"
setTextTrigger ship :ship " (Y/N) [N]?"
pause


:ship
getWord CURRENTLINE $man1 3
getWord CURRENTLINE $man2 4
getWord CURRENTLINE $man3 5
While ($man1 = "unmanned") or ($man2 = "unmanned") or ($man3 = "unmanned")
	send "nd"
	goto :popem
end
send "y"
goto :hit

:beacon
killAllTriggers
send "y"
goto :attack

:notarget
killAllTriggers
send "*"
goto :end

:hit
send $responder_figs "*"
add $attack_wave 1
waitFor "You lost"
goto :attack

:end_attack
return




# -=-=-=-=-=-=-=-=- end it -=--=-=-=-=-=-=-=-
:end
send "'OZ-Anticipation PWarp-Drop Powering Down...*"
echo ANSI_12 "*OZ-Anticipation PWarp-Drop Powering Down...*"
halt
