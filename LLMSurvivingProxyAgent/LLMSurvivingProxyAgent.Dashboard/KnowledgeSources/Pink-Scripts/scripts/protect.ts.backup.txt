# Exiled TWX Script: Homesector Fedspace Protection Script! -;) 0.06
# Author           : Pinky
# Description      : 
# Warnings         : Must Have Homesector with fuel ore planet
#		     Must Have at least Photon Missile onboard
# 	             Must Place Fighters First (for now)
#	             Must run from Stardock or Homesector
#		     Must turn off Animated Display
# Other            :

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Command")
        clientMessage "This script must be run from the game command menu"
        halt
end                    


# Load game settings
:loadsettings
read protectf.txt $stardocksettings 1
read protectf.txt $homesectorsettings 2
getWord $stardocksettings $stardock 1
getWord $stardocksettings $stardockoreplanet 2
getWord $homesectorsettings $homesector 1
getWord $homesectorsettings $homesectororeplanet 2 
getWord $homesectorsettings $cashplanet 3
getWord $homesectorsettings $transportplanet 4
getWord $homesectorsettings $defensiveship 5
getWord $homesectorsettings $offensiveship 6

# get the sector we're in
send "d" 
setTextLineTrigger GetSector :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $currentsector 3
setVar $originsector $currentsector

# get amount of photon torps
setVar $photonmissiles 0
send "i"
setTextLineTrigger GetAmountMissiles :getamountmissiles "Photon Missiles:"
pause
:getamountmissiles
getWord CURRENTLINE $photonmissiles 3

# get cash amount
waitFor "Command [TL="
send "i"
setTextLineTrigger GetCashAmount :getcashamount "Credits"
pause
:getcashamount
getWord CURRENTLINE $cashamount 3
stripText $cashamount ","

# Set trigger to stop script if disconnected
setEventTrigger ConnectionLost :End "Connection lost"

# show script banner
waitFor "Command [TL="
echo "**" ANSI_4 "     --" ANSI_12 "===| " ANSI_15 "FedSpace HomeSector Protection Script Version 0.06" ANSI_12 " |===" ANSI_4 "--**"
echo ANSI_3 "         If you have it keep it to yourself. -;).*"
echo ANSI_5 "*Stardock: " ANSI_14 $stardock ANSI_5 " Ore Planet: " ANSI_14 $stardockoreplanet 
echo ANSI_5 "*Homesector: " ANSI_14 $homesector ANSI_5 " Ore Planet: " ANSI_14 $homesectororeplanet ANSI_5 " Cash Planet: " ANSI_14 $cashplanet
echo ANSI_5 "*Current Sector: " ANSI_14 $currentsector 
echo ANSI_5 "*Photons: " ANSI_14 $photonmissiles 
echo ANSI_5 "*Credits: " ANSI_14 $cashamount "**"

:getrefuel
echo ANSI_4 "Auto Refuel Photons? "
getConsoleInput $photonrefuel SINGLEKEY
if ($photonrefuel <> "Y") and ($photonrefuel <> "N") then
	echo "*" ANSI_4 $photonrefuel ANSI_12 " is not a vailid response**"
	goto :getrefuel
end
if ($photonrefuel = "Y") then
	echo "*" ANSI_1 "You will dock at Stardock to get photons when you Run out! (UNSAFE MODE)**"
	echo ANSI_4
	getinput $photonamount "How Many Photons Should we Refuel? " 1
	setVar $photoncashamount $photonamount
	multiply $photoncashamount 40000
	echo ANSI_11 "**Photon Refuel will cost " $photoncashamount " per refuel*"
	
else
	echo "*" ANSI_2 "You will not refuel if you run out of Photons (SAFE MODE)!"
	echo "*" ANSI_12 "Script Will Halt when Photons Are Gone, Notify will sound**"
end

if ($currentsector = $stardock)
	setVar $refuel 0
else
	setVar $refuel 1
end

setVar $linebroke 0

# get photon and move type
:getmovetype
echo ANSI_4 "(P)lanetary Transporters [Longest Range of Attack/Return]"
echo ANSI_4 "Ship (T)ransporters [Best for Homesector Only Defense]"
echo ANSI_4 "Ship Trans(W)arp [Use Only if you have no other Options]"
echo ANSI_4 " "
echo ANSI_4 "What Type of Protection Would you like?"

getConsoleInput $movetype SINGLEKEY
if ($movetype <> "T") and ($movetype <> "W") and ($movetype <> "P") then
        echo "*" ANSI_4 $movetype ANSI_12 " is not a vailid response**"
        goto :getmovetype
end



#refuel and check settings
:setup
if ($currentsector = $stardock) and ($stardockoreplanet > 0) and ($refuel = 1)
	echo ANSI_4 "**[ FUEL ORE ][1]"
	# Fill Up on Fuel Ore
	send "l" $stardockoreplanet "*t*t1*q"
	goto :protect

elseif ($currentsector = $stardock) and ($stardockoreplanet = 0) and ($refuel = 1)
	echo ANSI_4 "**[ FUEL ORE ][2]"
	send "m" $homesector "*yyl" $homesectororeplanet "*t*t1*qm" $stardock "*yy"
	goto :protect

elseif ($currentsector = $homesector) and ($refuel = 1)
	echo ANSI_4 "**[ FUEL ORE ][3]"
	send "l" $homesectororeplanet "*t*t1*q"
	goto :protect

elseif ($refuel = 0)
	goto :protect
	
else
	echo ANSI_12 "**Something is wrong please check settigns!**"
	halt

end

:protect
setTextLineTrigger DeployedFighters :deployedfighters "Deployed Fighters"
setTextLineTrigger DeployedLMine :deployedlmine "Limpet mine"
echo ANSI_4 "**[ Waiting For Breach ]**"
if ($photonmissiles = 0) then
	waitFor "Blasting"
	echo "**" ANSI_4 "[ YOU ARE OUT OF PHOTONS SCRIPT ENDING!!! ]**"
	send "*"
	sound notify.wav
	halt
end
pause

:deployedfighters
echo ANSI_4 "**[ DEPLOYED ][1]"
setVar $deployline CURRENTLINE
stripText $deployline ":"
getWord $deployline $deploysector 5
getWord $deployline $intrudername 6
getWord $deployline $intruderstatus 7
setVar $deploypass 3
goto :protect_deploysectortest

:deployedamines
echo ANSI_4 "**[ DEPLOYED ][2]"
setVar $deployline CURRENTLINE
getWord $deployline $deploysector 4
getWord $deployline $intrudername 9
setVar $intruderstatus "Armid"
setVar $deploypass 3
goto :protect_deploysectortest

:deployedlmine
echo ANSI_4 "**[ DEPLOYED ][3]"
setVar $deployline CURRENTLINE
getWord $deployline $deploysector 4
setVar $intrudername "Unknown"
setVar $intruderstatus "Limpet"
setVar $deploypass 3
goto :protect_deploysectortest

:protect_deploysectortest
read protectf.txt $protect $deploypass
getWord $protect $protector 1
getWord $protect $protected 2
getWord $protect $photonship 3
getWord $protect $attackiflinebroke 4
if ($protect = "EOF") or ($protect = 0) then
        Echo "**" ANSI_4 $deploysector " not a protected Sector!**"
	killTrigger DeployedFighters
        killTrigger DeployedLMine
	goto :protect

elseif ($deploysector = "a")
        killTrigger DeployedFighters
        killTrigger DeployedLMine
	goto :protect

elseif ($intruderstatus = "retreated.")
	echo "**" ANSI_4 $intrudername " retreated seek and destroy!**"
        killTrigger DeployedFighters
        killTrigger DeployedLMine
        goto :protect

elseif ($intruderstatus = "is")
	echo "**" ANSI_4 $intrudername " ship attacked! take care of him!**"
        killTrigger DeployedFighters
        killTrigger DeployedLMine
        goto :protect

elseif ($photonmissiles = 0)
	echo "**" ANSI_4 "[ YOU ARE OUT OF PHOTONS SCRIPT ENDING!!! ]**"
	send "*"
	sound notify.wav
	halt


elseif ($deploysector = $protected) 
	echo ANSI_4 "*Pass: " ANSI_14 $deploypass ANSI_4 " Protector: " ANSI_14 $protector ANSI_4 " Protected: " ANSI_14 $protected
	Echo ANSI_13 "**Protected Sector Breached! Sending Photon!**"
	killTrigger DeployedFighters
        killTrigger DeployedLMine
	goto :sendphoton

else
	echo ANSI_4 "*Pass: " ANSI_14 $deploypass ANSI_4 " Protector: " ANSI_14 $protector ANSI_4 " Protected: " ANSI_14 $protected
	add $deploypass 1
	goto :protect_deploysectortest
end


:sendphoton
if ($currentsector = $protector) then
	echo ANSI_4 "**[ SEND PHOTON ][1]*"
	send "cpy" $protected "*q
	waitFor "Command [TL="
	send "Sh"
	waitFor "Command [TL="
	sound photon.wav
	subtract $photonmissiles 1
	setVar $refuel 0
	goto :photons
	
elseif ($photonship = 0) or ($movetype = "W") then
	echo ANSI_4 "**[ SEND PHOTON ][2]*"
	send "m" $protector "*yy"
        waitFor "Command [TL="
	send "cpy" $protected "*q"
	waitFor "Command [TL="
	send "sh"
	waitFor "Command [TL="
	send "m" $originsector "*yy"
	waitFor "Command [TL="
	sound photon.wav
	subtract $photonmissiles 1
	setVar $refuel 1
	goto :photons

elseif ($movetype = "T") then
        # x34*qshx35*q
        echo ANSI_4 "**[ SEND PHOTON ][3]*"
        send "x" $photonship "*q"
        send "cpy" $protected "*q"
        send "shx" $offensiveship "*q"
        setVar $linebroke 1
        setVar $refuel 0
        goto :photons

else
        echo ANSI_4 "**[ WAITING FOR SECONDARY ATTACK ]"
        goto :photons

end

:photons
if ($cashamount > $photoncashamount) and ($currentsector = $stardock) and ($photonrefuel = "Y")
	echo ANSI_4 "**[ REFUEL PHOTON ][1]"
	send "pshp" $photonamount "*qq"
	subtract $cashamount $photoncashamount
	add $photonmissiles $photonamount
	setVar $refuel 0
	goto :setup

elseif ($photonmissiles = 0) and ($currentsector = $stardock) and ($photonrefuel = "Y")
	echo ANSI_4 "**[ REFUEL PHOTON ][2]"
	send "m" $homesector "*yy"
	send "l" $homesectororeplanet "*t*t1*q"
	send "l" $cashplanet "*ctf" $photoncashamount "*qq" 
	send "m" $stardock "*yy"
	send "pshp" $photonamount "*qq"
	subtract $cashamount $photoncashamount
	add $photonmissiles $photonamount
	setVar $refuel 0
	goto :setup

elseif ($photonmissiles = 0) and ($currentsector = $homesector) and ($photonrefuel = "Y")
	echo ANSI_4 "**[ REFUEL PHOTON ][3]"
	send "l" $homesectororeplanet "*t*t1*q"
	send "l" $cashplanet "*ctf" $photoncashamount "*qq"
        add $cashamount $photoncashamount
	send "m" $stardock "*yy"
	send "pshp" $photonamount "*qq"	
	subtract $cashamount $photoncashamount
	add $photonmissiles $photonamount
	send "m" $homesector "*yy"
	setVar $refuel 1
	goto :setup

else
	goto :setup
end

:End

# dumbest disrupted 10 of your mines in 2374
# Limpet mine in 2374 activated
# Your mines in 2374 did 1576 damage to dumbest
# Deployed Fighters Report Sector 2374: dumber's Merchant Cruiser entered sector.
# Deployed Fighters Report Sector 2374: dumber retreated.
# Deployed Fighters Report Sector 2374: dumbest is attacking!
# dumbie paid the toll of 50 credits to your fighters in sector 2374
