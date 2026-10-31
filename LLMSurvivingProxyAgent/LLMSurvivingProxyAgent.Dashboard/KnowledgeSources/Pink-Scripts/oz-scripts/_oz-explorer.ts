# explorer
# checks checks for nnf and nnf dead ends
# logs as u grid - uses subspace commands

setVar $OZ_explorer_Version "OZ-Explorer 2.0"

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Command")
        clientMessage "This script must be run from the Command Prompt"
        halt
end

# loadVar $ozexplorerSaved

# if ($ozexplorerSaved)
#	loadVar 
# else
#	setVar 
#
#	saveVar 
#	
#	
#	setVar $ozexplorerSaved 1
#	saveVar $ozexplorerSaved
# end

addMenu "" "OZ-Explorer" "OZ-Explorer Settings" "." "" "Main" FALSE
addMenu "OZ-Explorer" "Run" "Run Script." "Z" :Menu_Run "" TRUE
# addMenu "OZ-Explorer" "Sector List" "Sector List" "1" :Menu_List "" FALSE


setMenuHelp "Run" "This Option Activates the Script."
# setMenuHelp "Sector List" "This Option Sets the sectors to clear.*Please enter the sectors seperated by a semicolon.*Example - 399;455;677"

gosub :sub_setMenu
gosub :OZ_ExplorerHeader
openMenu "OZ-Explorer"

:OZ_explorerHeader
ECHO ANSI_2 "*----------------------------------*"
ECHO ANSI_10 "           " $OZ_Explorer_Version " "
ECHO ANSI_2 "*----------------------------------*"
return

:sub_setMenu
#	setMenuValue "Sector List" $sec_list
#	setMenuValue "Figs to Lay" $figs
#	setMenuValue "Type of Figs" $type
#	setMenuValue "Mines to Lay" $mines
#	setMenuValue "Limpets to Lay" $limp
#	setMenuValue "Pickup-Photon" $photon
#	setMenuValue "Pickup-Done" $pickup
#	setMenuValue "TWarp-Done" $twarp
	
return


:Menu_Run
send "'OZ-Explorer 2.0: ACTIVE : type 'help' for options.*"
# globals
setVar $fig_num 1
setVar $fig_type d
# lets get figgies first
:check_dend
setVar $dead_end_info GAMENAME & ".dend"
fileExists $exist $dead_end_info
	if ($exist = 0)
		send "'OZ-Explorer: No Dead-End file list*"
		halt
	end
gosub :figrefresh
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
# -=-=-=-=-=-=- main command triggers -=-=-=-=-=-=-=-=-=-=-
:main_triggers
killAllTriggers
setTextLineTrigger cleared :cleared "cleared"
setTextLineTrigger inac :inac "INACTIVITY WARNING"
setTextLineTrigger refresh :refresh "refresh"
setTextLineTrigger next :next "next"
setTextLineTrigger nnfsec :nnfsec "nnf "
setTextLineTrigger next_nnf :next_nnf "nnf-ta"
setTextLineTrigger fig_ta :fig_ta "fig-ta"
setTextLineTrigger add_fig :add_fig "fig"
setTextLineTrigger help :help "help"
pause


:inac
killAllTriggers
send "'OZ-Explorer 2.0: ACTIVE : type 'help' for options.*"
goto :main_triggers


:refresh
killAllTriggers
setVar $line_check CURRENTLINE
cutText $line_check $ck 1 1
if ($ck = "R")
	cutText $line_check $id 3 6
	replaceText $line_check $id " "
	replaceText $line_check "R" " "
	stripText $line_check " "
	getWordPos $line_check $word_check "refresh"
	if ($word_check = 1)
		gosub :figrefresh
	end
elseif ($ck = "'")
	stripText $line_check "'"
	getWordPos $line_check $word_check "refresh"
	if ($word_check = 1)
		gosub :figrefresh
	end
end	
goto :main_triggers

:next
killAllTriggers
setVar $line_check CURRENTLINE
cutText $line_check $ck 1 1
if ($ck = "R")
	cutText CURRENTLINE $ta_id 3 6
	cutText $line_check $id 3 6
	replaceText $line_check $id " "
	replaceText $line_check "R" " "
	stripText $line_check " "
	getWordPos $line_check $word_check "next"
	if ($word_check = 1)
		send "taq"
		setTextLineTrigger ta_sec :next_ta_sec $ta_id
		pause

		:next_ta_sec
		cutText CURRENTLINE $nnf_ta_sec 38 7
		stripText $nnf_ta_sec " "
		setVar $type "de"
		gosub :nnf_find
	end 
elseif ($ck = "'")
	stripText $line_check "'"
	getWordPos $line_check $word_check "next"
	if ($word_check = 1)
		send "*"
		waitFor "Sector  :"
		getWord CURRENTLINE $nnf_ta_sec 3
		setVar $type "de"
		gosub :nnf_find
	end
end
:got_de
setVar $closest_de $jumpfig
setVar $nnf_ta_sec $jumpfig
setVar $type "fig"
gosub :nnf_find
:got_de_fig
gosub :plot_to_de
:sent_plot
goto :main_triggers


:nnfsec
killAllTriggers
setVar $line_check CURRENTLINE
cutText $line_check $ck 1 1
if ($ck = "R") 
	cutText CURRENTLINE $ta_id 3 6
	cutText $line_check $id 3 6
	replaceText $line_check $id " "
	replaceText $line_check "R" " "
	getWordPos $line_check $word_check "nnf"
	if ($word_check = 1)
		getWord $line_check $nnf_ta_sec 2
		isNumber $number $nnf_ta_sec
			if ($number = 0) 
				send "'OZ-Explorer: invalid sector number*"
				goto :main_triggers
			end
		setVar $type "nnf"
		gosub :nnf_find
	end
elseif ($ck = "'")
	stripText $line_check "'"
	getWordPos $line_check $word_check "nnf"
	if ($word_check = 1)
		getWord $line_check $nnf_ta_sec 2
		isNumber $number $nnf_ta_sec
			if ($number = 0)
				send "'OZ-Explorer: invalid sector number*"
				goto :main_triggers
			end
		setVar $type "nnf"
		gosub :nnf_find
	end
end
:got_nnf
goto :main_triggers


:next_nnf
killAllTriggers
setVar $line_check CURRENTLINE
cutText $line_check $ck 1 1
if ($ck = "R") 
	cutText CURRENTLINE $ta_id 3 6
	cutText $line_check $id 3 6
	replaceText $line_check $id " "
	replaceText $line_check "R" " "
	stripText $line_check " "
	stripText $line_check "t"
	stripText $line_check "a"
	stripText $line_check "-"
	getWordPos $line_check $word_check "nnf"
	if ($word_check = 1)
		send "taq"
		setTextLineTrigger ta_sec :nnf_ta_sec $ta_id
		pause

		:nnf_ta_sec
		cutText CURRENTLINE $nnf_ta_sec 38 7
		stripText $nnf_ta_sec " "
		setVar $type "nnf"
		gosub :nnf_find
	end 
elseif ($ck = "'")
	stripText $line_check "'"
	stripText $line_check "t"
	stripText $line_check "a"
	stripText $line_check "-"
	stripText $line_check " "
	getWordPos $line_check $word_check "nnf"
	if ($word_check = 1)
		send "*"
		waitFor "Sector  :"
		getWord CURRENTLINE $nnf_ta_sec 3
		setVar $type "nnf"
		gosub :nnf_find
	end
end
:got_nnf
goto :main_triggers


:cleared
killAllTriggers
setVar $line_check CURRENTLINE
cutText $line_check $ck 1 1
if ($ck = "R") 
	cutText CURRENTLINE $ta_id 3 6
	cutText $line_check $id 3 6
	replaceText $line_check $id " "
	replaceText $line_check "R" " "
	stripText $line_check " "
	getWordPos $line_check $word_check "cleared"
	if ($word_check = 1)
		gosub :add_fig_to_cleared
	end 	
elseif ($ck = "'")
	stripText $line_check "'"
	getWordPos $line_check $word_check "cleared"
	if ($word_check = 1)
		gosub :add_fig_to_cleared
	end
end
goto :main_triggers


:fig_ta
killAllTriggers
setVar $line_check CURRENTLINE
cutText $line_check $ck 1 1
if ($ck = "R") 
	cutText CURRENTLINE $ta_id 3 6
	cutText $line_check $id 3 6
	replaceText $line_check $id " "
	replaceText $line_check "R" " "
	stripText $line_check " "
	getWordPos $line_check $word_check "fig-ta"
	if ($word_check = 1)
		gosub :add_fig_to_ta
	end 	
elseif ($ck = "'")
	stripText $line_check "'"
	getWordPos $line_check $word_check "fig-ta"
	if ($word_check = 1)
		gosub :add_fig_to_ta
	end
end
goto :main_triggers

:add_fig
killAllTriggers
setVar $line_check CURRENTLINE
cutText $line_check $ck 1 1
if ($ck = "R") 
	cutText $line_check $id 3 6
	replaceText $line_check $id " "
	replaceText $line_check "R" " "
	stripText $line_check " "
	getWordPos $line_check $word_check "fig"
	if ($word_check = 1)
		gosub :add_fig_to_sec
	end 	
elseif ($ck = "'")
	stripText $line_check "'"
	getWordPos $line_check $word_check "fig"
	if ($word_check = 1)
		gosub :add_fig_to_sec
	end	
end
goto :main_triggers


:help
killAllTriggers
setVar $line_check CURRENTLINE
cutText $line_check $ck 1 1
if ($ck = "R") 
	cutText $line_check $id 3 6
	replaceText $line_check $id " "
	replaceText $line_check "R" " "
	stripText $line_check " "
	getWordPos $line_check $word_check "help"
	if ($word_check = 1)
		gosub :command_list
	end 
elseif ($ck = "'")
	stripText $line_check "'"
	getWordPos $line_check $word_check "help"
	if ($word_check = 1)
		gosub :command_list
	end	
end
goto :main_triggers


# -=-=-=-=-=-=-=- add figs to course cleared -=-=-=-=-=--=-=-=-
:add_fig_to_cleared
setVar $course_count 0
stripText $the_course " >"
stripText $the_course "."
:sub_cleared
if ($course_count = $sec_count)
	goto :done_cleared
else
	add $course_count 1
	getWord $the_course $cleared[$course_count] $course_count
	setVar $figList[$cleared[$course_count]] 1
	if ($course_count = $sec_count)
		setVar $de[$cleared[$course_count]] 0
	end
	goto :sub_cleared
end

:done_cleared
goto :main_triggers

# -=-=-=-=-=-=-=-=- add fig to sec -=-=-=-=-=-=-=-=-=
:add_fig_to_sec
getWord $line_check $fig_sec 2
isNumber $number $fig_sec
	if ($number = 0)
		send "'OZ-Explorer: sector is not a number.*"
		goto :end_add_fig_to_sec
	end
if ($fig_sec > 0) and ($fig_sec <= SECTORS)
	setVar $figList[$fig_sec] 1
	if ($de[$fig_sec] = 1)
		setVar $de[$fig_sec] 0
	end
else
	send "'OZ-Explorer: sector is out of bounds.*"
end
:end_add_fig_to_sec
return

# -=-=-=-=-=-=-=- add fig to ta -=-=-=-=-=-=-=-=-=-=-
:add_fig_to_ta
if ($ck = "R")
	goto :corpy_ta
elseif ($ck = "'")
	goto :my_ta
end

:corpy_ta
send "taq"
setTextLineTrigger ta_sec :ta_sec $ta_id
pause

:ta_sec
cutText CURRENTLINE $ta_sec 38 7
stripText $ta_sec " "
setVar $figList[$ta_sec] 1
	if ($de[$ta_sec] = 1)
		setVar $de[$ta_sec] 0
	end
return

:my_ta
send "*"
waitFor "Sector  :"
getWord CURRENTLINE $ta_sec 3
setVar $figList[$ta_sec] 1
	if ($de[$ta_sec] = 1)
		setVar $de[$ta_sec] 0
	end
return

# -=-=-=-=-=-=-=-= help subroutine -=-=-=-=-=-=-=-=-=-
:command_list
waitfor "(?="
send "'*-=-=-=-=-=-=-=-=- :OZ-Explorer: Available commands =-=-=-=-=-=-=-=-*"
send "refresh              <- refreshes fig list*"
send "next                 <- sends location of closest list sector*"
send "cleared              <- adds figs to last listed course*"
send "nnf <sector>         <- sends location of nnf to designated sector*"
send "nnf-ta               <- sends location of nnf to your ta*"
send "fig-ta               <- logs a fig at your ta*"
send "fig <sector>         <- logs a fig at designated sector*"
send "help                 <- displays help screen*"
send "*"
waitFor "Sub-space comm-link terminated"
return

# -=-=-=-=-=-=- fig refresh routine -=-=-=--=-=-=-=
:figrefresh
setVar $secsfigd 0
setArray $figList SECTORS
send "'OZ-Explorer: Fig refresh in progress...*"
send "G"
waitFor "==========================================================="
:get_figs
getWord CURRENTLINE $fs 1
getWord CURRENTLINE $st 2
isNumber $sn $fs
if ($st = "Total") or ($st = "fighters")
	setVar $perc (($secsfigd * 100)/SECTORS)
	send "'OZ-Explorer: Fig refresh complete*"
	send "'OZ-Explorer: Fig count: " $secsfigd " sector(s) : " $perc "%*"
	goto :end_refresh
end
if ($sn = 1)
	add $secsfigd 1
	setVar $figList[$fs] 1
end
setTextLineTrigger grab :get_figs
pause

:end_refresh
setVar $a 0
setArray $de SECTORS
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


# -=-=-=-=-=-=-=-=-=- nnf check -=-=-=-=-=-=-=-=-=-=-=-=-=-
# find nnf -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
:nnf_find
setVar $nnfcheck $nnf_ta_sec
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
if ($type = "nnf") and ($figList[$nnfcheck] = 0)
	setVar $jumpfig $nnfcheck
	goto :found_nnf
elseif ($type = "de") and ($de[$nnfcheck] = 1)
	setVar $jumpfig $nnfcheck
	goto :found_nnf
elseif ($type = "fig") and ($figList[$nnfcheck] = 1)
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
	if ($type = "nnf") and ($figList[$thesec] = 0)
		add $hops 1
		setVar $jumpfig $thesec
		goto :found_nnf
	elseif ($type = "de") and ($de[$thesec] = 1)
		add $hops 1
		setVar $jumpfig $thesec
		goto :found_nnf
	elseif ($type = "fig") and ($figList[$thesec] = 1)
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
if ($type = "nnf")
	send "'OZ-Explorer: Nearest non-figged sector to " $nnfcheck " is " $jumpfig ", " $hoppy " hop(s) away.*"
	goto :got_nnf
elseif ($type = "de")
	goto :got_de
elseif ($type = "fig")
	goto :got_de_fig
end


# -=-=-=-=-=-=-=-=- plot from jumpfig to de -=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
:plot_to_de
setVar $sec_count 1
getCourse $lane $jumpfig $closest_de
setVar $the_course $jumpfig&" . "
:sub_plot
if ($sec_count > $lane)
	goto :done_plot
else
	add $sec_count 1
	replaceText $the_course " . " " > "&$lane[$sec_count]&" . "
	goto :sub_plot
end

:done_plot
send "'OZ-Explorer: The closest non-figged list sector is " $closest_de ".*"
send "'OZ-Explorer: The closest fig to that list sector is in " $jumpfig ".*"
send "'OZ-Explorer: The course is - " $the_course "*"
goto :sent_plot


