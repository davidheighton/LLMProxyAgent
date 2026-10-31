# TWX Script       : E-Probe Deadends or Bubbles -- Modified to do Unexplored Sectors -- from HOMESECTOR! --
# Author           : Sam - chemicalppl@mindless.com
# Last Modified    : 5/17/01 - 1:13am
# Description      : After exporting the deadends.txt or bubbles.txt file, 
#		   : you can use this script to send an eprobe to every deadend
#		   : or bubble. Automatically refills probes, and records unreachable
#		   : sectors in a text file named probe.txt.	
# Trigger Point    : Command Prompt
# Warnings         : Make sure you are at SD with 1 or more probes and cloaks.  Also
#		   : turn off animation by pressing "C" followed by "N".
#		   : Also watch your credits, eprobes cost $3000 a piece and you can
#		   : buy 25 at one time.

setVar $cashplanet 1
setVar $stardock 1
cutText CURRENTLINE $location 1 7
if ($location <> "Command")
        clientMessage "This script must be run from the game command menu"
        halt
end

logging off

write probe.txt "These Are The Sectors The probe could not Reach From StarDock"

:GetList
echo "*" ANSI_15 "Select Sectors List:" "**"
echo ANSI_11 "1 " ANSI_3 " - Deadends*"
echo ANSI_11 "2 " ANSI_3 " - Bubbles*"
echo ANSI_11 "3 " ANSI_3 " - Unexplored (unexp.txt)* 
getConsoleInput $sl SINGLEKEY

if ($sl <> 1) and ($sl <> 2) and ($sl <> 3)
	goto :GetList
end

:GetLocation
echo "**" ANSI_15 "Are you in (H)omesector or (S)tardock? "
getConsoleInput $location SINGLEKEY

if ($location <> "H") and ($location <> "S")
	goto :GetLocation
end

:GetStardock
if ($location = "H")
getInput $homesector "Homesector with Cashplanet is located in?" 0
getInput $stardock "Stardock is Located In what Sector?" 0
getInput $cashplanet "Credits are located on what planet in This Sector?" 0
end

if ($stardock = 0) or ($cashplanet = 0) or ($homesector = 0)
	goto :GetStardock
end


send "i"
setTextLineTrigger 2 :getEprobes2 "Ether Probes :"
setTextLineTrigger 1 :getEprobes "Cloaking Device:"

:getEprobes2
getWord CURRENTLINE $probes 4
goto :set

:getEprobes
getWord CURRENTLINE $probes 7


killAllTriggers
:set
setVar $i 1
setVar $col 1

:eprobe
	setTextTrigger 3 :writeFile "Probe Destroyed!"
	if ($sl = 1)
		read deadends.txt $s $i
	end
	if ($sl = 2)
		read bubbles.txt $temp $i
		getWord $temp $s 1
	end
	if ($sl = 3)
		read unexp.txt $line $i
		getWord $line $s $col
	end
	if ($s = "0")
		Echo "** End of File"
		halt
	end
	send "e"
	waitFor "Please enter a destination for this probe :"
	send $s "*"
	subtract $probes 1
	if ($sl = 1) or ($sl = 2)
		add $i 1
	end
	if ($sl = 3) and ($col < 11)
		add $col 1
	elseif ($sl = 3) and ($col = 11)
		setVar $col 1
		add $i 1
	end
	waitFor "Command [TL="
	killAllTriggers
	if ($probes = 0)
		goto :buy
	else
		goto :eprobe
	end

:buy
if ($location = "S") 
	send "pshe"
	waitFor "How many Probes"
	send "25*qq"
	waitFor "Command [TL="
	add $probes 25
	goto :eprobe
elseif ($location = "H")
	send "l" $cashplanet "*t*t1*ctf75000*qq"
	waitFor "Command [TL="
	send "m" $stardock "*yy"
	waitFor "Command [TL="
	send "pshe"
	waitFor "How many Probes"
	send "25*qq"
	waitFor "Command [TL="
	send "m" $homesector "*yy"
	waitFor "Command [TL="
	add $probes 25
	goto :eprobe
end

:writeFile
	killAllTriggers
	write probe.txt $s
	if ($probes = 0)
		goto :buy
	else
		goto :eprobe
	end