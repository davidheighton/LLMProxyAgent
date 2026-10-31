# OZ Relaoder 2.0
# :D

setVar $OZ_Reloader_Version "OZ-Reloader 2.0"

# check if we can run it from here
cutText CURRENTLINE $location 1 6


if ($location <> "Planet")
        clientMessage "This script must be run from the Planet Prompt"
        halt
end

loadVar $ozReloaderSaved

if ($ozReloaderSaved)
	loadVar $threshold
else
	setVar $threshold 5000
	
	saveVar $threshold
		
	setVar $ozReloaderSaved 1
	saveVar $ozReloaderSaved
end

addMenu "" "OZ-Reloader" "OZ-Reloader Settings" "." "" "Main" FALSE
addMenu "OZ-Reloader" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-Reloader" "Set Threshold" "Set Threshold" "S" :Menu_Threshold "" FALSE


setMenuHelp "Run" "This Option Activates the Script."
setMenuHelp "Set Threshold" "This Option Sets the Minimum number of figs you have to loose before you reload."

gosub :sub_setMenu
gosub :OZ_ReloaderHeader
openMenu "OZ-Reloader"

:OZ_ReloaderHeader
ECHO ANSI_2 "*----------------------------------*"
ECHO ANSI_10 "           " $OZ_Reloader_Version " "
ECHO ANSI_2 "*----------------------------------*"
return

:Menu_Threshold
getInput $threshold "Enter the Threshold level."

isNumber $number $threshold
	if ($number = 0)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_Threshold
	end
saveVar $threshold
gosub :sub_SetMenu
gosub :OZ_ReloaderHeader
openMenu "OZ-Reloader"

:sub_setMenu
	setMenuValue "Set Threshold" $threshold
return

:Menu_Run
:Reloader
send "'Activating OZ-Reloader 2.0*"
waitFor "(?="
:getplanetnum
send "D"
setTextLineTrigger pnum :pnum "Planet #"
setTextLinetrigger figs :figs "Fighters"
pause

:pnum
getWord CURRENTLINE $planet 2
stripText $planet "#"
getWord CURRENTLINE $basesec 5
stripText $basesec ":"
pause

:figs
setVar $loss 0
getWord CURRENTLINE $pfigs 5
stripText $pfigs ","
getWord CURRENTLINE $sfigs 6
stripText $sfigs ","
send "q"
send "'OZ-Reloader 2.0 - using Planet # " $planet " *"
send "'OZ-Reloader 2.0 - Planet # " $planet " has " $pfigs " figs availible*"
send "'OZ-Reloader 2.0 - Reload Ship has " $sfigs " figs aboard*"
goto :wait

:wait
setTextLineTrigger subload :subload "reload"
setTextLineTrigger pmis :pmis "damaging your ship."
setTextLineTrigger mines :mines "of your mines in "&$basesec
waitFor "Shipboard Computers"
getWord CURRENTLINE $ck 1
getWord CURRENTLINE $ck2 4
getWord CURRENTLINE $ck3 5
getWord CURRENTLINE $ck4 6
getWord CURRENTLINE $ck5 7
if ($ck <> "Shipboard")
	killAllTriggers
	goto :wait
elseif ($ck2 = "destroyed") or ($ck3 = "destroyed") or ($ck4 = "destroyed") or ($ck5 = "destroyed")
	goto :get_loss
end
:get_loss
getText CURRENTLINE $fig_loss "destroyed" "fighters."
gosub :stripit
add $loss $fig_loss
if ($loss >= $threshold)
	goto :reload
else
	killAllTriggers
	goto :wait
end
:reload
send "l" $planet "*  mnt*  q"
killAllTriggers
setVar $loss 0
goto :wait

:subload
getWord CURRENTLINE $subck 1
if ($subck = "R")
	goto :reload
else
	killAllTriggers
	goto :wait
end

:pmis
getWord CURRENTLINE $mis_shooter 1
send "'I have just been photoned by " $mis_shooter " - Invasion Warning!*"
killAllTriggers
goto :wait

:mines
getWord CURRENTLINE $disruptor 1
send "'" $disruptor " just disrupted mines at our base - Invasion Warning!*"
killAllTriggers
goto :wait

:stripit
stripText $fig_loss "s"
stripText $fig_loss "h"
stripText $fig_loss "i"
stripText $fig_loss "e"
stripText $fig_loss "l"
stripText $fig_loss "d"
stripText $fig_loss "p"
stripText $fig_loss "o"
stripText $fig_loss "i"
stripText $fig_loss "n"
stripText $fig_loss "t"
stripText $fig_loss "a"
stripText $fig_loss "n"
stripText $fig_loss " "
return
