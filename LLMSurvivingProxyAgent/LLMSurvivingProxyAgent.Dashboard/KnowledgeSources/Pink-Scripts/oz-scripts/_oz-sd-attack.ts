# sd attack for twx 2.0

setVar $oz_sd_attack_version "OZ-SD Attack 2.0"

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Command")
        clientMessage "This script must be run from the Command Prompt"
        halt
end

:getinfo
send "V"
setTextLineTrigger sd :getSDsec1 "The StarDock is located in sector"
pause
:getSDsec1
getWord CURRENTLINE $SD 7
stripText $SD "."
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
killTrigger credits
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
if ($homesec <> $sd)
echo ANSI_12 "**Must Start at StarDock!!**"
halt
end

loadVar $OzSDAttackSaved

if ($OzSDAttackSaved)
	setVar $responder_waves $fig_waves
	setVar $responder_figs $max
	setVar $sit_sd "OFF"
	setVar $menu_sit 0
	setVar $return_sd "OFF"
	setVar $menu_sd 0
	setVar $menu_attack 0
	setVar $attack_type "Adj-Attack"
	setVar $responder_traders 0

else
	setVar $sit_sd "OFF"
	setVar $menu_sit 0
	setVar $return_sd "OFF"
	setVar $menu_sd 0
	setVar $menu_attack 0
	setVar $attack_type "Adj-Attack"
	setVar $responder_waves $fig_waves
	setVar $responder_figs $max
	setVar $responder_traders 0

	saveVar $sit_sd
	saveVar $return_sd
	saveVar $attack_type
	
	setVar $ozsdattackSaved 1
	saveVar $ozsdattackSaved
end

addMenu "" "OZ-SD Attack" "OZ-SD Attack" "." "" "Main" FALSE
addMenu "OZ-SD Attack" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-SD Attack" "Stay on SD" "Stay on SD" "S" :Menu_Sit "" FALSE
addMenu "OZ-SD Attack" "Re-Land After" "Re-Land After" "R" :Menu_Return "" FALSE
addMenu "OZ-SD Attack" "Attack Type" "Attack Type" "A" :Menu_Attack "" FALSE
addMenu "OZ-SD Attack" "Traders-Avoid" "Traders-Avoid" "T" :Menu_Traders "" FALSE
addMenu "OZ-SD Attack" "Figs/Wave" "Figs/Wave" "F" :Menu_Figs "" FALSE
addMenu "OZ-SD Attack" "Waves" "Waves" "W" :Menu_Waves "" FALSE

setMenuHelp "Run" "This Option Activates the Responder."
setMenuHelp "Traders-Avoid" "This Option Sets the Traders to Avoid while Responder is Active."
setMenuHelp "Figs/Wave" "This Option Sets the Number of Figs to use per Attack Wave."
setMenuHelp "Waves" "This Option Sets the Waves of Figs to use."
setMenuHelp "Stay on SD" "This Option Sets where you wait for trigger."
setMenuHelp "Re-Land After" "This Option Sets where you return after trigger."
setMenuHelp "Attack Type" "This Option Sets type of attack."

:start_menu
gosub :sub_setMenu
gosub :ozHeader
openMenu "OZ-SD Attack"

:ozHeader
ECHO ANSI_2 "*----------------------------------------*"
ECHO ANSI_10 "           " $oz_sd_attack_version "       "
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
		openMenu "OZ-SD Attack"

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
openMenu "OZ-SD Attack"

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
openMenu "OZ-SD Attack"

:Menu_Sit
add $menu_sit 1
if ($menu_sit = 1)
	setVar $sit_sd "ON"
elseif ($sit_sd = "ON")
	setVar $menu_sit 0
	setVar $sit_sd "OFF"
else
	setVar $menu_sit 0
	setVar $sit_sd "OFF"
end
saveVar $sit_sd
gosub :sub_SetMenu
gosub :OZheader
openMenu "OZ-SD Attack"

:Menu_Return
add $menu_sd 1
if ($menu_sd = 1)
	setVar $return_sd "ON"
elseif ($return_sd = "ON")
	setVar $menu_sd 0
	setVar $return_sd "OFF"
else
	setVar $menu_sd 0
	setVar $return_sd "OFF"
end
saveVar $return_sd
gosub :sub_SetMenu
gosub :OZheader
openMenu "OZ-SD Attack"

:Menu_Attack
add $menu_attack 1
if ($menu_attack = 1)
	setVar $attack_type "Adj-Phot"
elseif ($attack_type = "Adj-Phot")
	setVar $menu_attack 0
	setVar $attack_type "Adj-Attack"
else
	setVar $menu_attack 0
	setVar $attack_type "Adj-Attack"
end
saveVar $attack_type
gosub :sub_SetMenu
gosub :OZheader
openMenu "OZ-SD Attack"

:sub_setMenu
	setMenuValue "Traders-Avoid" $responder_traders
	setMenuValue "Figs/Wave" $responder_figs
	setMenuValue "Waves" $responder_waves
	setMenuValue "Stay on SD" $sit_sd
	setMenuValue "Re-Land After" $return_sd
	setMenuValue "Attack Type" $attack_type
return

:menu_run
send "'Activating OZ-SD Attack 2.0*"
if ($menu_attack = 1) and ($phot = 0)
	send "'No Photons Aboard! OZ-SD Attack Powering Down...*"
	halt
end

# lets get sector info and check for oneways
:getsec
setVar $oneway 0
getSector $sd $sdinfo
:distance
add $oneway 1
if ($sdinfo.warp[$oneway] = 0)
	goto :done_distance
end
getDistance $dist $sdinfo.warp[$oneway] $sd
	if ($dist = 1)
		setVar $adj[$oneway] 1
	else
		setVar $adj[$oneway] 0
	end
goto :distance

:done_distance
if ($menu_sit = 1)
	send "psg"
end

:trigger
setVar $count 0
setTextLineTrigger limp :sdlimp "Limpet mine in"
setTextLineTrigger fighit :sdfig "Deployed Fighters Report Sector"
pause

:sdlimp
killAllTriggers
gosub :limphit
if ($person = $traders[$m]) or ($fercheck = "Ferrengi") or ($fercheck = "Alien")
	goto :trigger
else
	goto :sd_id_check
end

:sdfig
killAllTriggers
gosub :fighit
if ($person = $traders[$m]) or ($fercheck = "Ferrengi") or ($fercheck = "Alien")
	goto :trigger
else
	goto :sd_id_check
end
:sd_id_check
add $m 1
if ($m > $responder_traders)
	goto :kill
end

:kill
:checkit
add $count 1
if ($sector = $sdinfo.warp[$count]) 
	goto :attack
elseif ($count > $sdinfo.warps)
	goto :trigger
end
goto :checkit

:attack
if ($adj[$count] = 0) and ($menu_attack = 0)
	goto :ckeckit
elseif ($menu_attack = 1)
	goto :photon
end
if ($menu_sit = 1)
	send "qq"
end
send "m" $sector "*"
send "za999*znfz1*zcdzn"
setVar $attack_wave 0
gosub :sub_attack
send "m" $sd "*"

if ($menu_sd = 0)
	send "'OZ-SD Attack Powering Down...*"
	halt
end

:restock
send "psh"
setTextLineTrigger limp :limp "A port official runs up to you"
setTextLineTrigger nolimp :nolimp "The Emporium Daily Specials"
pause

:limp
killtrigger nolimp
send "Y"
setTextLineTrigger broke :broke "(you haven't the funds!)"
setTextLineTrigger gone :gone "they find and remove the Limpet"
pause

:broke
killTrigger gone
send "'OZ-SD Attack Powering Down...*"
Echo ANSI_12 "**No Funds!!**"
halt

:gone
killTrigger broke
send "s"

:nolimp
send "'OZ-SD Attack Powering Down...*"
killTrigger limp
halt


# -=-=-=-=-=-=- Photon subroutine -=-=-=-=-=-=-=-=-
:photon
if ($menu_sit = 1)
	send "qq"
end
send "cpy" $sector "*qsh"
if ($menu_sd = 1)
	send "psh"
end
send "'OZ-SD Attack - Fired P-Missle into sector " $sector ".*"
halt

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

# -=-=-=-=-=-=-- attack subroutine -=-==-=-=-=-=-=-=-=-=-=
:sub_attack
waitFor "(?="
killAllTriggers
if ($attack_wave >= $responder_waves) 
	goto :
end
send "ad"
waitFor "<Attack>"
:popem
killAllTriggers
setTextLineTrigger notarget :notarget "<Re-Display>"
setTextLineTrigger nothere :notarget "no longer in this sector!"
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
goto :sub_attack

:notarget
killAllTriggers
send "*"
setVar $attack_return 1
return

:hit
send $responder_figs "*"
add $attack_wave 1
waitFor "You lost"
goto :sub_attack



























