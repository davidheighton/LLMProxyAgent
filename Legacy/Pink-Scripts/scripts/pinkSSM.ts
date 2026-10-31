ReqRecording
# TWX SupG Script  : SupGWorldSSM2
# TWX Pink Mods    : pinkSSM
# Author           : SupG
# Description      : Searches universe for good SSM spots and makes
#                    you tons of cash
# Trigger Point    : Command prompt
# Warnings         : Make sure you have ample starting cash 500k should do it
# Other            : Let me know of any problems with this script
# Modifications    : Pink (pinky@exiled.org) added quick fighter increase and limpet/armid dropping
#www.twtavern.com


#setVar $pinkfigsper 10
#setVar $pinkdroplimpets 0
#setVar $pinkdroparmids 0
setVar $pinkASectorCount 0
setVar $pinkLSectorCount 0
setVar $status "Finding Port"
setVar $total_busts 0
cutText CURRENTLINE $location 1 12
if ($location <> "Command [TL=")
        clientMessage "This script must be run from the command menu"
        halt
end

#Script Initialization and Info gathering.
setVar $ssm_info_file GAMENAME & ".wsm"
setVar $bust_file GAMENAME & ".bst"
setVar $clear_busts "NO"

#get previous settings for this game
fileExists $exist $ssm_info_file
if ($exist = "TRUE")
	read $ssm_info_file $class_zero 1
	read $ssm_info_file $steal_divisor 2
	read $ssm_info_file $restock 3
	read $ssm_info_file $bust_protect 4
	read $ssm_info_file $smart_escape 5
	read $ssm_info_file $escape_ship 6
	read $ssm_info_file $security 7
	read $ssm_info_file $view_status 8
	read $ssm_info_file $pinkfigsper 9
	read $ssm_info_file $pinkdroplimpets 10
	read $ssm_info_file $pinkdroparmids 11
	read $ssm_info_file $pinklimpetfreq 12
        read $ssm_info_file $pinkarmidfreq 13
else
	setVar $steal_divisor 30
	setVar $class_zero "1"
	setVar $restock "NO"
	setVar $bust_protect "ON"
	setVar $smart_escape "OFF"
	setVar $security "YES"
	setVar $view_status "YES"
	setVar $pinkfigsper 1
	setVar $pinkdroplimpets 0
 	setVar $pinkdroparmids 0
	setvar $pinklimpetfreq 5
	setVar $pinkarmidfreq 1
end
#allow for version upgrading-downward compatability
if ($security  = "EOF")
	setVar $security "YES"
end
if ($view_status  = "EOF")
	setVar $view_status "YES"
end
if ($bust_protect <> "YES" and $bust_protect <> "NO")
	setVar $bust_protect "YES"
end
if ($security = "NO")
	setVar $menu7 2
end
if ($smart_escape = "ON")
	setVar $menu4 2
end	
fileExists $exist $bust_file
if ($exist = "TRUE")
	setVar $clear_it 1
	setVar $clear_bust "NO"
end

#main menu code
:main_menu
echo ANSI_9 "*--" ANSI_1 "=" ANSI_15 "    SupG's World SSM Ver 2.0 with Pink Modifications       " ANSI_1 "=" ANSI_9 "--*"
echo ANSI_9 "--" ANSI_1 "=" ANSI_15 " Press the number in front of the Option you wish to change" ANSI_1 "=" ANSI_9 "--**"
echo ANSI_10 "      Number                 Option                   Value*"
echo ANSI_7  "     -=-=-=-              -=-=-=-=-=-=-              -=-=-=-=-*"
echo ANSI_15 "       1                  Class 0 Port                " ANSI_14 $class_zero "*"
echo ANSI_15 "       2                  Steal divisor               " ANSI_14 $steal_divisor "*"
echo ANSI_15 "       3                  Restock Figs/Shields        " ANSI_14 $restock "*"
if ($smart_escape = "OFF")
	echo ANSI_15 "       4                  Smart Escape                " ANSI_14 $smart_escape "*"
else
	echo ANSI_15 "       4                  Smart Escape                " ANSI_14 $smart_escape & " " & $escape_ship "*"
end
echo ANSI_15 "       5                  Fake Bust Protection        " ANSI_14 $bust_protect "*"
if ($clear_it = 1)
	echo ANSI_15 "       6                  Clear Busts                 " ANSI_14 $clear_bust "*"
end
echo ANSI_15 "       7                  SSM Security                " ANSI_14 $security "*"
echo ANSI_15 "       8                  Show Status Window          " ANSI_14 $view_status "*"
echo ANSI_15 "       F                  How many Figs Per Sector    " ANSI_14 $pinkfigsper "*"
echo ANSI_15 "       L                  Limpet Drop Per Sector      " ANSI_14 $pinkdroplimpets "*"
echo ANSI_15 "       K                  Limpet Frequency            " ANSI_14 "Every " $pinklimpetfreq " Sectors*"
echo ANSI_15 "       A                  Armid Drop Per Sector       " ANSI_14 $pinkdroparmids "*"
echo ANSI_15 "       S                  Armid Frequency             " ANSI_14 "Every " $pinkarmidfreq " Sectors*"
echo ANSI_11 "*       C                  Run Script"
getConsoleInput $selection SINGLEKEY

if ($selection = 1)
:class_zero
	getInput $class_zero "Where is a Class 0 port?" 1
	isNumber $number $class_zero
	if ($number = "FALSE")
		echo ansi_12 "*Invalid Sector*"
		goto :class_zero
	end
	goto :main_menu
elseif ($selection = 2)
:steal_divisor	
	getInput $steal_divisor "What is the Steal divisor" 30
	isNumber $number $steal_divisor
	if ($number = "FALSE")
		echo ansi_12 "*Invalid divisor*"
		goto :steal_divisor
	end
	goto :main_menu
elseif ($selection = 3)
	add $menu3 1
	if ($menu3 = 2) 
		setVar $restock "NO"
	else
		setVar $menu3 1
		setVar $restock "YES"
	end
	goto :main_menu
elseif ($selection = 4)
	add $menu4 1
	if ($menu4 = 2) 
		setVar $smart_escape "ON"
		getInput $escape_ship "What is the ship number of your escape ship?"
	else
		setVar $menu4 1
		setVar $smart_escape "OFF"
	end
	goto :main_menu
elseif ($selection = 5)
	add $menu5 1
	if ($menu5 = 2) 
		setVar $bust_protect "NO"
	else
		setVar $menu5 1
		setVar $bust_protect "YES"
	end
	goto :main_menu
elseif ($selection = 6) and ($clear_it = 1)
	setVar $clear_it 0
	delete $bust_file
	goto :main_menu
elseif ($selection = 7)
	add $menu7 1
	if ($menu7 = 2) 
		setVar $security "NO"
	else
		setVar $menu7 1
		setVar $security "YES"
	end
	goto :main_menu
elseif ($selection = 8)
	add $menu8 1
	if ($menu8 = 2) 
		setVar $view_status "NO"
	else
		setVar $menu8 1
		setVar $view_status "YES"
	end
	goto :main_menu

elseif ($selection = "F")
	getInput $pinkfigsper "How many Figs Per Sector?" 10
	goto :main_menu

elseif ($selection = "L")
	getInput $pinkdroplimpets "How many Limpets Per Sector?" 0
	goto :main_menu

elseif ($selection = "A")
	getInput $pinkdroparmids "How many Armids Per Sector?" 0
	goto :main_menu


elseif ($selection = "K")
	getInput $pinklimpetfreq "Wait how many sectors per Limpet Drop?" 0
	goto :main_menu

elseif ($selection = "S")
	getInput $pinkarmidfreq "Wait how many sectors per Armid Drop?" 0
	goto :main_menu

elseif ($selection = "C")
	delete $ssm_info_file
	write $ssm_info_file $class_zero
	write $ssm_info_file $steal_divisor
	write $ssm_info_file $restock
	write $ssm_info_file $bust_protect	
	write $ssm_info_file $smart_escape
	write $ssm_info_file $escape_ship
	write $ssm_info_file $security
	write $ssm_info_file $view_status
	write $ssm_info_file $pinkfigsper 
	write $ssm_info_file $pinkdroplimpets
	write $ssm_info_file $pinkdroparmids
	write $ssm_info_file $pinklimpetfreq
        write $ssm_info_file $pinkarmidfreq

	goto :start
else
	goto :main_menu
end

#Start the madness==-=-=-=-=-=-=-=-=-==-==-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=--=-
:start
send "c;q"
setTextLineTrigger oo :oo "Offensive Odds:"
pause
:oo
setVar $info_line CURRENTLINE
stripText $info_line "Main Drive Cost:"
stripText $info_line "Max Fighters:"
stripText $info_line "Offensive Odds:"
getWord $info_line $off_odds 3
striptext $off_odds "."
striptext $off_odds ":1"
send "jy"
send "cn"
setTextLineTrigger ad :ad "(2) Animation display"
pause
:ad
getWord CURRENTLINE $ansi_onoff 5
if ($ansi_onoff = "On")
	send "2qq"
else
	send "qq"
end

if ($bust_protect = "YES")
	gosub :get_busts
end
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
	goto :start_sec
end

#where are we starting
:start_sec
send "D"
setTextLineTrigger startsec :starter "Sector  :"
pause
:starter 
getWord CURRENTLINE $current_sector 3
if ($view_status = "YES")
	Window status 200 100 "Status" yes
end
#warp algorithm =-=-=-=-=-==-=-=-=-=-=-=-=-=-=-=-=-==-=-=-=-=-=-=-=-=-=--=
:warp
setVar $status "Finding Pair"
if ($view_status = "YES")
	gosub :update_window
end
setVar $good_port "FALSE"
setVar $loop 0
send "SDSH"
waitFor "Long Range Scan"
waitFor "Command [TL="
getSector $current_sector $sector_info
if ($sector_info.port.exists = 1)
	if ($sector_info.port.buy_equip = "YES") and ($sector_info.armidmines.quantity = 0)
		setVar $chk_bust $current_sector
		gosub :is_busted
		if ($busted = "TRUE")
			setVar $good_port "FALSE"
		else
			setVar $good_port "TRUE"
		end
	end
end
#weighting system inspired by Xide
:check_adj
setVar $warp_probability 500
if ($loop < $sector_info.warps)
	add $loop 1
	getSector $sector_info.warp[$loop] $adj_sector
	if ($sector_info.warp[$loop] > 0) and ($sector_info.warp[$loop] < 11)
		add $warp_probability 99999
	end
	if ($ADJ_SECTOR.CONSTELLATION = "The Federation.")
		add $warp_probability 99999
	end
	if ($adj_sector.density > 1000)
		add $warp_probability 100
	end
	if ($adj_sector.density = 100) and ($adj_sector.port.exists = 1)
		subtract $warp_probability 2
	end
	if ($adj_sector.anomoly = "YES")
		add $warp_probability 9999
	end
	if ($adj_sector.warp[$loop] = $previous_sector)
		add $warp_probability 75
	end
	if ($adj_sector.port.exists = 1)
		if ($adj_sector.port.buy_equip = "YES")
			subtract $warp_probability 6
		end
	end
	if ($adj_sector.port.exists = 1) and ($sector_info.port.buy_equip = "YES") and ($adj_sector.port.buy_equip = "YES")  and ($good_port = "TRUE") and ($adj_sector.density < 600) and ($adj_sector.anomoly = "NO") and ($busted = "FALSE") and ($adj_sector.armidmines.quantity = 0)
		setVar $chk_bust $sector_info.warp[$loop]
		gosub :is_busted
		if ($busted = "TRUE")
			add $warp_probability 5
		else
			send "cf" $sector_info.warp[$loop] "*" $current_sector "*q"
			waitfor "<Computer deactivated>"
			getDistance $one_way_chk $sector_info.warp[$loop] $current_sector
			if ($one_way_chk > 1)
				add $warp_probability 4
			else
				goto :SSM
			end
		end
	end	
	subtract $warp_probability $adj_sector.warps
	getRnd $rand_prob 0 20
	subtract $warp_probability $rand_prob
	setVar $next_warp[$loop] $warp_probability
	
	if ($loop = 1)
		setVar $best_probability $warp_probability
		setVar $best_index $loop
	else
		if ($warp_probability < $best_probability)
			setVar $best_probability $warp_probability
			setVar $best_index $loop
		end
	end
	goto :check_adj		

end

setVar $sector $sector_info.warp[$best_index]
gosub :moving
setVar $previous_sector $current_sector
setVar $current_sector $sector_info.warp[$best_index]
# send "f1*ct"
# Pink Mods.

send "f" $pinkfigsper "*ct"

if ($pinkdroplimpets > 0)
	if ($pinkLSectorCount >= $pinklimpetfreq)
	send "h2" $pinkdroplimpets "*c"
	setVar $pinkLSectorCount 1
	else
	Add $pinkLSectorCount 1
	end
end

if ($pinkdroparmids > 0)
	if ($pinkASectorCount >= $pinkarmidfreq)
	send "h1" $pinkdroparmids "*c"
	setVar $pinkASectorCount 1
	else
	Add $pinkASectorCount 1
	end
end

goto :warp

#SSM subroutine		
:SSM
setVar $status "SSM"
send "I"
setTextLineTrigger re :re "Rank and Exp   :"
setTextLineTrigger th :th "Total Holds    :"
setTextLineTrigger cr :cr "Credits        :"
pause
:re
getWord CURRENTLINE $experience 5
stripText $experience ","
pause
:th
getWord CURRENTLINE $holds 4
pause
:cr
getWord CURRENTLINE $credits 3
setVar $total_credits $credits
stripText $credits ","

setVar $sec[1] $current_sector
setVar $sec[2] $sector_info.warp[$loop]
setVar $sec_index 2
if ($view_status = "YES")
	gosub :update_window
end
send "M" $sec[$sec_index] "*"
waitfor "<Move>"
gosub :clear_sector
# send "f1*ct"
# Pink Mods.

send "f" $pinkfigsper "*ct"

if ($pinkdroplimpets > 0)
	if ($pinkLSectorCount >= $pinklimpetfreq)
	send "h2" $pinkdroplimpets "*c"
	setVar $pinkLSectorCount 1
	else
	Add $pinkLSectorCount 1
	end
end

if ($pinkdroparmids > 0)
	if ($pinkASectorCount >= $pinkarmidfreq)
	send "h1" $pinkdroparmids "*c"
	setVar $pinkASectorCount 1
	else
	Add $pinkASectorCount 1
	end
end

:start_SSM
setVar $steal_holds $experience
divide $steal_holds $steal_divisor
if ($steal_holds > $holds)
	setVar $steal_holds $holds
end
send "pr*s"
setTextLineTrigger fake :steal_bust "Suddenly you're Busted!"
setTextTrigger good :cont "Which product?"
setTextLineTrigger wentpos :went_good "<Re-Display>"
pause

:cont
killtrigger wentpos
killtrigger fake
send "3" $steal_holds "*"
setTextLineTrigger success :steal_success "Success!"
setTextLineTrigger bust :steal_bust "Suddenly you're Busted!"
setTextLineTrigger upgrade :upgrade_port "There aren't that many holds of Equipment at this port!"
pause

:went_good
killtrigger upgrade
killtrigger fake
killtrigger good
send "q"
setVar $status "Positive Alignment"
if ($view_status = "YES")
	gosub :update_window
end
goto :keepalive

:steal_bust
killtrigger smokey
killtrigger wentpos
killtrigger good
killtrigger success
killtrigger upgrade
add $total_busts 1
write $bust_file $sec[$sec_index]
if ($bust_protect = "YES")
	add $five_count 1
	setVar $bust[$five_count] 1
end
setVar $current_sector $sec[$sec_index]
gosub :restock
goto :warp

:upgrade_port
killtrigger success
killtrigger bust
killtrigger good
setVar $upgrade_amount $steal_holds
divide $upgrade_amount 9
send "o3" $upgrade_amount "*q"
goto :start_SSM

:steal_success
killtrigger bust
killtrigger upgrade
killtrigger good

setVar $add_exp $steal_holds
multiply $add_exp 88
if ($add_exp < 1000)
	goto :norec
end
divide $add_exp 1000
goto :rec

:norec
killtrigger rec
setVar $status "Not enough exp"
goto :keepalive

:rec
add $experience $add_exp

:sell_product
getSector $sec[$sec_index] $port_info
send "pt**"
if ($port_info.port.buy_ore = "NO")
	send "0*"
end
if ($port_info.port.buy_org = "NO")
	send "0*"
end
if ($view_status = "YES")
	setTextLineTrigger ch :ch "empty cargo holds."
	pause
	:ch
	getWord CURRENTLINE $total_credits 3
	gosub :update_window
end
add $sec_index 1
if ($sec_index > 2)
	setVar $sec_index 1
end
send "m" $sec[$sec_index] "*"
if ($security = "YES")
	gosub :clear_sector
end


goto :start_SSM


:is_busted
setVar $bust_count 0
if ($bust_protect = "NO")
	setVar $busted "FALSE"
	return
elseif ($bust_protect = "YES") and ($bust[$chk_bust] = 1)
	setVar $busted "TRUE"
else
	setVar $busted "FALSE"
end
return

:restock
setVar $status "Restock"
setVar $sector $class_zero
if ($view_status = "YES")
	gosub :update_window
end
gosub :moving

send "pt"
setTextLineTrigger c0c :c0c "You have"
pause
:c0c
getWord CURRENTLINE $credits 3
stripText $credits ","
setTextLineTrigger limpet :remove_limpet "A port official runs up to you"
setTextLineTrigger c0h :c0h "A  Cargo holds     :"
pause

:remove_limpet
send "y"
pause

:c0h
killtrigger limpet
getWord CURRENTLINE $max_buyable_holds 10
send "a" $max_buyable_holds "*y"
setTextLineTrigger c0mh :c0mh "more holds is"
pause
:c0mh
getWord CURRENTLINE $price 8
stripText $price ","
subtract $credits $price
if ($restock = "YES")
	setTextLineTrigger c0sp :c0sp "C  Shield Points   :"
	pause
	:c0sp
	getWord CURRENTLINE $shield_price 5
	getWord CURRENTLINE $max_shields_buy 9
	echo "gotshields"
	setVar $total_price $shield_price
	multiply $total_price $max_shields_buy
	setVar $cred_difference $credits
	subtract $cred_difference $max_shields_buy
	if ($cred_difference > 500000) and ($max_shields_buy > 0)
		send "c" $max_shields_buy "*"
		subtract $credits $total_price
	else
		send "?"
	end
	setTextLineTrigger c0fg :c0fg "B  Fighters        :"
	pause
	:c0fg
	getWord CURRENTLINE $fig_price 4
	getWord CURRENTLINE $max_figs_buy 8
	setVar $buy_figgies $max_figs_buy
	setVar $fig_cost $fig_price
	multiply $fig_cost $max_figs_buy
	setVar $creds $credits
	subtract $creds $fig_cost
	if ($creds < 500000) or ($max_figs_buy = 0)
		setVar $cred_buy $credits
		subtract $cred_buy 500000
		setVar $buy_figgies $cred_buy
		divide $buy_figgies $fig_price
		setVar $max_figs_buy $buy_figgies
		
	end	
	if ($max_figs_buy > 0)
		send "b" $max_figs_buy "*"
	end
end
send "Q"
:warping_out
getRnd $sec[$sec_index] 11 SECTORS
setVar $status "Warping Out"
setVar $sector $sec[$sec_index]
if ($view_status = "YES")
	gosub :update_window
end
gosub :moving
goto :warp


:moving
send "m" $sector "*"
waitfor "<Move>"
if ($smart_escape = "ON")
	setTextLineTrigger escape :escape "An Interdictor Generator in this sector holds you fast!"
end
setTextTrigger twarp :no_twarp "Do you want to engage the TransWarp drive?"
setTextTrigger express :express_warp "Engage the Autopilot?"
setTextTrigger in_adj :there "Sector  : " & $sector
setTextLineTrigger voided_sec :voided "DANGER! You have marked sector"
pause

:voided
killtrigger twarp
killtrigger escape
killtrigger in_adj
killtrigger express
getWord CURRENTLINE $void 7
send "n"
if ($class_zero = $void)
	setVar $status "Class 0 Avoided"
	if ($view_status = "YES")
		gosub :update_window
	end
	goto :keepalive
end
if ($status = "Restock")
	goto :warp_out
else
	goto :warp
end

:no_twarp
killtrigger escape
killtrigger in_adj
killtrigger express
killtrigger voided_sec
send "n"

:express_warp
killtrigger twarp
killtrigger escape
killtrigger in_adj
killtrigger voided_sec
setVar $current_sector $sec[$sec_index]
send "e"

:there
killtrigger voided_sec
killtrigger escape
killtrigger twarp
killtrigger express
gosub :clear_sector
return

:kill_figs
killalltriggers
send "a999*"
return

:no_avoid
send "n"
return

:clear_sector
setVar $option_prompt 0
if ($smart_escape = "ON")
	setTextLineTrigger escape :escape "An Interdictor Generator in this sector holds you fast!"
end
setTextTrigger hitfig :hit_fig "Your fighters:"
setTextTrigger hitmine :hit_mine "Mined Sector: Do you wish to Avoid this sector in the future? (Y/N)"
setTextLineTrigger clear :ready_state "Autopilot disengaging."
setTextTrigger done :ready_state "Command [TL="
setTextTrigger continue :keep_rollin "Stop in this sector"
pause

:keep_rollin
killtrigger figgie
killtrigger hitmine
killtrigger clear
killtrigger hitfig
killtrigger done
killtrigger escape
send "n"
goto :clear_sector

:hit_fig
killtrigger hitmine
killtrigger clear
killtrigger continue
killtrigger done
killtrigger escape
getWord CURRENTLINE $sector_figgies 6
getWord CURRENTLINE $total_attack_points 3
striptext $total_attack_points ","
striptext $sector_figgies ","
multiply $total_attack_points $off_odds
divide $total_attack_points 10
setVar $ten_percent $total_attack_points
divide $ten_percent 10
subtract $total_attack_points $ten_percent
if ($sector_figgies > $total_attack_points)
	send "d"
	waitfor "Sector  :"
	getWord CURRENTLINE $avoid_sector 3
	send "r"
	setTextLineTrigger screwed :screwed "Sorry, you have nowhere to retreat to!"
	setTextTrigger keepagoin :goin "Command [TL="
	pause
:goin
	killtrigger screwed
	send "cv" $avoid_sector "*q"
	if ($avoid_sector = $class_zero)
		getRnd $run_sec 11 SECTORS
		send  "M" $run_sec "*"
		waitfor "<Move>"
		gosub :moving
		setVar $status "Class 0 Blocked"
		if ($view_status = "YES")
			gosub :update_window
		end
		goto :keepalive
	else
		if ($status = "Warping Out")
			goto :warping_out
		elseif ($status = "Restock")
			goto :restock
		else
			goto :warp
		end
	end
end	
gosub :kill_figs
goto :clear_sector

:hit_mine
killtrigger hitfig
killtrigger clear
killtrigger continue
killtrigger done
killtrigger escape
gosub :no_avoid
goto :clear_sector

:ready_state
killtrigger hitmine
killtrigger hitfig
killtrigger continue
killtrigger done
killtrigger clear
killtrigger escape
return

:escape
killtrigger hitmine
killtrigger clear
killtrigger hitfig
killtrigger done
killtrigger clear
killtrigger continue
gosub :clear_sector
setVar $status "ESCAPE!!"
if ($view_status = "YES")
	gosub :update_window
end
send "x" 
setTextLineTrigger noships :no_xport "You do not own any other ships!"
setTextTrigger entershipnumber :ship_number "Choose which ship to beam to"
pause
:ship_number
send $escape_ship "*  q"
setTextLineTrigger nohop :no_xport "only has a transport range of"
setTextLineTrigger safe :xport "Security code accepted,"
setTextLineTrigger noship :no_xport "That is not an available ship."
setTextLineTrigger noturn :screwed "any turns left."
pause

:no_xport
send "pr*"
:screwed
send "'Help, my TA*"
goto :keepalive
	
:xport
goto :keepalive

:keepalive
killalltriggers
setDelayTrigger keep :keep 60000
pause

:update_window
setWindowContents status "Operation: " & $status & "*Total Busts: " & $total_busts & "*Credits: " & $total_credits & "*Experience: " & $experience
return

:keep
send "#"
goto :keepalive

#reads the busted file and stores the busted sectors in an array for easy access
:get_busts
setVar $five 0
fileExists $file_test $bust_file
if ($file_test = "FALSE")
	return
end
:chk_five
if ($five <> "EOF")
	add $five_count 1
	read $bust_file $five $five_count
	setVar $busts[$five] 1
	goto :chk_five
end
return