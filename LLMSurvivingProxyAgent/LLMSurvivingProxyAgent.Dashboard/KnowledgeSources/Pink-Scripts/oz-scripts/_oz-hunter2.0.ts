# heh yeh baby
# hunter in the house
# charge clears nnf dead ends

# lets check for dead ends here first
# need to have them exported to file gamename.dend
# lets get some inputs to start
# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Command")
        clientMessage "This script must be run from the Command Prompt"
        halt
end
setVar $oz_hunter_version "OZ-Hunter 2.0"

loadVar $OzHunterSaved
if ($ozHunterSaved)
	loadVar $hunter_figs
	loadVar $hunter_turns
else
	setVar $hunter_figs 100
	setVar $hunter_turns 100
	saveVar $hunter_figs
	saveVar $hunter_turns

	setVar $ozHunterSaved 1
	saveVar $ozHunterSaved
end

addMenu "" "OZ-Hunter" "OZ-Hunter Settings" "." "" "Main" FALSE
addMenu "OZ-Hunter" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-Hunter" "Min Figs" "Min Figs" "F" :Menu_Figs "" FALSE
addMenu "OZ-Hunter" "Min Turns" "Min Turns" "T" :Menu_Turns "" FALSE


setMenuHelp "Run" "This Option Activates the Responder."
setMenuHelp "Min Figs" "This Option Sets the Minimum Figs for Hunter to Stop."
setMenuHelp "Min Turns" "This Option Sets the Minimum Turns for Hunter to Stop."

:start_menu
gosub :sub_setMenu
gosub :ozHeader
openMenu "OZ-Hunter"



:ozHeader
ECHO ANSI_2 "*----------------------------------------*"
ECHO ANSI_10 "             " $oz_hunter_version "       "
ECHO ANSI_2 "*----------------------------------------*"
return

:Menu_Figs
getInput $hunter_figs "Enter the minimum fig level to stop at"
echo ANSI_12 "*" $hunter_figs "*"
isNumber $number $hunter_figs
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Figs
	end
saveVar $hunter_figs
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-Hunter"

:Menu_Turns
getInput $hunter_turns "Enter the minimum turn level to stop at"
echo ANSI_12 "*" $hunter_turns "*"
isNumber $number $hunter_turns
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Turns
	end
saveVar $hunter_turns
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-Hunter"

:sub_setMenu
	setMenuValue "Min Figs" $hunter_figs
	setMenuValue "Min Turns" $Hunter_turns
return



:menu_run
# globals
setVar $fig_num 1
setVar $fig_type d
# lets get figgies first
:figrefresh
setVar $secsfigd 0
setArray $figList SECTORS
send "'OZ Hunter: Fig refresh in progress...*"
send "G"
waitFor "==========================================================="
:get_figs
getWord CURRENTLINE $fs 1
getWord CURRENTLINE $st 2
isNumber $sn $fs
if ($st = "Total") or ($st = "fighters")
	setVar $perc (($secsfigd * 100)/SECTORS)
	send "'OZ Hunter: Fig refresh complete*"
	send "'OZ Hunter: Fig count: " $secsfigd " sector(s) : " $perc "%*"
	goto :check_dend
end
if ($sn = 1)
	add $secsfigd 1
	setVar $figList[$fs] 1
	write $figfile $fs
end
setTextLineTrigger grab :get_figs
pause


:check_dend
setVar $a 0
setArray $de SECTORS
setVar $dead_end_info GAMENAME & ".dend"
fileExists $exist $dead_end_info
	if ($exist = 1)
		gosub :get_de
	else
		send "'OZ Hunter: No Dead-End file list*"
		halt
	end
:info_nab
KillAllTriggers
send "d"
waitfor "Sector  :"
getWord CURRENTLINE $cursec 3
getSector $cursec $cursecinfo
send "V"
setTextLineTrigger sd :getSDsec1 "The StarDock is located in sector"
pause

:getSDsec1
getWord CURRENTLINE $SD 7
stripText $SD "."
# computer option format check
:computer
killAllTriggers
send "cn"
setTextlineTrigger anima :anima "Animation display"
setTextLineTrigger stops :stops "Abort display on keys"
setTextLineTrigger screen :screen "Screen Pauses"
pause
:anima
getWord CURRENTLINE $anima 5
pause
:stops
getWord CURRENTLINE $stops 7
pause
:screen
getWord CURRENTLINE $screen 5
:setoptions
if ($anima = "On")
	send "2"
	goto :computer
elseif ($stops = "ALL")
	send "9"
	goto :computer
elseif ($screen = "Yes")
	send "b"
	goto :computer
else
	send "qq"
end
goto :ship_info

# lets get some ship info
:ship_info
send "I"
killAllTriggers
setTextLineTrigger tpw :tpw "Turns to Warp  :"
setTextLineTrigger turns :turns "Turns left     :"
setTextLineTrigger figgies :figs "Fighters       :"
setTextLineTrigger creds :creds "Credits        :"
pause
:tpw
getWord CURRENTLINE $tpw 5
setVar $turns_left $tpw
pause
:turns
getWord CURRENTLINE $turns 4
if ($turns = "Unlimited")
	setVar $tpw 0
	setVar $turns 500
end
pause
:figs
getWord CURRENTLINE $figs 3
stripText $figs ","
pause
:creds
getWord CURRENTLINE $creds 3
stripText $creds ","
# turn of twarp
send "cunq"

:start_it
goto :nnf_find
:getnav
setVar $c 1
getCourse $lane $cursec $jumpfig
	if ($lane = "-1")
		goto :ztm_error
	end
:bang_it
add $c 1
send $lane[$c] "*zea9955*zn"
setVar $cursec $lane[$c]
subtract $turns $tpw
	if ($lane[$c] > 10) and ($lane[$c] <> $sd)
		send "f  z" $fig_num "*zc" $fig_type "zn"
		subtract $figs $fig_num
	end
	if ($turns < $hunter_turns)
		goto :low_turns
	end
	if ($figs < $hunter_figs)
		goto :low_figs
	end
	if ($c > $lane)
		setVar $de[$cursec] 0
		goto :start_it
	else
		goto :bang_it
	end
:uhOh
echo ANSI_12 "**uh-Oh**"
halt




:low_turns
send "'OZ Hunter: Min Turn Level Reached*"
send "cuyq"
halt

:low_figs
send "'OZ Hunter: Min Fig Level Reached*"
send "cuyq"
halt





# subroutine to add non figged dead ends to the de array
:get_de
add $a 1
read $dead_end_info $desec $a
	if ($desec <> "EOF")
		if ($figList[$desec] = 0)
			setVar $de[$desec] 1
			goto :get_de
		else
			goto :get_de
		end
	end
return

# subroutine to find nnf dead end
:nnf_find
setVar $nnfcheck $cursec
setVar $backs 0
setVar $hops 0
setVar $adjacents 0
setVar $targetcount 0
:calc_nnf
isNumber $result $nnfcheck
if ($result <> 1)
	goto :stop
end
if ($nnfcheck > SECTORS) or ($nnfcheck < 1)
	goto :stop
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
	goto :stop
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
goto :getnav

# place for nnf errors to go to 
:stop
send "'NNF error*"
halt

# place for ztm errrors
:ztm_error
send "'ZTM error*"
halt
