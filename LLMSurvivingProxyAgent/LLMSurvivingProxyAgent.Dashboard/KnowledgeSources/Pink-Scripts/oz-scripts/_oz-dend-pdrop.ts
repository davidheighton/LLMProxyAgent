# OZ  anticipation pwarp drop for dead ends- for level 6- for those pesky chargers that clear dead ends
# see if we can run here...

setVar $oz_dendpwd_version "OZ-Dead-End PWarp-Drop 2.0"

cutText CURRENTLINE $location 1 7
striptext $location " "
if ($location = "Citadel")
	goto :start_info
else
	echo ANSI_12 "**NEED TO BE AT CIT PROMPT!!**"
	send "'OZ-Dead-End Pwarp-Drop: Need to be at Citadel Prompt...*"
	send "'OZ-Dead-End Pwarp-Drop: Powering Down...*"
	halt
end

:start_info
send "i"
waitFor "Fighters       :"
getWord CURRENTLINE $figs 3
stripText $figs ","
stripText $figs " "
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

loadVar $OzdendpwdropSaved

if ($OzdendpwdropSaved)
	loadVar $responder_attack
	loadVar $drop
	setVar $responder_waves $fig_waves
	setVar $responder_figs $max
else 
	setVar $responder_attack "OFF"
	setVar $responder_waves $fig_waves
	setVar $responder_figs $max
	setVar $drop "Dead-End"

	saveVar $responder_attack
	saveVar $drop

	setVar $ozdendpwdropSaved 1
	saveVar $ozdendpwdropSaved
end

addMenu "" "OZ-Dead-End PWarp-Drop" "OZ-Dead-End PWarp-Drop" "." "" "Main" FALSE
addMenu "OZ-Dead-End PWarp-Drop" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-Dead-End PWarp-Drop" "Figs/Wave" "Figs/Wave" "F" :Menu_Figs "" FALSE
addMenu "OZ-Dead-End PWarp-Drop" "Waves" "Waves" "W" :Menu_Waves "" FALSE
addMenu "OZ-Dead-End PWarp-Drop" "Lift/Attack" "Lift/Attack" "A" :Menu_Attack "" FALSE
addMenu "OZ-Dead-End PWarp-Drop" "Drop Sector" "Drop Sector" "D" :Menu_Drop "" FALSE

setMenuHelp "Run" "This Option Activates the Beam Anticipation Photon."
setMenuHelp "Figs/Wave" "This Option Sets the Number of Figs to use per Attack Wave."
setMenuHelp "Waves" "This Option Sets the Waves of Figs to use."
setMenuHelp "Lift/Attack" "This Option Sets the Lift and Attack."
setMenuHelp "Drop Sector" "This Option Sets the Drop Sector."

:start_menu
gosub :sub_setMenu
gosub :ozHeader
openMenu "OZ-Dead-End PWarp-Drop"



:ozHeader
ECHO ANSI_2 "*----------------------------------------*"
ECHO ANSI_10 "    " $oz_dendpwd_version "       "
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
openMenu "OZ-Dead-End PWarp-Drop"

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
openMenu "OZ-Dead-End PWarp-Drop"

:Menu_Drop
add $menu_drop 1
if ($menu_drop = 1)
	setVar $drop "Gate"
elseif ($drop = "Gate")
	setVar $menu_drop 0
	setVar $drop "Dead-End"
else
	setVar $menu_drop 0
	setVar $drop "Dead-End"
end
saveVar $drop
gosub :sub_SetMenu
gosub :OZHeader
openMenu "OZ-Dead-End PWarp-Drop"

:Menu_Attack
getInput $attack "Lift and Attack after PDrop? (Y/N) "
lowercase $attack
	if ($attack = "y")
		setVar $responder_attack "ON"
		saveVar $responder_attack
		gosub :sub_setMenu
		gosub :ozheader
		openMenu "OZ-Dead-End PWarp-Drop"

	elseif ($attack = "n")
		setVar $responder_attack "OFF"
 		saveVar $responder_attack
		gosub :sub_setMenu
		gosub :ozheader
		openMenu "OZ-Dead-End PWarp-Drop"

	else
		echo ANSI_12 "*Please enter y or n*"
		goto :Menu_attack
	end

		

:sub_setMenu
	setMenuValue "Drop Sector" $drop
	setMenuValue "Figs/Wave" $responder_figs
	setMenuValue "Waves" $responder_waves
	setMenuValue "Lift/Attack" $responder_attack
return

:menu_run
:mode5

send "' Activating OZ-Dead-End PWarp-Drop*"
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
send "'OZ-Dead-End PWarp-Drop: Fig refresh in progress...*"
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
send "'OZ-Dead-End PWarp-Drop: Fig refresh complete*"
setVar $total_figs $fig_count
multiply $fig_count 100
divide $fig_count SECTORS
send "'OZ-Dead-End PWarp-Drop: Fig count: " $total_figs " sector(s) : " $fig_count "%*"
:check_dend
setVar $a 0
setArray $de SECTORS
setVar $dead_end_info GAMENAME & ".dend"
fileExists $exist $dead_end_info
	if ($exist = 1)
		gosub :get_de
	else
		send "'OZ-Dead-End PWarp-Drop: No Dead-End file list*"
		halt
	end
# subroutine to add non figged dead ends to the de array
:get_de
add $a 1
read $dead_end_info $desec $a
	if ($desec <> "EOF")
		if ($figgies[$desec] = 1)
			setVar $de[$desec] 1
			goto :get_de
		else
			goto :get_de
		end
	end
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

# -=-=-=-=-=- this looks for the nearest figged dead end -=-=-=-=-=-=-
:check_for_nde
gosub :nnf_find
:got_nnf_de
# -=-=-=-=- lets make sure its a dead end -=-=-=-=-=-=-=-=
getSector $jumpfig $dend_check
if ($dend_check.warps > 1)
	setVar $de[$jumpfig] 0
	goto :hehe5
end
setVar $gate $dend_check.warp[1]
if ($drop = "Dead-End")
	setVar $drop_sec $jumpfig
elseif ($drop = "Gate")
	setVar $drop_sec $gate
end
:get_plock
send "p" $drop_sec "*"
setTextLineTrigger gate_hit :gate_hit "Report Sector "&$gate
setTextLineTrigger no_fig :no_fig_down "Your own fighters must be in"
setTextLineTrigger decay :decay "Locator beam lost."
setDelayTrigger not_comming :not_comming 5000
pause

:gate_hit
killAllTriggers
goto :decay

:no_fig_down
killAllTriggers
setVar $figgies[$drop_sec] 0
goto :hehe5

:not_comming
killAllTriggers 
send "**"
goto :hehe5

:decay
send "y"

if ($responder_attack = "ON")
	goto :lift_attack
else
	goto :restock5
end

:lift_attack
setVar $cycle 0
setVar $attack_wave 0
send "qq"
gosub :attack
send "l" $responder_planet "*mnt*c"
if ($cycle > 5)
	goto :restock5
end
goto :lift_attack


:restock5
send "'OZ-Dead-End PWarp-Drop Powering Down!*"
halt



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
add $cycle 1
send "*"
goto :end_attack

:hit
send $responder_figs "*"
add $attack_wave 1
waitFor "You lost"
goto :attack

:end_attack
return




# -=-=-=-=-=-=-=-=- end it -=--=-=-=-=-=-=-=-
:end
send "'OZ-Dead-End PWarp-Drop Powering Down...*"
echo ANSI_12 "*OZ-Dead-End PWarp-Drop Powering Down...*"
halt

# -=-=-=-=-= subroutine to find nnf dead end -=-=-=-=-=-
:nnf_find
setVar $nnfcheck $activated_sector
setVar $backs 0
setVar $hops 0
setVar $adjacents 0
setVar $targetcount 0
:calc_nnf
isNumber $result $nnfcheck
if ($result <> 1)
	goto :end
end
if ($nnfcheck > SECTORS) or ($nnfcheck < 1)
	goto :end
end
if ($de[$nnfcheck] = 1)
	setVar $jumpfig $nnfcheck
	goto :found_nnf
end
:nnf_hunt
setVar $target $nnfcheck
gosub :check_nnf

:nnf_hops
setVar $preadj $adjacents
if ($adjacents = 0)
	send "'OZ Hunter: No figs found*"
	goto :end
end
setVar $adjacents 0
add $hops 1
setVar $prehop $hops
subtract $prehop 1
setVar $hopscount 0

:nnf_count
if ($hopscount < $preadj)
	add $hopscount 1
	setVar $target $adjacent[$hopscount]_[$prehop]
	gosub :check_nnf
	goto :nnf_count
end
goto :nnf_hops
	

:check_nnf
setVar $warpcount 0
getsector $target $targetinfo
add $targetcount 1
setVar $donesecs[$targetcount] $target
:warp_nnf
if ($warpcount < 7)
	add $warpcount 1
	setVar $thesec $targetinfo.warp[$warpcount]
	setVar $doneloop 0
	if ($thesec = 0)
		return
	end
:done_nnf
	if ($doneloop < $targetcount)
		add $doneloop 1
		if ($donesecs[$doneloop] = $thesec)
			goto :warp_nnf
		end
		goto :done_nnf
	end
	getdistance $backdoorcheck $thesec $target
	if ($backdoorcheck > 1)
		goto :warp_nnf
	end
	if ($de[$thesec] = 1)
		add $hops 1
		setVar $jumpfig $thesec
		goto :found_nnf
	else
		add $adjacents 1
		setVar $adjacent[$adjacents]_[$hops] $targetinfo.warp[$warpcount]
	end
	goto :warp_nnf
end

:found_nnf
# getDistance $hoppy $jumpfig $nnfcheck
# send "'OZ-BOT: Nearest non-figged sector to " $nnfcheck " is " $jumpfig ", " $hoppy " hop(s) away.*"
goto :got_nnf_de