# OZ - nearest non figged sector gridder for twx 2
# duh

# check if we can run it from here
cutText CURRENTLINE $location 1 12

if ($location <> "Command [TL=")
        clientMessage "This script must be run from the command prompt"
        halt
end

# Script Initialization and Info gathering.
setVar $hog_grid_info GAMENAME & ".grd"

# get previous settings for this game
fileExists $exist $hog_grid_info
if ($exist = 1)
	read $hog_grid_info $return 1
	read $hog_grid_info $restock 2
	read $hog_grid_info $rylos 3
	read $hog_grid_info $alpha 4
	read $hog_grid_info $fig_min 5
	read $hog_grid_info $turn_min 6
	read $hog_grid_info $highdens 7
	read $hog_grid_info $m 8
	else
	setVar $menu1 1
	setVar $return "Home"
	setVar $menu2 1
	setVar $restock "Terra"
	setVar $rylos "Unknown"
	setVar $alpha "Unknown"
	setVar $fig_min 20
	setVar $turn_min 30
	setVar $highdens 220
	setVar $m 10
end

# allow for version upgrading-downward compatability
if ($rylos  = "EOF")
	setVar $rylos "Unknown"
end
if ($alpha  = "EOF")
	setVar $alpha "Unknown"
end
if ($fig_min  = "EOF")
	setVar $fig_min 20
end
if ($turn_min  = "EOF")
	setVar $turn_min 30
end
if ($highdens  = "EOF")
	setVar $highdens 220
end
if ($m  = "EOF")
	setVar $m 11
end



#lets set some global variables here
setVar $fig_num 1
setVar $fig_type d
goto :sectorinfo

# lets get sector info
:sectorinfo
KillAllTriggers
send "d"
waitfor "Sector  :"
getWord CURRENTLINE $homesec 3
getSector $homesec $homesecinfo
send "V"
setTextLineTrigger sd :getSDsec1 "The StarDock is located in sector"
pause
:getSDsec1
getWord CURRENTLINE $SD 7
stripText $SD "."

# lets clear voids
# send "cv0*yyq"
goto :computer

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
pause
:figs
getWord CURRENTLINE $figs 3
stripText $figs ","
pause
:creds
getWord CURRENTLINE $creds 3
stripText $creds ","
goto :scan_check

# scanner check
:scan_check
killAllTriggers
send "I"
setTextLineTrigger scantype :scan_type "LongRange Scan :"
setTextLineTrigger noscan :no_scanner "Credits        :"
pause
:no_scanner
killtrigger scantype
echo ANSI_12 "* No holo scanner detected, shutting down...*"
halt
:scan_type
killtrigger noscan
getWord CURRENTLINE $scan_type 4
if ($scan_type = "Density")
	echo ANSI_12 "* This script requires the use of a Holographic scanner, shutting down... *"
	halt
else
	waitfor "Command [TL"
	goto :start_da_menu
end
goto :main_menu


:start_da_menu
# set some pre-variables for main menu
setVar $oz_nnfgrid_version "OZ-NNF Gridder 2.0"

loadVar $OzNNFGridSaved

if ($ozNNFGridSaved)
	loadVar $menu1
	loadVar $return
	loadVar $menu2
	loadVar $restock
	loadVar $rylos
	loadVar $alpha
	loadVar $fig_min
	loadVar $turn_min
	loadVar $highdens
	loadVar $m
	
else
	setVar $menu1 1
	setVar $return "Home"
	setVar $menu2 1
	setVar $restock "Terra"
	setVar $rylos "Unknown"
	setVar $alpha "Unknown"
	setVar $fig_min 20
	setVar $turn_min 30
	setVar $highdens 220
	setVar $m 10

	saveVar $menu1	
	saveVar $return
	saveVar $menu2
	saveVar $restock
	saveVar $rylos
	saveVar $alpha
	saveVar $fig_min
	saveVar $turn_min
	saveVar $highdens
	saveVar $m

	setVar $ozNNFGridSaved 1
	saveVar $ozNNFGridSaved
end

addMenu "" "OZ-NNF Gridder" "OZ-NNF Gridder" "." "" "Main" FALSE
addMenu "OZ-NNF Gridder" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-NNF Gridder" "Return Sector" "Return Sector" "1" :Menu_Return "" FALSE
addMenu "OZ-NNF Gridder" "Restock Where" "Restock Where" "2" :Menu_Restock "" FALSE
addMenu "OZ-NNF Gridder" "Rylos Location" "Rylos Location" "3" :Menu_Rylos "" FALSE
addMenu "OZ-NNF Gridder" "Alpha Location" "Alpha Location" "4" :Menu_Alpha "" FALSE
addMenu "OZ-NNF Gridder" "Fig Min Level" "Fig Min Level" "5" :Menu_Fig "" FALSE
addMenu "OZ-NNF Gridder" "Turn Min Level" "Turn Min Level" "6" :Menu_Turn "" FALSE
addMenu "OZ-NNF Gridder" "Max Density Level" "Max Density Level" "7" :Menu_Density "" FALSE

setMenuHelp "Run" "This Option Activates the NNF Gridder."
setMenuHelp "Return Sector" "This Option Sets the Sector to Return to when done."
setMenuHelp "Restock Where" "This Option Sets the location to restock figs."
setMenuHelp "Rylos Location" "This Option Sets the location of Rylos."
setMenuHelp "Alpha Location" "This Option Sets the location of Alpha Centauri."
setMenuHelp "Fig Min Level" "This Option Sets the minimum fig level to restock at."
setMenuHelp "Turn Min Level" "This Option Sets the minimum turn level to halt at."
setMenuHelp "Max Density Level" "This Option Sets the max density level to avoid."

:start_menu
gosub :sub_setMenu
gosub :ozHeader
openMenu "OZ-NNF Gridder"

:ozHeader
ECHO ANSI_2 "*----------------------------------------*"
ECHO ANSI_10 "           " $oz_nnfgrid_version "       "
ECHO ANSI_2 "*----------------------------------------*"
return

:Menu_Return
add $menu1 1
	if ($menu1 = 2)
		setVar $return "Star-Dock"
		setVar $rtrn $sd
	elseif ($menu1 = 3)
		setVar $return "Terra"
		setVar $rtrn 1
	else
		setVar $menu1 1
		setVar $return "Home"
		setVar $rtrn $homesec
	end
saveVar $menu1
saveVar $return
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-NNF Gridder"

:Menu_Restock
add $menu2 1
	if ($menu2 = 2) 
		setVar $restock "Star-Dock"
		setVar $rstck $sd
	elseif ($menu2 = 3)
		setVar $restock "Rylos"
		setVar $rstck $rylos
	elseif ($menu2 = 4)
		setVar $restock "Alpha Centauri"
		setVar $rstck $alpha
	else
		setVar $menu2 1
		setVar $restock "Terra"
		setvar $rstck 1
	end
saveVar $menu2
saveVar $restock
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-NNF Gridder"

:Menu_Rylos
getInput $rylos "Where is Rylos?" 
	isNumber $number $rylos
	if ($number = 0)
		echo ansi_12 "*Invalid Sector*"
		goto :menu_rylos
	end
saveVar $rylos
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-NNF Gridder"

:Menu_Alpha
getInput $alpha "Where is Alpha Centauri?" 
	isNumber $number $alpha
	if ($number = 0)
		echo ansi_12 "*Invalid Sector*"
		goto :menu_alpha
	end
saveVar $alpha
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-NNF Gridder"

:Menu_Fig
getInput $fig_min "Restock Figs at what level?" 
	isNumber $number $fig_min
	if ($number = 0)
		echo ansi_12 "*Invalid Number*"
		goto :menu_fig
	end
saveVar $fig_min
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-NNF Gridder"

:menu_turn
getInput $turn_min "End at what turn level?" 
	isNumber $number $turn_min
	if ($number = 0)
		echo ansi_12 "*Invalid Number*"
		goto :menu_turn
	end
saveVar $turn_min
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-NNF Gridder"

:menu_density
getInput $highdens "What is the max density level?" 
	isNumber $number $highdens
	if ($number = 0)
		echo ansi_12 "*Invalid Number*"
		goto :menu_density
	end
saveVar $highdens
gosub :sub_setMenu
gosub :ozheader
openMenu "OZ-NNF Gridder"

:sub_setMenu
	setMenuValue "Return Sector" $return
	setMenuValue "Restock Where" $restock
	setMenuValue "Rylos Location" $rylos
	setMenuValue "Alpha Location" $alpha
	setMenuValue "Fig Min Level" $fig_min
	setMenuValue "Turn Min Level" $turn_min
	setMenuValue "Max Density Level" $highdens
return

:Menu_Run
:start_the_shit
send "cunq"
setVar $u $m
subtract $u 1
goto :refresh_figs
:refresh_fig_return

:subroutine
goto :refresh
:refresh_return
If ($figs_left < $fig_min)
	goto :restock_figs
elseif ($turns_left <= $turn_min)
	goto :head_home
end

:navigate
killAllTriggers
setVar $restocking 0
setVar $goin_home 0
:subnav
goto :nnf_find
:got_nnf
getSector $navsec $navinfo
:plot_to_nav
killAllTriggers
goto :dens_scan
:return_scan
send "cf*" $navsec "*q"
setTextLineTrigger plot_nav :plot_to " > "
setTextLineTrigger noway :noway "No route within 45 warps"
pause
:plot_to
getWord CURRENTLINE $nextsec 3
stripText $nextsec "("
stripText $nextsec ")"
waitFor "<Computer deactivated>"
if ($restocking = 1) and ($rstck = $sd) and ($sd = $nextsec)
	goto :check_next_return
elseif ($restocking = 1) and ($rstck = 1) and (1 = $nextsec)
	goto :check_next_return
elseif ($goin_home = 1) and ($rtrn = $nextsec)
	goto :check_next_return
else
	gosub :next_check
end
:check_next_return
KillAllTriggers
send "m" $nextsec "*"
If ($nextsec <> $SD) and ($nextsec > 10)
	send "za999*znfz" $fig_num "*zc" $fig_type "zn"
	setVar $figsec[$nextsec] 1
end
if ($nextsec = $navsec)
	goto :doneplot
end
goto :replot

:replot
# insert deploy check here
goto :plot_to_nav

:doneplot
if ($goin_home = 1)
	goto :sub_home
elseif ($restocking = 1)
	goto :sub_restock
end
goto :subroutine

:noway
send "nq"
if ($goin_home = 1)
	goto :trapped
elseif ($restocking = 1)
	goto :head_home
end
setVar $figsec[$navsec] 1
goto :subnav


# trapped means surrounded by high density or anomolys
:trapped
send "cuyq"
echo ANSI_12 "**TRAPPED!!**"
halt


# checks the next warp for anom or high density
:next_check
KillAllTriggers
setVar $q 0
:sub_check
add $q 1
If ($q > $i)
	goto :trapped
end
If ($nextsec <> $adjsecs[$q])
	goto :sub_check
elseif ($nextsec = $adjsecs[$q]) and ($density[$q] > $highdens)
	send "shcv" $nextsec "*q"
	setVar $figsec[$nextsec] 1
	goto :plot_to_nav
elseif ($nextsec = $adjsecs[$q]) and ($anomoly[$q] = "Yes")
	send "shcv" $nextSec "*q"
	setVar $figsec[$nextsec] 1
	goto :plot_to_nav
else
	goto :check_next_return
end

# checks to see if a fig is there
:check_fig
if ($figsec[$navsec] = 1)
	goto :subnav
else
	return
end

# refresh figs
:refresh
send "I"
killAllTriggers
setTextLineTrigger my_sec :my_sec "Current Sector :"
setTextLineTrigger turn_refresh :turn_refresh "Turns left     :"
setTextLineTrigger fig_ship :fig_ship "Fighters       :"
pause

:my_sec
getWord CURRENTLINE $my_sec 4

:turn_refresh
getWord CURRENTLINE $turns_left 4

:fig_ship
getWord CURRENTLINE $figs_left 3
stripText $figs_left ","
waitFor "Credits        :"
goto :refresh_return
	

# refresh figgies sectors
:refresh_figs
setVar $g 0
setArray $figsec SECTORS
send "g"
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
	setVar $figsec[$sector] 1
end
if ($stopper = "Total")
	goto :donefig
end
setTextLineTrigger grab :get_figs
pause
:donefig
setVar $figsec[$sd] 1
setVar $figsec[1] 1
setVar $figsec[2] 1
setVar $figsec[3] 1
setVar $figsec[4] 1
setVar $figsec[5] 1
setVar $figsec[6] 1
setVar $figsec[7] 1
setVar $figsec[8] 1
setVar $figsec[9] 1
setVar $figsec[10] 1
goto :refresh_fig_return

# density scans surroundings
:dens_scan
send "d"
waitFor "Sector  :"
getWord CURRENTLINE $cur_sec 3
getSector $cur_sec $cur_secinfo
killAllTriggers
setTextLineTrigger rylos :getrylos " Rylos,"
setTextLineTrigger alpha :getalpha " Alpha Centauri, "
setTextLineTrigger noclasso :noclasso "Warps to Sector(s) :"
pause
:noclasso
waitFor "Command [TL"
setVar $i 0
send "Sd"
waitFor " Relative Density Scan"
:Start
killAllTriggers
setTextLineTrigger getSec :getSec "Sector"
setTextTrigger allDone :allDone "Command [TL="
pause
:getSec
add $i 1
getText CURRENTLINE $AdjSecs[$i] "Sector" "==>"
stripText $adjsecs[$i] "("
stripText $adjSecs[$i] ")"
stripText $adjsecs[$i] " "
getText CURRENTLINE $Density[$i] "==>" "Warps :"
stripText $density[$i] ","
stripText $density[$i] " "
getText CURRENTLINE $warps[$i] "Warps :" "NavHaz :"
stripText $warps[$i] " "
cutText CURRENTLINE $anomoly[$i] 73 5
stripText $anomoly[$i] " "
goto :Start
:allDone
killAllTriggers
goto :return_scan

# rylos and alpha sector storage
:getrylos
setVar $rylos $cur_sec
goto :writeinfo

:getalpha
setVar $alpha $cur_sec
goto :writeinfo

:writeinfo
delete $hog_grid_info
	write $hog_grid_info $return
	write $hog_grid_info $restock
	write $hog_grid_info $rylos
	write $hog_grid_info $alpha
	write $hog_grid_info $fig_min
	write $hog_grid_info $turn_min
	write $hog_grid_info $highdens
	write $hog_grid_info $m	
goto :noclasso

# restock figgies at predetermined port
:restock_figs
if ($restock = "Star-Dock")
	setVar $rstck $sd
elseif ($restock = "Rylos")
	setVar $rstck $rylos
elseif ($restock = "Alpha Centauri")
	setVar $rstck $alpha
elseif ($restock = "Terra")
	setvar $rstck 1
end
killAllTriggers
setVar $u2 $u
setVar $restocking 1
setVar $u $rstck
setVar $navsec $rstck
goto :plot_to_nav
:sub_restock
If ($rstck = $sd)
	send "psspb"
else
	send "ptb"
end
:trig_figs
killAllTriggers
setTextLineTrigger figbuy :figbuy "B  Fighters        :"
setTextLineTrigger figshave :figshave "fighters."
pause
:figbuy
getWord CURRENTLINE $figs_buy 8
pause
:figshave
getWord CURRENTLINE $figs_have 3
:on_to_buying
send $figs_buy "*"
if ($rstck = $sd)
	send "qqq"
else 
	send "q"
end
add $figs_buy $figs_have
if ($figs_buy < $fig_min)
	goto :head_home
else
	setVar $u $u2
	goto :navigate
end

# head home when outa turns or figs
:head_home
killAllTriggers
if ($return = "Star-Dock")
	setVar $rtrn $sd
elseif ($return = "Terra")
	setVar $rtrn 1
elseif ($return = "Home")
	setVar $rtrn $homesec
end
setVar $goin_home 1
setVar $u $rtrn
setVar $navsec $rtrn
goto :plot_to_nav
:sub_home
send "cuyq"
echo ansi_12 "**ALL DONE**"
halt

# find nnf -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
:nnf_find
setVar $nnfcheck $my_sec
setVar $backs 0
setVar $hops 0
setVar $adjacents 0
setVar $targetcount 0
:calc_nnf
isNumber $result $nnfcheck
if ($result <> 1)
	echo ANSI_12 "**NNF Error**"
	halt
end
if ($nnfcheck > SECTORS) or ($nnfcheck < 1)
	echo ANSI_12 "**NNF Error**"
	halt
end
if ($figsec[$nnfcheck] = 0)
	setVar $jumpfig $nnfcheck
	goto :found_nnf
end
:nnf_hunt
setVar $target $nnfcheck
gosub :check_nnf

:nnf_hops
setVar $preadj $adjacents
if ($adjacents = 0)
	echo ANSI_12 "**NNF Error**"
	halt
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
	if ($figsec[$thesec] = 0)
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
getDistance $hoppy $jumpfig $nnfcheck
setVar $navsec $jumpfig
goto :got_nnf
# send "'OZ-BOT: Nearest non-figged sector to " $nnfcheck " is " $jumpfig ", " $hoppy " hop(s) away.*"
# goto :wait_for_command




