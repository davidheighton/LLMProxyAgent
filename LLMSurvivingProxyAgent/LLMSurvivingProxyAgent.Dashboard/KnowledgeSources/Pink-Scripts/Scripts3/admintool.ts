# Exiled TWX Script: Exiled Admin Script / Borg
# Author           : Pinky
# Description      : 
# Warnings         :
# Other            : 
                             
# show script banner
:Start
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "Exiled Admin Script" ANSI_11 " |===" ANSI_3 "--**"
echo ANSI_7 "If you have it keep it to yourself. -;)."

# Set trigger to stop script if disconnected
setEventTrigger 0 :End "Connection lost"


# Menu
:menu
echo "**" ANSI_4 "1. Delete Ferengi Homeworld and Sector Settings*"
echo ANSI_4 "2. Set Borg Assimilation Game Settings*"
echo ANSI_4 "3. Set Alpha Quadrant Game Settings (Turn Game)*"
echo ANSI_4 "4. Set Alpha Quadrant Game Settings (Unlimited Game)*"
echo ANSI_4 "5. Set Alpha Quadrant Alien Homeworlds (Unlimited Game)*"
echo ANSI_4 "0. Quit*"
echo "**" ANSI_3 "Selection: "

getConsoleInput $adminfunction SINGLEKEY

# Remove Ferengi Homeworld
if ($adminfunction = 1)
	# Remove Planet find Sector
	send "n2*"
	setTextLineTrigger GetSector :getsector "<O> Location"
	pause
	:getsector
	getWord CURRENTLINE $FerengiSector 5 5
	send "z311qx"
	if ($FerengiSector < 10000)
	 send "s" $FerengiSector "*"
	else 
	 send "s" $FerengiSector
	end
	send "z*n*j0*h0*x"
end

# Set Borg Assimilation Game Settings
if ($adminfunction = 2)
	# Setup Port Names.
	send "p1*aSupply Hive 1 of 3*>"
	send "aSupply Hive 2 of 3*>"
	send "aSupply Hive 3 of 3*>"
	send "aMaster Supply Hive*x"
	# Setup Master Hive.
	send "n1*aMaster Hive*h7b10000000*"
end

# Set Alpha Qaudrant Game Settings
if ($adminfunction = 3)
	# Setup Port Names.
	send "p1*aInternational Station*>"
	send "aDeep Space 7*>"
	send "aDeep Space 8*>"
	send "aDeep Space 9*x"
	# Setup Earth.
	send "n1*aEarth**"
end

# Set Alpha Qaudrant Game Settings
if ($adminfunction = 4)
	# Setup Port Names.
	send "p1*aInternational Station*>"
	send "aDeep Space 7*>"
	send "aDeep Space 8*>"
	send "aDeep Space 9*x"
	# Setup Earth.
	send "n1*aEarth*h17b10000000*"
end

if ($adminfunction = 5)
	send "n2*i1000000w30000>"
	send "i1000000w30000>"
	send "i1000000w30000>"
	send "i1000000w30000>"
	send "i1000000w30000>"
	send "i1000000w30000>"
	send "i1000000w30000*"
end

if ($adminfunction = 0)
	goto :END
end

:End