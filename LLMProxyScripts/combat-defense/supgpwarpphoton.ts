# TWX SupG Script  : SupGPwarpPhoton.ts
# Author           : SupG
# Description      : Uses the Level 4+ planet to drop in on an adjacent fighter
#                    being struck and photons said sector.
# Trigger Point    : Command prompt

# Other            : Let me know of any problems with this script
#www.twtavern.com

echo ANSI_6 "**-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 "  SupGPwarpPhoton   " ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-*"
echo ANSI_6 "-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 "  www.twtavern.com  " ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-**"

getinput $pnum "Planet Number"0

send "I"
setTextLineTrigger sec :sec "Current Sector :"
pause

:sec
getWord CURRENTLINE $home 4

waitfor "Command [TL="
send "G"
:update
setVar $fig_sec 0
waitfor "==============="
:get_figs
getWord CURRENTLINE $sector 1
getWord CURRENTLINE $stopper 2
isNumber $secnum $sector
if ($secnum = "TRUE")
	setVar $figgies[$sector] 1
end
if ($stopper = "Total")
	goto :wait
end
setTextLineTrigger grab :get_figs
pause
:wait
send "l" $pnum "*c"
:hehe
setTextLineTrigger hitfig :get_distance "Deployed Fighters Report Sector"
pause
:get_distance
getWord CURRENTLINE $activated_sector 5
stripText $activated_sector ":"
getSector $activated_sector $secinf
setVar $i 1
:act
if ($secinf.warp[$i] = 0)
	goto :hehe
end
if ($figgies[$secinf.warp[$i]] = 1)
	send "p" $secinf.warp[$i] "*ycpy" $activated_sector "*q"
	setTextTrigger go :go "Photon Missile launched into sector"
	setTextTrigger no :no "That is not an adjacent sector"
	pause
else
	add $i 1
	goto :act
end
:no
killtrigger go
send "qq"
waitfor "Citadel treasury contains"
setVar $figgies[$secinf.warp[$i]] 0
goto :wait

:go
send "p" $home "*y"

send "'SUPGPWARPHOTON: Photoned " $activated_sector " from " $secinf.warp[$i] ".*"
sound message.wav
halt

