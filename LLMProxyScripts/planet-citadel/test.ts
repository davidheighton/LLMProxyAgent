# Exiled TWX Script: Homesector Fedspace Protection Fighter Mine Placement Script! -;)
# Author           : Pinky
# Description      : 
# Warnings         : Must Have Cash Onhand
# Other            :

# check if we can run it from here
cutText CURRENTLINE $location 1 14

if ($location <> "Planet command")
        clientMessage "This script must be run from the surface of a planet"
        halt
end

logging off

# show script banner
echo "**" ANSI_4 "     --" ANSI_12 "===| " ANSI_15 "Protected Fighter Mine Placement Script Version 0.04" ANSI_12 " |===" ANSI_4 "--**"
echo ANSI_7 "If you have it keep it to yourself. -;).**"

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

# load Settings
read sectorsf.txt $stardock 1
setVar $pass 2

:fighterplacement
read sectorsf.txt $protectedsector $pass
if ($protectedsector = "EOF") then
	Echo "** Done placing Fighters and Mines! **"
	halt
end
send "l" $planet "*t*t1*q"
send "m" $stardock "*yy"
send "pshm100*qq"
send "cunq"
send "m" $protectedsector "*e*"
send "h100*cf10*ct"
send "cuyq"
send "m" $sector "*yy"
goto :fighterplacement


:End