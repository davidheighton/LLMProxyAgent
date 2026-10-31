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
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "Smack_Capture Script Version 0.04" ANSI_11 " |===" ANSI_3 "--**"
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
send "l" $planet "*t*t1*m***q"

                    
:waitforsmack
setTextLineTrigger 1 :smack_main "Deployed Fighters"
pause

:smack_main
setVar $pass 1
setVar $line CURRENTLINE
stripText $line ":"
getWord $line $smacksector 5

:protected_test
#test if protected sector
read protect.txt $protected $pass
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
send "m" $protector "*yycpy" $protected "*qm" $protected "*ay20000*h150*c"

setTextLineTrigger 2 :End "For defeating"
setTextLineTrigger 3 :smack_capture "You destroyed"
pause

:start_smack_capture
setVar $pass 3

:smack_capture
add $pass 1
setVar $line CURRENTLINE
stripText $line ","
getWord $line $fightersleft 6
if ($fightersleft < 4) then
	send "ay1*"
	setTextLineTrigger $pass :smack_capture "You destroyed"
	add $pass 1
	setTextLineTrigger $pass :End "For defeating"
	pause
elseif ($fightersleft < 30000) then
	setVar $attackfigs $fightersleft
	divide $attackfigs 3
	send "ay" $attackfigs "*"
	setTextLineTrigger $pass :smack_capture "You destroyed"
	add $pass 1
	setTextLineTrigger $pass :End "For defeating"
	pause
else
	send "m" $sector "*yy"
	send "l" $planet "*t*t1*m***q"
	send "m" $protector "*yym" $protected "*ay20000*"
	setTextLineTrigger $pass :smack_capture "You destroyed"
	add $pass 1
	setTextLineTrigger $pass :End "For defeating"
	pause
end

:End
sound photon.wav