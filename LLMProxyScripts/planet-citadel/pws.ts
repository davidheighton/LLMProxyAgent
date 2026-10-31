# Exiled TWX Script: Planet Product Selling Script
# Author           : Pinky
# Description      : Places Figs in defined sectors twarps planet to sectors and sells everything it can sell
# Warnings         : If you run into enemy figs the script stops and must be started over (working on a fix)
# Other            : 

# check if we can run it from here
cutText CURRENTLINE $location 1 14

if ($location <> "Planet command")
        clientMessage "This script must be run from the surface of a planet"
        halt
end

logging off

# show script banner
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "PWS Script Version 0.03" ANSI_11 " |===" ANSI_3 "--**"
echo ANSI_7 "If you have it keep it to yourself. -;)."

# Set trigger to stop script if disconnected
setEventTrigger 0 :End "Connection lost"

# get the planet number for the planet we're on
send "d"
setTextLineTrigger 1 :getPlanet "Planet #"
pause
:getPlanet
getWord CURRENTLINE $planet 2
stripText $planet "#"

# get the sector we're in
send "qd"
setTextLineTrigger 1 :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $sector 3

setVar $origsector $sector
setVar $pass 1
setVar $probes 5
setVar $stardock 3174

:main_pws
# read sector info
read sectors.txt $goto_sector $pass
if ($goto_sector = 0)
	# warp home and end script.
	waitFor "Command [TL="
	send "l" $planet "*cp" $origsector "*yqq*"
        clientMessage "Script Compleate"
        halt
end
# fill up on fuel ore
send "lt*t1*q"
# check to make sure path is clear.
setTextLineTrigger ProbeDestroyed :probedestroyed "Probe Destroyed!"
setTextLineTrigger ProbeOk :move "Probe Self Destructs"
send "e" $goto_sector "*"
subtract $probes 1
pause

:move
KillTrigger ProbeOk
KillTrigger ProbeDestroyed
# express to sector and drop figs.

setTextLineTrigger PlaceFigs :placefigs "Arriving sector"
send "m" $goto_sector "*ne"
Pause

:placefigs
killtrigger PlaceFigs
send "f1*ct"
# twarp back to planet sector and pick up figs unless origin
if ($sector = $origsector) then
	send "m" $sector "*ne"
else
	send "m" $sector "*nef1*ct"
end
# land on planet warp to sector sell product
waitFor "Command [TL="
send "lcp" $goto_sector "*yqqpn" $planet "*"
send "******"
# reasign vars
setVar $sector $goto_sector
Add $pass 1

if ($probes = 1) then
	goto :refill_probes
end

goto :main_pws

:refill_probes
setTextLineTrigger ProbeDestroyed :probedestroyed "Probe Destroyed!"
setTextLineTrigger ProbeOk :refill_probesmove "Probe Self Destructs"
send "e" $stardock "*"
subtract $probes 1
pause

:refill_probesmove
# get cash amount
send "i"
setTextLineTrigger GetCashAmount :getcashamount "Credits"
pause
:getcashamount
getWord CURRENTLINE $cashamount 3
stripText $cashamount ","

killTrigger GetCashAmount
KillTrigger ProbeOk
KillTrigger ProbeDestroyed
# express to sector and drop figs.
send "l" $planet "*ctt" $cashamount "*tf30000*qq"
setTextLineTrigger GetProbes :getprobes "Arriving sector"
send "m" $stardock "*ne"
pause
:getprobes
send "psyhe5*qq"
setVar $probes 5
send "m" $sector "*ne"
goto :main_pws


:ProbeDestroyed
Echo "**Path Is Not Clear Ending"
Halt

:End