# OZ - Responder 2.0 for TWX2
# Multi-Option style responder for fig hit response

setVar $oz_responder_version "OZ-Responder 2.0"

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Command")
        clientMessage "This script must be run from the Command Prompt"
        halt
end

:getinfo
send "i"
waitFor "Current Sector :"
getWord CURRENTLINE $homesec 4
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




loadVar $OzResponderSaved

if ($ozResponderSaved)
	loadVar $type
	loadVar $mode
	loadVar $responder_type
	loadVar $responder_planet
	loadVar $responder_attack
	loadVar $responder_traders
	setVar $responder_waves $fig_waves
	setVar $responder_figs $max
else
	setVar $type "TWarp-Drop"
	setVar $mode 1
	setVar $responder_type 1
	setVar $responder_planet 0
	setVar $responder_attack "OFF"
	setVar $responder_waves $fig_waves
	setVar $responder_figs $max
	setVar $responder_traders 0

	saveVar $type
	saveVar $mode
	saveVar $responder_type
	saveVar $responder_planet
	saveVar $responder_attack
	saveVar $responder_traders

	setVar $ozResponderSaved 1
	saveVar $ozResponderSaved
end

addMenu "" "OZ-Responder" "OZ-Responder Settings" "." "" "Main" FALSE
addMenu "OZ-Responder" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-Responder" "Traders-Avoid" "Traders-Avoid" "A" :Menu_Traders "" FALSE
addMenu "OZ-Responder" "Figs/Wave" "Figs/Wave" "F" :Menu_Figs "" FALSE
addMenu "OZ-Responder" "Waves" "Waves" "W" :Menu_Waves "" FALSE
addMenu "OZ-Responder" "Move-In/Attack" "Move-In/Attack" "M" :Menu_Attack "" FALSE
addMenu "OZ-Responder" "Planet" "Planet" "P" :Menu_Planet "" FALSE
addMenu "OZ-Responder" "Type" "Type" "T" :Menu_Type "" FALSE

setMenuHelp "Run" "This Option Activates the Responder."
setMenuHelp "Traders-Avoid" "This Option Sets the Traders to Avoid while Responder is Active."
setMenuHelp "Figs/Wave" "This Option Sets the Number of Figs to use per Attack Wave."
setMenuHelp "Waves" "This Option Sets the Waves of Figs to use."
setMenuHelp "Move-In/Attack" "This Option Sets the Move in and Attack for Photon Types."
setMenuHelp "Planet" "This Option Sets the Planet to use."
setMenuHelp "Type" "This Option Sets Responder Type."

:start_menu
gosub :sub_setMenu
gosub :ozHeader
openMenu "OZ-Responder"

:ozHeader
ECHO ANSI_2 "*----------------------------------------*"
ECHO ANSI_10 "           " $oz_responder_version "       "
ECHO ANSI_2 "*----------------------------------------*"
return

:Menu_Traders
getInput $responder_traders "Enter the number of Traders to avoid"
isNumber $number $responder_traders
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Traders
	end
setVar $t 0 
:subtrader
add $t 1
	if ($t > $responder_traders)
		saveVar $responder_traders
		gosub :sub_setMenu
		gosub :ozheader
		openMenu "OZ-Responder"

	end
getInput $traders[$t] "Enter Trader Name to Avoid"
echo ANSI_12 "*" $traders[$t] "*"
uppercase $traders[$t]
goto :subtrader

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
openMenu "OZ-Responder"


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
openMenu "OZ-Responder"


:Menu_Attack
getInput $attack "Move in and Attack After Photon or PDrop? (Y/N) "
lowercase $attack
	if ($attack = "y")
		setVar $responder_attack "ON"
		saveVar $responder_attack
		gosub :sub_setMenu
		gosub :ozheader
		openMenu "OZ-Responder"

	elseif ($attack = "n")
		setVar $responder_attack "OFF"
 		saveVar $responder_attack
		gosub :sub_setMenu
		gosub :ozheader
		openMenu "OZ-Responder"

	else
		echo ANSI_12 "*Please enter y or n*"
		goto :Menu_attack
	end

		
:Menu_Planet
getInput $responder_planet "Enter the Planet Number to use with Responder"
isNumber $number $responder_planet
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Planet
	end
saveVar $responder_planet
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-Responder"


:Menu_Type
add $responder_type 1
	if ($responder_type = 2)
		setVar $mode 2
		setVar $type "BWarp-Drop"
	elseif ($responder_type = 3)
		setVar $mode 3
		setVar $type "PWarp-Drop"
	elseif ($responder_type = 4)
		setVar $mode 4
		setVar $type "TWarp-Phot"
	elseif ($responder_type = 5)
		setVar $mode 5
		setVar $type "BWarp-Phot"
	elseif ($responder_type = 6)
		setVar $mode 6
		setVar $type "PWarp-Phot"
	else
		setVar $responder_type 1
		setVar $mode 1
		setVar $type "Twarp-Drop"
	end
saveVar $mode 
saveVar $type
saveVar $responder_type
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-Responder"


:sub_setMenu
	setMenuValue "Traders-Avoid" $responder_traders
	setMenuValue "Figs/Wave" $responder_figs
	setMenuValue "Waves" $responder_waves
	setMenuValue "Move-In/Attack" $responder_attack
	setMenuValue "Planet" $responder_planet
	setMenuValue "Type" $type
return

:menu_run
	if ($mode = 1)
		goto :twarp_drop
	elseif ($mode = 2)
		goto :bwarp_drop
	elseif ($mode = 3)
		goto :pwarp_drop
	elseif ($mode = 4)
		goto :twarp_phot
	elseif ($mode = 5)
		goto :bwarp_phot
	elseif ($mode = 6)
		goto :pwarp_phot
	end

# -=-=-=-=-=-=-=-=-=- Twarp Drop mode -=-=-=-=-=-=-=-=-=-=-
:twarp_drop
gosub :ss
echo ANSI_12 "* OZ Responder - TWarp-Drop : ACTIVE *"
killAllTriggers
setTextLineTrigger limp :twdlimp "Limpet mine in"
setTextLineTrigger fighit :twdfig "Deployed Fighters Report Sector"
pause

:twdlimp
killAllTriggers
gosub :limphit

:twdfig
killAllTriggers
setVar $m 1
gosub :fighit
if ($person = $traders[$m]) or ($fercheck = "Ferrengi") or ($fercheck = "Alien")
	goto :twarp_drop
else
	goto :twd_id_check
end
:twd_id_check
add $m 1
if ($m > $responder_traders)
	goto :twd_go
end

:twd_go
gosub :twarp
setVar $attack_wave 0
gosub :attack
:twd_attack_return
if ($attack_return = 1)
	setVar $sector $homesec
	gosub :twarp
	goto :end
end

# -=-=-=-=-=-=- BWarp Drop -=-=-=-=-=-=-=-
:bwarp_drop
gosub :ss
send "l" $responder_planet "*tnt1*mnt*c"
if ($responder_planet = 0)
	echo ANSI_12 "*Planet Number Error!*"
	goto :end
end
:bwd_return
waitFor "Citadel treasury"
echo ANSI_12 "* OZ Responder - BWarp-Drop : ACTIVE *"
setTextLineTrigger limp :bwdlimp "Limpet mine in"
setTextLineTrigger fighit :bwdfig "Deployed Fighters Report Sector"
pause

:bwdlimp
killAllTriggers
gosub :limphit

:bwdfig
killAllTriggers
setVar $m 1
gosub :fighit
if ($person = $traders[$m]) or ($fercheck = "Ferrengi") or ($fercheck = "Alien")
	goto :bwd_return
else
	goto :bwd_id_check
end
:bwd_id_check
add $m 1
if ($m > $responder_traders)
	goto :bwd_go
end

:bwd_go
gosub :bwarp
setVar $attack_wave 0
gosub :attack
:bwd_attack_return
if ($attack_return = 1)
	setVar $sector $homesec
	gosub :twarp
	goto :end
end
# -=-=-=-=-=-=-=-=- PWarp-Drop -=-=-=-=-=-=-=-=-=-=-=
:pwarp_drop
gosub :ss
send "l" $responder_planet "*tnt1*mnt*c"
if ($responder_planet = 0)
	echo ANSI_12 "*Planet Number Error!*"
	goto :end
end
:pwd_return
waitFor "Citadel treasury"
echo ANSI_12 "* OZ Responder - PWarp-Drop : ACTIVE *"
setTextLineTrigger limp :pwdlimp "Limpet mine in"
setTextLineTrigger fighit :pwdfig "Deployed Fighters Report Sector"
pause

:pwdlimp
killAllTriggers
gosub :limphit

:pwdfig
killAllTriggers
setVar $m 1
gosub :fighit
if ($person = $traders[$m]) or ($fercheck = "Ferrengi") or ($fercheck = "Alien")
	goto :pwd_return
else
	goto :pwd_id_check
end
:pwd_id_check
add $m 1
if ($m > $responder_traders)
	goto :pwd_go
end

:pwd_go
gosub :pwarp
	if ($responder_attack = "ON")
		goto :pwd_lift
	else
		goto :end
	end
:pwd_lift
setVar $attack_wave 0
send "qq"
gosub :attack
if ($attack_return = 1)
	setVar $sector $homesec
	gosub :pwarp
	goto :end
end
:pwd_attack_return
send "l" $responder_planet "*mnt*c"
goto :pwd_lift

# -=-=-=-=-=-=-=-=-= twarp phot -=-=-=-=-=-=-=-=-=-=-=-=-
:twarp_phot
gosub :ss
if ($phot = 0)
	send "'OZ Responder - No Photons Aboard - Deactivating...*"
	echo ANSI_12 "*No Photons!!!*"
	goto :end
end
killAllTriggers
gosub :fig_refresh
:twp_fig_return
echo ANSI_12 "* OZ Responder - TWarp-Phot : ACTIVE *"
setTextLineTrigger limp :twplimp "Limpet mine in"
setTextLineTrigger fighit :twpfig "Deployed Fighters Report Sector"
pause

:twplimp
killAllTriggers
gosub :limphit

:twpfig
killAllTriggers
setVar $m 1
gosub :fighit
if ($person = $traders[$m]) or ($fercheck = "Ferrengi") or ($fercheck = "Alien")
	goto :twp_fig_return
else
	goto :twp_id_check
end
:twp_id_check
add $m 1
if ($m > $responder_traders)
	goto :twp_go
end

:twp_go
gosub :get_adj
gosub :twarp
gosub :photon
if ($responder_attack = "ON")
		goto :twp_movein
	else
		setVar $attack_return 1
		goto :twp_attack_return
	end
:twp_movein
waitFor "Wave Duration has ended"
send "m" $sector "*za999*znfz1*zcdzn"
gosub :surround
setVar $attack_wave 0
gosub :attack
:twp_attack_return
if ($attack_return = 1)
	setVar $adjsec $homesec
	gosub :twarp
	goto :end
end

# -=-=-=-=-=-=-=-=-=-=-=-=- bwarp phot -=-=-=-=-=-=-=-=-=-=-=-=-=-=-
:bwarp_phot
gosub :ss
if ($phot = 0)
	send "'OZ Responder - No Photons Aboard - Deactivating...*"
	echo ANSI_12 "*No Photons!!!*"
	goto :end
end
killAllTriggers
gosub :fig_refresh
:bwp_fig_return
send "l" $responder_planet "*tnt1*mnt*c"
waitFor "Citadel treasury"
echo ANSI_12 "* OZ Responder - BWarp-Phot : ACTIVE *"
setTextLineTrigger limp :bwplimp "Limpet mine in"
setTextLineTrigger fighit :bwpfig "Deployed Fighters Report Sector"
pause

:bwplimp
killAllTriggers
gosub :limphit

:bwpfig
killAllTriggers
setVar $m 1
gosub :fighit
if ($person = $traders[$m]) or ($fercheck = "Ferrengi") or ($fercheck = "Alien")
	goto :bwp_fig_return
else
	goto :bwp_id_check
end
:bwp_id_check
add $m 1
if ($m > $responder_traders)
	goto :bwp_go
end

:bwp_go
gosub :get_adj
gosub :bwarp
gosub :photon
if ($responder_attack = "ON")
		goto :bwp_movein
	else
		setVar $attack_return 1
		goto :bwp_attack_return
	end
:bwp_movein
waitFor "Wave Duration has ended"
send "m" $sector "*za999*znfz1*zcdzn"
gosub :surround
setVar $attack_wave 0
gosub :attack
:bwp_attack_return
if ($attack_return = 1)
	setVar $sector $homesec
	gosub :twarp
	goto :end
end

# -=-=-=-=-=-=-=- pwarp phot -=-=-=-=-=-=-=-=-=--=
:pwarp_phot
gosub :ss
if ($phot = 0)
	send "'OZ Responder - No Photons Aboard - Deactivating...*"
	echo ANSI_12 "*No Photons!!!*"
	goto :end
end
killAllTriggers
gosub :fig_refresh
send "l" $responder_planet "*tnt1*mnt*c"
:pwp_fig_return
waitFor "Citadel treasury"
echo ANSI_12 "* OZ Responder - PWarp-Phot : ACTIVE *"
setTextLineTrigger limp :pwplimp "Limpet mine in"
setTextLineTrigger fighit :pwpfig "Deployed Fighters Report Sector"
pause

:pwplimp
killAllTriggers
gosub :limphit

:pwpfig
killAllTriggers
setVar $m 1
gosub :fighit
if ($person = $traders[$m]) or ($fercheck = "Ferrengi") or ($fercheck = "Alien")
	goto :pwp_fig_return
else
	goto :pwp_id_check
end
:pwp_id_check
add $m 1
if ($m > $responder_traders)
	goto :pwp_go
end

:pwp_go
gosub :get_adj
gosub :pwarp
gosub :photon
if ($responder_attack = "ON")
		goto :pwp_movein
	else
		goto :end
	end
:pwp_movein
waitFor "Wave Duration has ended"
send "qqm" $sector "*za999*znfz1*zcdzn<zn"
send "l" $responder_planet "*mnt*c"
setVar $adjsec $sector
setVar $pwpgo 1
gosub :pwarp
send "qq"
gosub :surround
send "l" $responder_planet "*mnt*c"
:pwp_lift
setVar $attack_wave 0
send "qq"
gosub :attack
if ($attack_return = 1)
	setVar $adjsec $homesec
	gosub :pwarp
	goto :end
end
:pwp_attack_return
send "l" $responder_planet "*mnt*c"
goto :pwp_lift

# -=-=-=-=-=-=-= photon subroutine -=-=-=-=-=-=-=-=-=-=-
:photon
send "cpy" $sector "*q"
return
# -=-=-=-=-=-=- get adjacent sector subroutine -=-=-=-=-=-=-=-
:get_adj
getSector $sector $adjinf
setVar $i 1
:act
if ($adjinf.warp[$i] = 0) and ($mode = 4)
	goto :twp_fig_return
elseif ($adjinf.warp[$i] = 0) and ($mode = 5)
	goto :bwp_fig_return
elseif ($adjinf.warp[$i] = 0) and ($mode = 6)
	goto :pwp_fig_return
end
if ($figgies[$adjinf.warp[$i]] = 1) 
	setVar $adjsec $adjinf.warp[$i]
else 
	add $i 1
	goto :act
end
return	

# -=-=-=-=-=-=- fig refresh subroutine -=-=-=-=-=-=-=-=-=
:fig_refresh
setVar $fig_count 0
send "'OZ Responder: Fig refresh in progress...*"
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
send "'OZ Responder: Fig refresh complete*"
setVar $total_figs $fig_count
multiply $fig_count 100
divide $fig_count SECTORS
send "'OZ Responder: fig count: " $total_figs " sector(s) : " $fig_count "%*"
return

# -=-=-=-=-=-= Pwarp routine -=-=-=-=-=-=-=-=-=-=-=-=-=-
:pwarp
killAllTriggers
if ($mode = 3)
	send "p" $sector "*y"
elseif ($mode = 6)
	send "p" $adjsec "*y"
end
setTextLineTrigger pwarp_lock :pwarp_lock "Locating beam pinpointed"
setTextLineTrigger no_pwarp_lock :no_pwarp_lock "Your own fighters must be"
pause

:pwarp_lock
killAllTriggers
waitFor "Planet is now in sector"
return

:no_pwarp_lock
killAllTriggers
if ($mode = 3)
	goto :pwd_return
elseif ($mode = 6) and ($pwpgo = 0)
	setVar $figgies[$adjsec] 0
	goto :pwp_fig_return
elseif ($mode = 6) and ($pwpgo = 1)
	setVar $figgies[$adjsec] 0
	goto :end
end

# -=-=-=-=-=- Limpet hit subroutine -=-=-=-=-=-=-=-
:limphit
getWord CURRENTLINE $sector 4
return

# -=-=-=-=-=-=-fig hit subroutine -=-=-=-=-=-=-=-=-=
:fighit
KillAllTriggers
getWord CURRENTLINE $sector 5
getText CURRENTLINE $person ": " "'s"
uppercase $person
getWord CURRENTLINE $fercheck 6
stripText $sector ":"
return
# -=-=-=-=-=-=-=- bwarp subroutine -=-=-=-=-=-=-=-=-=-
:bwarp
if ($mode = 2)
	send "b" $sector "*"
elseif ($mode = 5)
	send "b" $adjsec "*"
end
setTextLineTrigger beam_lock :beam_lock "TransWarp Locked"
setTextLineTrigger no_beam_lock :no_beam_lock "No locating beam found"
pause
:beam_lock
killAllTriggers
send "y*"
waitFor "Warps to Sector(s)"
return

:no_beam_lock
killAllTriggers
send "n*"
if ($mode = 2)
	goto :bwd_return
elseif ($mode = 5)
	setVar $figgies[$adjsec] 0
	goto :bwp_return
end
# -=-=-=-=-=-=-=-twarp subroutine-=-=-=-=-=-=-=-=-=-
:twarp
if ($mode = 1) or ($mode = 2) or ($mode = 5)
	send "m" $sector "*y"
elseif ($mode = 4)
	send "m" $adjsec "*y"
end
setTextLineTrigger twarp_lock :twarp_lock "TransWarp Locked"
setTextLineTrigger no_twrp_lock :no_twarp_lock "No locating beam found"
setTextLineTrigger twarp_adj :twarp_adj "<Set NavPoint>"
setTextLineTrigger no_ore :no_ore "You do not have enough Fuel Ore"
pause


:no_ore
goto :end


:twarp_adj
send "**"
killAllTriggers
return

:twarp_lock
KillAlltriggers
send "y*"
waitFor "Warps to Sector(s)"
return

:no_twarp_lock
killAllTriggers
send "n*"
if ($mode = 1)
	goto :twarp_drop
elseif ($mode = 4)
	setVar $figgies[$adjsec] 0
	goto :twp_fig_return
end
# -=-=-=-=-=-=-=-=-=attack subroutine-==-=-=-=-=-=-=-=-
:attack
waitFor "(?="
killAllTriggers
if ($attack_wave >= $responder_waves) and ($mode = 1)
	setVar $attack_return 1
	goto :twd_attack_return
elseif ($attack_wave >= $responder_waves) and ($mode = 2)
	setVar $attack_return 1
	goto :bwd_attack_return
elseif ($attack_wave >= $responder_waves) and ($mode = 4)
	setVar $attack_return 1
	goto :twp_attack_return
elseif ($attack_wave >= $responder_waves) and ($mode = 5)
	setVar $attack_return 1
	goto :bwp_attack_return
elseif ($attack_wave >= $responder_waves) and ($mode = 6) 
	goto :pwp_attack_return
elseif ($attack_wave >= $responder_waves) and ($mode = 3)
	goto :pwd_attack_return
end
send "ad"
waitFor "<Attack>"
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
setVar $attack_return 1
return

:hit
send $responder_figs "*"
add $attack_wave 1
waitFor "You lost"
goto :attack

# -=-=-=-=-=-=-=-=- surround subroutine -=-=-=-=-=-=-=-=-=-
:surround
setVar $highdens 499
send "d"
waitFor "Sector  :"
getWord CURRENTLINE $cur_sec 3
getSector $cur_sec $cur_secinfo
waitFor "Command [TL"
setVar $z 0
send "Sd"
waitFor " Relative Density Scan"
:Start_scan
killAllTriggers
setTextLineTrigger getSec :getSec "Sector"
setTextTrigger allDone :Done_scan "Command [TL="
pause
:getSec
add $z 1
getText CURRENTLINE $Density[$z] "==>" "Warps :"
stripText $density[$z] ","
stripText $density[$z] " "
goto :Start_scan
:Done_scan
send "^"
setvar $y 0
killAllTriggers
:plots
add $y 1
if ($cur_secinfo.warp[$y] = 0)
	goto :done_plots
else
	send "f" $cur_secinfo.warp[$y] "*" $cur_sec "*"
end
setTextLineTrigger pathck :pathck $cur_secinfo.warp[$y]&" > "
pause

:pathck
getWord CURRENTLINE $nextck 3
if ($nextck = $cur_sec) and ($Density[$y] <= $highdens)
	setVar $plot[$y] 1
else
	setVar $plot[$y] 0
end
goto :plots

:done_plots
send "q"
setVar $x 0
:hit_surround
add $x 1
if ($x > $y)
	goto :end_surround
elseif ($plot[$x] = 1)
	send "m" $cur_secinfo.warp[$x] "*za999*znfz1*zcdzn<zn"
	goto :hit_surround
elseif ($plot[$x] = 0
	goto :hit_surround
end

:end_surround
return


# -=-=-=-=-=-=-=-=-=- subspace start -=-=-=-=-=-=-=-=-=-=-
:ss
send "'OZ Responder: " $type " :ACTIVE*"
return
# -=-=-=-=-=-=-=-=-=- end script -=-=-=-=-=-=-=-=-=-=-=-=-	
:end
send "'OZ Responder: " $type " Powering Down...*"
echo ANSI_12 "*OZ Responder Powering Down...*"
halt