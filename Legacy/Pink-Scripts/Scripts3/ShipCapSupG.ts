# Script Name      : SupGShipCap
# Author           : SupG
# Description      : Calculates appropriate amount of figs to shoot at an enemy ship
#                    to capture it, shoots one time and is activated by user pressing
#                    Y at the attack prompt on the target ship.
# Trigger Point    : Sector command prompt
# Warnings         : The first time you run this script in any particular game it goes
#                    through the ship catalog and gets the known ship specs, up to 2
#                    pages of ships will be recorded. Don't run this for the first time
#		     in a particular game if you are trying to cap an online players ship
#		     or you will be waiting for the ship catalog to scroll by, start this 
#		     script when you first begin a game so you have the data available.
# Other            : You are free to modify this script to your liking, but please give me
#                    credit for any of my code that you use.
#		     By releasing the source code to this script I fear that people with 
#		     scripting knowledge will know better how to "beat" this script, though
#		     I highly doubt many people will actually use the tactics involved that it
#		     would take to do so.
#attack
#systemscript
#initialize Variables
setVar $cap_file GAMENAME & "_SHIPS.txt"
setVar $two_page 0
setVar $ship_counter 0
setVar $count_me 0
fileExists $chck $cap_file
# check if we can run it from here
cutText CURRENTLINE $location 1 7
if ($location <> "Command") AND ($location <> "Citadel")
        clientMessage "This script must be run from the command menu"
        halt
end
#turn animations off
send "cn"
waitfor "(2) Animation display"
getWord CURRENTLINE $ansi_onoff 5

if ($ansi_onoff = "On")
	send "2qq"
else
	send "qq"
end

if ($chck = 0)
	setVar $alpha[1] "A"
	setVar $alpha[2] "B"
	setVar $alpha[3] "C"
	setVar $alpha[4] "D"
	setVar $alpha[5] "E"
	setVar $alpha[6] "F"
	setVar $alpha[7] "G"
	setVar $alpha[8] "H"
	setVar $alpha[9] "I"
	setVar $alpha[10] "J"
	setVar $alpha[11] "K"
	setVar $alpha[12] "L"
	setVar $alpha[13] "M"
	setVar $alpha[14] "N"
	setVar $alpha[15] "O"
	setVar $alpha[16] "P"
	setVar $alpha[17] "R"
	setVar $alpha[18] "A"
	setVar $alpha[19] "B"
	setVar $alpha[20] "C"
	setVar $alpha[21] "D"
	setVar $alpha[22] "E"
	setVar $alpha[23] "F"
	setVar $alpha[24] "G"
	setVar $alpha[25] "H"
	setVar $alpha[26] "I"
	goto :chk_ships
else
	goto :cap_ship
end

#get ship specifications
:chk_ships
send "CC?"
waitFor "<A>"
add $ship_counter 1
setVar $ship_name[1] CURRENTLINE
setVar $strip_it "<A> "
stripText $ship_name[$ship_counter] $strip_it
:ship_chk_loop
setTextLineTrigger grab_ship :get_shipnames
pause
:get_shipnames
if (CURRENTLINE <> "")
	add $ship_counter 1
	setVar $ship_name[$ship_counter] CURRENTLINE
	setVar $strip_it "<" & $alpha[$ship_counter] & "> "
	stripText $ship_name[$ship_counter] $strip_it
	goto :ship_chk_loop
else
	if ($two_page = 1)
		send "+"
		goto :get_stats
	else
		goto :chk_page_two
	end
end

:chk_page_two
setTextLineTrigger more :two_page "<+> Next Page"
waitFor "<Q> To Leave"
killtrigger more
#pause
goto :get_stats

:two_page
waitFor "Which ship are you interested in (?=List) ?"
setVar $two_page 1
send "+"
waitFor "<A>"
add $ship_counter 1
setVar $ship_name[$ship_counter] CURRENTLINE
setVar $strip_it "<" & $alpha[$ship_counter] & "> "
stripText $ship_name[$ship_counter] $strip_it
goto :ship_chk_loop

:get_stats
if ($two_page = 1)
	if ($count_me > 16)
		setVar $two_page 0
		send "+"
	end
end
if ($count_me < $ship_counter)
	add $count_me 1
	send $alpha[$count_me]
	setTextLineTrigger hc :hc "Basic Hold Cost:"
	pause
	:hc
	setVar $line_chk 1
	setVar $max_shield_line CURRENTLINE
	stripText $max_shield_line "Basic Hold Cost:"
	stripText $max_shield_line "Initial Holds:"
	stripText $max_shield_line "Maximum Shields:"
	getWord $max_shield_line $max_Shields 3
	stripText $max_Shields ","
	setTextLineTrigger do :do "Defensive Odds:"
	pause
	
	:do		
	setVar $line_chk 1
:chk_line_def
	getWord CURRENTLINE $def[$line_chk] $line_chk
	if ($def[$line_chk] = "Odds:")
		add $line_chk 1
		getWord CURRENTLINE $def_odds $line_chk
		stripText $def_odds ":1"
		stripText $def_odds "."
	else
		add $line_chk 1
		goto :chk_line_def
	end
	write $cap_file $max_shields & " " & $def_odds & " " & $ship_name[$count_me]
	goto :get_stats
	
end
send "qq"
goto :cap_ship

#time for some ship cappin
:cap_ship
#get own offensive odds
send "c;"
setTextLineTrigger oo :oo "Offensive Odds:"
pause

:oo
setVar $oddsline CURRENTLINE
stripText $oddsline "Main Drive Cost:"
stripText $oddsline "Max Fighters:"
stripText $oddsline "Offensive Odds:"
getword $oddsline $real_odds 3
stripText $real_odds "."
stripText $real_odds ":1"
send "q"

:wait
#wait for user to attack
setVar $unmanned "NO"
setVar $own_odds $real_odds 
setVar $cap_points 0
setVar $max_figs 0
setVar $cap_shield_points 0
setVar $ship_fighters 0
waitFor "[N]? Yes"
setVar $cap_ship_info CURRENTLINE
getWord $cap_ship_info $attack_prompt 1
if ($attack_prompt <> "Attack")
	goto :wait
end
setVar $type_count 0
setVar $is_ship 0
#find out the ship type
:ship_type
add $type_count 1
read $cap_file $ship_data $type_count
if ($ship_data = "EOF")
	echo ANSI_12 "*Ship Type not determined*"
	goto :wait
end
getWord $ship_data $word1 1
getWord $ship_data $word2 2
stripText $ship_data $word1
stripText $ship_data $word2
stripText $ship_data "  "
getWordPos $cap_ship_info $is_ship $ship_data
getWordPos $cap_ship_info $unman "'s unmanned"
if ($unman > 0)
	setVar $unmanned "YES"
end
if ($is_ship > 0)
	setVar $cap_ship_shields $word1
	setVar $cap_ship_odds $word2
	goto :send_attack
else
	goto :ship_type
end

#how many fighters do they have
:send_attack
getText $cap_ship_info $ship_fighters $ship_data "(Y/N)"
getText $ship_fighters $ship_fighters "-" ")"
stripText $ship_fighters ","
setVar $ship_shield_percent 0
#how many shields do they have
setTextLineTrigger combat :combat_scan "Combat scanners show enemy shields at"
setTextTrigger nocombat :cap_it "How many fighters do you wish to use"
pause

:combat_scan
getWord CURRENTLINE $ship_shield_percent 7
stripText $ship_shield_percent "%"
multiply $cap_ship_shields $ship_shield_percent
divide $cap_ship_shields 100
setVar $cap_shield_points $cap_ship_shields
pause

#calculate and attack
:cap_it
killtrigger combat
getWord CURRENTLINE $max_figs 11
stripText $max_figs ","
stripText $max_figs ")"
setVar $cap_points 0
add $cap_points $cap_shield_points
add $cap_points $ship_fighters
multiply $cap_points $cap_ship_odds
multiply $own_odds 95
divide $own_odds 100
divide $cap_points $own_odds

if ($unmanned = "YES")
	divide $cap_points 2
end
if ($cap_points = 0)
	setVar $cap_points 1
elseif ($cap_points > $max_figs)
	setVar $cap_points $max_figs
end
send $cap_points "*"
goto :wait

