# Exiled TWX Script: Exiled Smack Script Booby Trap Script
# Author           : Pinky
# Description      : Photon Attacks Protected Sectors (must be modified to work with your sectors)
# Warnings         :
# Other            : 

# check if we can run it from here
cutText CURRENTLINE $location 1 14

if ($location <> "Planet command")
        clientMessage "This script must be run from the surface of a planet"
        halt
end

logging off

# show script banner
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "Smack_Idle Script Version 0.03" ANSI_11 " |===" ANSI_3 "--**"
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
setTextLineTrigger 2 :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $sector 3
send "l" $planet "*t*t1*q"
                   
setTextLineTrigger 3 :smack_main "Deployed Fighters"
setVar $fighterpass 4

:waitforsmack
setTextLineTrigger $fighterpass :smack_main "Deployed Fighters"
pause

:smack_main
setVar $pass 1
add $fighterpass 1
setVar $line CURRENTLINE
stripText $line ":"
getWord $line $smacksector 5

:protected_test
#test if protected sector
read protecth.txt $protected $pass
add $pass 1
read protect.txt $protector $pass
if ($protected = 0)
	clientMessage "Not Attacking!"
        goto :waitforsmack
end
if ($protected = $smacksector) then
	goto :smack_sector
else
	add $pass 1
	goto :protected_test
end
goto :End

:smack_sector
send "m" $protector "*yycpy" $protected "*q"
send "sh"
send "m" $sector "*yy"
send "l" $planet "*t*t1*q"
sound photon.wav
goto :waitforsmack

:End