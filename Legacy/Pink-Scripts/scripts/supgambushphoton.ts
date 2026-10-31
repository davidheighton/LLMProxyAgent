# Script Name      : SupGAmbushPhoton
# Author           : SupG
# Description      : Surrounds current sector (if chosen), and waits for someone to
#                    hit a fighter in an adjacent sector and photons them.  Works at
#		     Stardock (auto re-buys photons, and option to hide on dock), and
#		     also works in open space as well as fedspace with an adjacent
#		     non-fedspace sector. Will lay armid mines if chosen, and will not
#		     take you through a one way warp.
# Trigger Point    : Sector command prompt
# Warnings         : This script may error out if you have not explored the adjacent
#		     sectors from where you are running it. The one-way warp protection
#		     uses getDistance which needs adequate mapping data to work properly
# Other            : You are free to modify this script to your liking, but please give me
#                    credit for any of my code that you use.
# Scripter's Tavern : www.twtavern.com

setVar $warp_loop 0
setVar $fig_type "d"
setVar $where_at "Stardock"
setVar $surround_figs "No"
setVar $surround_dock "N"
setVar $fighter_type "Defensive"
setVar $surround_mines "No"
SetVar $hide_dock "No"
setVar $hide "N"
setVar $lay_mines "N"
setVar $menu1 1
setVar $menu2 1
setVar $menu3 1
setVar $menu4 1
setVar $menu5 1
setVar $first_warp 0
:menu
echo ANSI_3 "*-=" ANSI_9 "SupG's Surround/Photon Script" ANSI_3 "=-*"
echo ANSI_15 "*Press the number in front of the option value you wish to change.*"
echo ANSI_15 "*Number   Option                Value*"
echo ANSI_12 "------   -------               ---------*"
echo ANSI_15 "  1      Location              " ANSI_9 $where_at 
echo ANSI_15 "*  2      Surround Sector       " ANSI_9 $surround_figs
if ($menu2 = 2)
	echo ANSI_15 "*  3      Fighter Type          " ANSI_9 $fighter_type
	echo ANSI_15 "*  4      Lay Armid Mines       " ANSI_9 $surround_mines
else
	echo ANSI_8 "*  3      Fighter Type          " ANSI_9 $fighter_type
	echo ANSI_8 "*  4      Lay Armid Mines       " ANSI_9 $surround_mines
end
if ($menu1 = 1)
	echo ANSI_15 "*  5      Hide On Dock          " ANSI_9 $hide_dock "*"
else
	echo ANSI_8 "*  5      Hide On Dock          " ANSI_9 $hide_dock "*"
end
echo ANSI_15 "*  C      Run Script*"

getConsoleInput $selection SINGLEKEY

if ($selection = 1)
	add $menu1 1
	if ($menu1 = 1) or ($menu1 > 2)
		setVar $menu1 1
		setVar $at_SD "Y"
		setVar $where_at "Stardock"
	elseif ($menu1 = 2)
		setVar $at_SD "N"
		setVar $where_at "Other Sector"
	end
	goto :menu
elseif ($selection = 2)
	add $menu2 1
	if ($menu2 = 1) or ($menu2 > 2)
		setVar $surround_dock "N"
		setVar $surround_figs "NO"
		setVar $menu2 1
	elseif ($menu2 = 2)
		setVar $surround_dock "Y"
		
:fig_amount
		getInput $num_fighters "How many fighters?"
		isNumber $num_check $num_fighters
		if ($isNumber = "FALSE")
			goto :fig_amount
		end
		setVar $surround_figs "Yes : "  & $num_fighters
	end
	goto :menu
elseif ($selection = 3)
	if ($menu2 = 1)
		echo ANSI_7 "*Cannot change fighter type. Surround Sector disabled.*"
		goto :menu
	end
	add $menu3 1
	if ($menu3 = 1) or ($menu3 > 3)
		setVar $menu3 1
		setVar $fig_type "D"
		setVar $fighter_type "Defensive"
	elseif ($menu3 = 2)
		setVar $fig_type "T"
		setVar $fighter_type "Toll"
	elseif ($menu3 = 3)
		setVar $fig_type "O"
		setVar $fighter_type "Offensive"
	end
	goto :menu
elseif ($selection = 4)
	if ($menu2 = 1)
		echo ANSI_7 "*Cannot use mines. Surround Sector disabled.*"
		goto :menu
	end
	add $menu4 1
	if ($menu4 = 1) or ($menu4 > 2)
		setVar $lay_mines "N"
		setVar $surround_mines "No"
		setVar $menu4 1
	elseif ($menu4 = 2)
		setVar $lay_mines "Y"
		
:mine_amount
		getInput $num_mines "How many mines?"
		isNumber $num_check $num_mines
		if ($isNumber = "FALSE")
			goto :mine_amount
		end
		setVar $surround_mines "Yes : " & $num_mines
	end
	goto :menu
elseif ($selection = 5)
	if ($menu1 = 2)
		echo ANSI_7 "*Cannot hide on dock cause we ain't at dock.*"
		goto :menu
	end
	add $menu5 1
	if ($menu5 = 1) or ($menu5 > 2)
		setVar $menu5 1
		setVar $hide "N"
		setVar $hide_dock "No"
	elseif ($menu5 = 2)
		setVar $hide "Y"
		setVar $hide_dock "Yes"
	end
	goto :menu
elseif ($selection = "C")
	goto :run_it
else
	goto :menu
end

:run_it
# check if we can run it from here
cutText CURRENTLINE $location 1 12
if ($location <> "Command [TL=")
        clientMessage "This script must be run from the command menu"
        halt
end

:turn_off_ansi
send "cv*yyq"
send "cn"
waitfor "(2) Animation display"
getWord CURRENTLINE $ansi_onoff 5

if ($ansi_onoff = "On")
	send "2qq"
else
	send "qq"
end
# find out which sector this is
send "d"
waitFor "Sector  :"
getWord CURRENTLINE $sector 3
# get all adjacent sectors and move
getSector $sector $sector_info
if ($surround_dock = "Y")
	goto :begin_surround
elseif ($surround_dock = "N")
	goto :wait
end

#make sure there isn't any oneways--- evil oneways
setVar $one_way_loop 0
setVar $one_ways 0
:one_way_check
if ($one_way_loop < $sector_info.warps)
	add $one_way_loop 1
	getDistance $far $sector_info.warp[$one_way_loop] $sector
	if ($far > 1)
		add $one_ways 1
		setVar $one_way[$one_ways] $sector_info.warp[$one_way_loop]
		echo ansi_12 $sector_info.warp[$one_way_loop] " IS A ONE WAY "
	end	
	goto :one_way_check
end


if ($surround_dock = "Y")
	goto :begin_surround
elseif ($surround_dock = "N")
	goto :wait
end

:begin_surround
send "cunq"

:move
waitFor "Command [TL="
if ($warp_loop < $sector_info.warps)
:increment
	add $warp_loop 1
	if ($one_ways > 0)
	setVar $one_chk 0
:onechk		
		if ($one_chk < $one_ways)
			add $one_chk 1
			if ($sector_info.warp[$warp_loop] = $one_way[$one_chk])
				goto :increment
			else
				goto :onechk
			end
		end
	end
	if (SECTORS > 5000) or ($sector_info.warp[$warp_loop] < 600)
		if ($sector_info.warp[$warp_loop] > 10)
			send $sector_info.warp[$warp_loop] "*"
		else
			goto :increment
		end
	elseif (SECTORS = 5000) and ($sector_info.warp[$warp_loop] > 599)
     		if ($sector_info.warp[$warp_loop] > 10)
			send $sector_info.warp[$warp_loop]
		else
			goto :increment
		end
	end
	add $first_warp 1
	if ($first_warp > 1) 
		send "e"
	end
	waitFor "Warping to Sector"
else
		if (SECTORS > 5000) or ($sector < 501)
			send $sector "*"
		elseif (SECTORS = 5000) and ($sector > 500)
     			send $sector
		end
		goto :wait
end

#check for fighters and mines in sector
:clear_sector
	setTextTrigger go :keep_goin "Stop in this sector (Y,N,E,I,R,S,D,P,?) (?=Help) [N] ?"
	setTextTrigger 11 :kill_figs "Option?"
	setTextTrigger 13 :no_avoid "Mined Sector: Do you wish to Avoid this sector in the future? (Y/N)"
	setTextTrigger clear :lay_figs_mines "Command [TL="
	pause	
		
#lay fighters and mines in the sector

:lay_figs_mines
killalltriggers
if ($lay_mines = "N")  
	send "f" $num_fighters "*c" $fig_type
	if ($old_mines = "Y")
		setVar $lay_mines Y
 	end
elseif ($lay_mines = "Y")
	send "f" $num_fighters "*c" $fig_type "h1" $num_mines "*"
	waitfor "<Drop/Take Armid Mines>"
	setTextTrigger corp :make_corp "Should these be"
	setTextTrigger nocorp :move "Command [TL="
	pause
end
goto :move


:make_corp
killalltriggers
killtrigger nocorp
#pause
send "c"
goto :move


:kill_figs
killalltriggers
send "ay9999*"
goto :clear_sector

:no_avoid
killalltriggers
send "n"
if ($lay_mines = "Y")
	setVar $lay_mines N
	setVar $old_mines Y
end
goto :lay_figs_mines

:keep_goin
killalltriggers
send "*"
goto :clear_sector
#=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
:wait
send "cuyq"
#check for photon missiles
send "i" 
waitFor "<Info>"
:chk_figs
setTextTrigger no_torp :stardock "Command [TL="
setTextLineTrigger got_torps :next "Photon Missiles:"
pause
:next
killtrigger no_torp
getWord CURRENTLINE $num_torps 3
if ($hide = "Y")
  	send "ps"
else
	goto :reset_timer
end  

#=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
:got_torps
killtrigger keepalive
if ($hide = "N")
 send "q"
end
:reset_timer
setDelayTrigger keepalive :keepalive 60000
:watch_sec
setVar $s_loop 0
setTextLineTrigger trip :chk_sec "Deployed Fighters Report Sector"
pause

#=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
:chk_sec
getWord CURRENTLINE $sector_hit 5
getWord CURRENTLINE $fed 1
striptext $sector_hit ":"
if ($fed = "F")
	goto :watch_sec
end
:chk_sec_loop
if ($s_loop < $sector_info.warps)
	add $s_loop 1
	if ($sector_info.warp[$s_loop] = $sector_hit)
		goto :shoot_da_bitch
	else
		goto :chk_sec_loop
	end
else
	goto :watch_sec
end

:shoot_da_bitch
if ($hide = "Y")
	send "qcpy" $sector_hit "*q"
	sound "message.wav"
	send "'Photoned " $sector_hit ".*"
	goto :wait
else
	send "cpy" $sector_hit "*q"
	sound "message.wav"
	send "'Photoned " $sector_hit ".*"
	goto :wait
end


:stardock
 killtrigger got_torps
 if ($at_SD = "N")
	echo ansi_12 "*You don't have any ptorps. Go buy some.*"
	halt
 end
 send "ps" 
 setTextTrigger cant_torp :end_it "Sorry, your ship is not equipped to handle Photon Missiles!"
 setTextTrigger buytorps :bought_em "<Hardware Emporium> So what are you looking for (?) ?"
 send "hp1*"
 pause

:bought_em
killtrigger cant_torp
	send "q"
goto :got_torps

:end_it
killtrigger buytorps
echo ansi_12 "*Your ship can't use PTorps.. Buy a new ship."
send "qs"
halt

:keepalive 
killalltriggers
send "#"
goto :reset_timer