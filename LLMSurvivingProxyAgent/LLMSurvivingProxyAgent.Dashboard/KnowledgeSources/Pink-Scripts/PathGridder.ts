cutText CURRENTLINE $location 1 12
if ($location <> "Command [TL=")
	echo "**" ansi_12 "This script must be run from the command prompt.*"
	halt
end

#check turns per day
send "v"
waitFor "Initial Turns per day"
getword CURRENTLINE $totalturns 5
striptext $totalturns ","
saveVar $totalturns

#check ship status
send "c;q"
waitFor "Max Figs Per Attack:"
getWord CURRENTLINE $maxfigs 5
send "i"
setTextLineTrigger 1 :getScanner "LongRange Scan :"
waitFor "Credits      "
clientMessage "No long range scanner detected!"
halt
:getScanner
getWord CURRENTLINE $scanType 4

killAllTriggers

setVar $bd_version "bd Path Gridder 1.0"
setVar $min_figs 500
setVar $min_turns 300
setVar $max_density 499
setVar $target 10
setVar $botname "N/A"
setVar $menu_data 1
setVar $datatype "File"
setVar $grid_listfile "griddata.txt"
setVar $mode "Calculating"
setVar $start "Yes"

addMenu "" "BD Path Gridder" "BD Path Gridder Settings" "." "" "Main" FALSE
addMenu "BD Path Gridder" "Start Gridding" "Start Gridding." "S" :Menu_Grid "" FALSE
addMenu "BD Path Gridder" "Min Figs" "Min Figs" "1" :Menu_Figs "" FALSE
addMenu "BD Path Gridder" "Min Turns" "Min Turns" "2" :Menu_Turns "" FALSE
addMenu "BD Path Gridder" "Max Density" "Max Density" "3" :Menu_Density "" FALSE
addMenu "BD Path Gridder" "Start Sector" "Start Sector" "4" :Menu_Sector "" FALSE
addMenu "BD Path Gridder" "BOT Name" "BOT Name" "5" :Menu_Botname "" FALSE
addMenu "BD Path Gridder" "Data Type" "Data Type" "6" :Menu_Datatype "" FALSE
addMenu "BD Path Gridder" "Data File" "Data File" "7" :Menu_Datafile "" FALSE

setMenuHelp "Start Gridding" "This will activate the path grid script."
setMenuHelp "Min Figs" "This sets the minimum figs to stop the script."
setMenuHelp "Min Turns" "This sets the minimum turns to stop the script."
setMenuHelp "Max Density" "This sets the maximum density to avoid."
setMenuHelp "Start Sector" "This sets the lowest sector number to grid."
setMenuHelp "BOT Name" "This retrieves the name of the current bot loaded (Must be exact - Case sensitive)."
setMenuHelp "Data Type" "This determines sector data source."
setMenuHelp "Data File" "Name file that contains a list of sectors to grid."

:start_menu
	gosub :sub_setMenu
	openMenu "BD Path Gridder"
	pause

:Menu_Figs
	getInput $min_figs "Enter the minimum fighter level to stop at"
	echo ANSI_12 "*" $min_figs "*"
	isNumber $number $min_figs
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Figs
	end
	saveVar $min_figs
	gosub :sub_setMenu
	openMenu "BD Path Gridder"

:Menu_Turns
	getInput $min_turns "Enter the minimum turn level to stop at"
	echo ANSI_12 "*" $min_turns "*"
	isNumber $number $min_turns
		if ($number <> 1)
			echo ANSI_12 "*Invalid Number*"
			goto :Menu_Turns
		end
	saveVar $min_turns
	gosub :sub_setMenu
	openMenu "BD Path Gridder"

:Menu_Density
	getInput $max_density "Enter the maximum density level to avoid"
	echo ANSI_12 "*" $max_density "*"
	isNumber $number $max_density
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Density
	end
	saveVar $max_density
	gosub :sub_setMenu
	openMenu "BD Path Gridder"

:Menu_Sector
	getInput $target "Enter lowest sector number to grid"
	echo ANSI_12 "*" $target "*"
	isNumber $number $target
	if ($number <> 1)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Sector
	end
	if ($target > SECTORS)
		echo ANSI_12 "*Sector number out of range*"
		goto :Menu_Sector
	end
	saveVar $target
	gosub :sub_setMenu
	openMenu "BD Path Gridder"

:Menu_Botname
	getInput $botname "Enter M()M BOT botname (Blank for none)"
	if ($botname = "")
		setVar $botname "N/A"
	end
	echo ANSI_12 "*" $botname "*"
	saveVar $botname
	gosub :sub_setMenu
	openMenu "BD Path Gridder"

:Menu_Datatype
	add $menudata 1
	if ($menudata = 1)
		setVar $datatype "Unexplored"
		setVar $grid_listfile "N/A"
	else
		setVar $menudata "1"
		goto :Menu_Datafile
	end
	echo ANSI_12 "*" $datatype "*"
	saveVar $grid_listfile		
	saveVar $menudata
	saveVar $datatype
	gosub :sub_setMenu
	openMenu "BD Path Gridder"

:Menu_Datafile	
	setVar $datatype "File"
	getInput $grid_listfile "Enter name of sector data file. (Blank for none)"
	saveVar $grid_listfile
	if ($grid_listfile = "")
		setVar $grid_listfile "N/A"
		setVar $menudata 2
		setVar $datatype "Unexplored"	
	end
	saveVar $grid_listfile
	saveVar $menudata
	saveVar $datatype
	gosub :sub_setMenu
	openMenu "BD Path Gridder"

:sub_setMenu
	setMenuValue "Min Figs" $min_figs
	setMenuValue "Min Turns" $min_turns
	setMenuValue "Max Density" $max_density
        setMenuValue "Start Sector" $target
	setMenuValue "BOT Name" $botname
	setMenuValue "Data Type" $datatype
	setMenuValue "Data File" $grid_listfile
	
return

:Menu_Grid
	CloseMenu
	window status 210 140 "Status Report" ONTOP
	send "/"
	settexttrigger 1 :quickstats "Sect "
	:keepgoing
	setVar $rec 0		
	send "'BD Path Gridder powering up...*"
	waitFor "Message sent"	
	killalltriggers
	goto :Start_Gridding

:Start_Gridding
	if ($datatype = "Unexplored")
		add $target 1		
		saveVar $target
		if ($target > SECTORS)
			echo ANSI_12 "*All sectors have been explored*"
			halt
		end	
		if (SECTOR.EXPLORED[$target] = YES)
			goto :Start_Gridding				
		end		
	end
	if ($datatype = "File")
		add $rec 1
		read $grid_listfile $Sector ($rec)
		if (($Sector = "") OR ($Sector > SECTORS))
			echo ANSI_12 "*Invalid sector number. Data file not found or corrupted!*"
			halt
		end
		if ($Sector = "EOF")
  			echo ANSI_12 "*End Of File! Out of sector data...*"
			halt
		end		
		setVar $target $Sector		
	end
	gosub :getCourse
	killalltriggers
	setVar $j 2
	setVar $result ""
	while ($j <= $courseLength)
		send "/"
		settexttrigger 1 :quickstats "Sect "
		pause
		:resume
			setVar $mode "Gridding"
			if $botname <> "N/A"
				send zocsend "'"&$botname&" surround*"
				waitFor "- Surrounded sector"
			end
			setVar $result $COURSE[$j]
			send "sd"
			waitFor "Relative Density Scan"			
			setVar $density sector.density[$result]
			if ($density > $max_density)
				setAvoid $target
				send "'A density of "&$density&" found in sector "&$result&".  Skipping sector...*"   
				goto :Start_Gridding
			else
				send "c v 0 * y n "&$result&" * q "
			end
			send " "&$result&" * "
			waitFor "<Move>" 
			if (($result > 10) AND ($result <> STARDOCK))
				send " a z "&$maxfigs&" * z n * f 1 * c d*"
			else
				send "d"
			end
			
			waitFor "Sector  : " & $result & " in "
			add $j 1
	end
	setVar $mode "Calculating"
goto :Start_Gridding

:getCourse
	killalltriggers
	setVar $v 1
	while $v <= $voids
		setVar $voidcheck $voids[$v]
		if ($target = $voidcheck)
			send "'Avoided sector found!  Skipping sector "&$target&"...*"   
			goto :Start_Gridding
		end
		add $v 1
	end
	setVar $sectors ""
	setTextLineTrigger sectorlinetrig :sectorsline " > "
	send "^f*"&$target&"*q"
	pause

:sectorsline
	killAllTriggers
	setVar $line CURRENTLINE
	replacetext $line ">" " "
	striptext $line "("
	striptext $line ")"
	setVar $line $line&" "
	getWordPos $line $pos "So what's the point?"
	getWordPos $line $pos2 ": ENDINTERROG"
	if (($pos > 0) OR ($pos2 > 0))
		goto :noPath
	end
	getWordPos $line $pos " sector "
	getWordPos $line $pos2 "TO"
	if (($pos <= 0) AND ($pos2 <= 0))
		setVar $sectors $sectors & " " & $line
	end
	getWordPos $line $pos " "&$target&" "
	getWordPos $line $pos2 "("&$target&")"
	getWordPos $line $pos3 "TO"
	if ((($pos > 0) OR ($pos2 > 0)) AND ($pos3 <= 0))
		goto :gotSectors
	else
		setTextLineTrigger sectorlinetrig :sectorsline " > "
		setTextLineTrigger sectorlinetrig2 :sectorsline " "&$target&" "
		setTextLineTrigger sectorlinetrig3 :sectorsline " "&$target
		setTextLineTrigger sectorlinetrig4 :sectorsline "("&$target&")"
		setTextLineTrigger donePath :sectorsline "So what's the point?"
		setTextLineTrigger donePath2 :sectorsline ": ENDINTERROG"
	end
	pause

:gotSectors
	killAllTriggers
	setVar $sectors $sectors&" :::"
	setVar $courseLength 0
	setVar $index 1
	getWord $sectors $COURSE[$index] $index
	while ($COURSE[$index] <> ":::")
		add $courseLength 1
		add $index 1
		getWord $sectors $COURSE[$index] $index
	end
	return

:noPath
	killAllTriggers
	add $target 1
	saveVar $target
	goto :Start_Gridding

:quickstats
	killtrigger 1
	getword currentline $turns 3
	striptext $turns "Creds"
	striptext $turns ","
	replaceText $turns #179 " "
	striptext $turns " "
	getword currentline $figs 5
	striptext $figs "Shlds"
	striptext $figs ","
	replaceText $figs #179 " "
	striptext $figs " "
	if ($totalturns = "Unlimited")
		setVar $turns 60000
		saveVar $turns
	end
	if (($turns = 0) OR ($figs < $lastfigs))
		if ($botname <> "N/A")  
			goto :callSaveMe
		end
		send "'SOS!  Help my T/A!!!*"
		halt
	end		
	if ($turns < $min_turns)
		goto :low_turns
	end
	if ($figs < $min_figs)
		goto :low_figs
	end
	waitFor "Command ["
	mergetext "Charge Sector = " $target $status
	mergetext $status "*Current Sector = " $status
	mergetext $status $result $status
	mergetext $status "*Fighters = " $status
	mergetext $status $figs $status
	mergetext $status "*Turns Left = " $status
	if ($totalturns = "Unlimited")
		mergetext $status $totalturns $status
		
	else
		mergetext $status $turns $status
	end	
	mergetext $status "*Data Source = " $status
	mergetext $status $datatype $status
	mergetext $status "*Grid Mode = " $status
	mergetext $status $mode $status
	mergetext $status "*" $status
	setwindowcontents status $status
	setVar $lastfigs $figs
	divide $lastfigs 2
	saveVar $lastfigs
	if ($start = "Yes")
		setVar $start "No"
		goto :keepgoing
	else
		goto :resume
	end
:low_turns
	send "'Min Turn Level Reached*"
	halt

:low_figs
	send "'Min Fig Level Reached*"
	halt

:callSaveMe
	send "q q q q * '"&$botname&" call*"
	halt
