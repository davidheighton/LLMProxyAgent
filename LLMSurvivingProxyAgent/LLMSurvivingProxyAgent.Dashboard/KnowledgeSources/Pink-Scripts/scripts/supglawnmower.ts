# Script Name      : SupGLawnMower.ts
# Author           : SupG
# Description      : Warps you from one spot to another killing figs and laying figs as it goes
# Trigger Point    : Sector command prompt
# Warnings         : This script may error out if you don't have a good mapping of the universe
#		     Will not work when warping using navpoints, you must manually enter the sector number
# Other            : You are free to modify this script to your liking, but please give me
#                    credit for any of my code that you use.
# Scripter's Tavern : www.twtavern.com
:top
send "d"
:wait
setTextLineTrigger sector :sector "Sector  :"
setTextLineTrigger getdest :dest "Warping to Sector"
setTextLineTrigger nav :nav "<Set Course to NavPoint>"
pause

:sector
killtrigger getdest
killtrigger nav
getWord CURRENTLINE $current 3
goto :wait

:dest
killtrigger sector
killtrigger nav
getWord CURRENTLINE $destination 4
getDistance $distance $current $destination
if ($distance = 1)
	goto :singlehop
end

setTextTrigger go :go "Engage the Autopilot?"
setTextLineTrigger nogo :nogo "Do you want to engage the TransWarp drive? Yes"
pause

:nogo
killtrigger go
killtrigger nav
goto :wait

:go
killtrigger nogo
killtrigger nav
send "N"
:input
waitfor "Command [TL="
echo ANSI_15 "*Warp to " $destination "?" 
getConsoleInput $heh singlekey
upperCase $heh
if ($heh = "Y")
	goto :warpit
else
	send "d"
	goto :wait
end

:warpit
getCourse $lane $current $destination
setVar $spanky 1
:spanky
if ($spanky < $lane)
	add $spanky 1
	#macro by Xide
	send "m" $lane[$spanky] "*atny9999**f1*ctsd"
	goto :spanky
end
send "m" $destination "*atny9999**f1*ctsd"
goto :top

:singlehop
send "atny9999**f1*ctsd"
goto :top

:nav
killalltriggers
waitfor "Command [TL="
goto :wait