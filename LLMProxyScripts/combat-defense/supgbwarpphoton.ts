# TWX SupG Script  : SupGBWarpPhoton
# Author           : SupG
# Description      : Simple script that uses the planetary transporter
#                    to warp you to a sector being hit by an enemy.
# Trigger Point    : Citadel prompt

# Other            : Let me know of any problems with this script
#www.twtavern.com
echo ANSI_6 "**-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 "Script Name" ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-*"
echo ANSI_6 "-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 "  www.twtavern.com  " ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-**"

getinput $pnum "planet number"
:choice
Echo ansi_12 "*H" ansi_15 "ome or " ansi_12 "S" ansi_15 "tardock after photon"
getConsoleinput $choice singlekey

lowercase $choice
if ($choice <> "h") and ($choice <> "s")
	goto :choice
end

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
	send "b" $secinf.warp[$i] "*"
	setTextTrigger go :go "All Systems Ready, shall we engage?"
	setTextTrigger no :no "Do you want to make this transport blind?"
	pause
else
	add $i 1
	goto :act
end
:no
killtrigger go
send "n"
waitfor "Transporter shutting down."
setVar $figgies[$secinf.warp[$i]] 0
add $i 1
goto :act

:go
#send "*m" $activated_sector "*"
#halt
send "ycpy" $activated_sector "*q"
if ($choice = "s")
	send "nsyyps"
else
	send "m" $home "*yy"
end

send "'Photoned " $activated_sector " from " $secinf.warp[$i] ".*"
sound message.wav
halt

