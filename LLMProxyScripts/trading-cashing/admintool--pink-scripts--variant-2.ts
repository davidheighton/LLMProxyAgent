# Exiled TWX Script: Exiled Admin Script / Borg
# Author           : Pinky
# Description      : 
# Warnings         :
# Other            : 


# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Trade")
        clientMessage "This script must be run from the Editor Prompt."
        halt
end                    

                    
# show script banner
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "Exiled Admin Script" ANSI_11 " |===" ANSI_3 "--**"
echo ANSI_7 "If you have it keep it to yourself. -;)."

# Set trigger to stop script if disconnected
setEventTrigger 0 :End "Connection lost"


# Menu
:menu
echo "**" ANSI_4 "1. Delete Ferengi Homeworld and Sector Settings*"
echo ANSI_4 "2. Set Borg Game Settings*"
echo "**" ANSI_3 "Selection: "

getConsoleInput $adminfunction SINGLEKEY

# Remove Ferengi Homeworld
if ($adminfunction = "1") then
end

# Set Borg Game Settings
if ($adminfunction = "2") then
	# Setup Port Names.
	send "p1*aSupply Hive 1 of 3*>"
	send "aSupply Hive 2 of 3*>"
	send "aSupply Hive 3 of 3*>"
	send "Master Supply Hive*x"
	# Setup Master Hive.
	send "n1*aMaster Hive*h7b10000000"
end


# Functions




:End