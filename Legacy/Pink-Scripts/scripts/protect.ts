# Exiled TWX Script: Homesector Fedspace Protection Script! -;) 0.95
# Author           : Pinky
# Description      : 
# Warnings         : Must Have Homesector with fuel ore planet
#		     Must Have Transport Planet with a long range.
#		     Must Have at least Photon Missile onboard
# 	             Must Place Fighters and Limpets First (for now)
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
getWord $homesectorsettings $attackplanet 5
getWord $homesectorsettings $defensiveship 6
getWord $homesectorsettings $offensiveship 7

# get the sector we're in
send "d" 
setTextLineTrigger GetSector :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $currentsector 3
setVar $originsector $currentsector
setVar $incitadel 0

# get cash amount
waitFor "Command [TL="
send "i"
setTextLineTrigger GetCashAmount :getcashamount "Credits"
pause
:getcashamount
getWord CURRENTLINE $cashamount 3
stripText $cashamount ","

# Set trigger to stop script if disconnected and setup transwarp protection
setEventTrigger ConnectionLost :End "Connection lost"
setTextLineTrigger SelfDestructProtection :SelfDestructProtection "ARE YOU SURE CAPTIAN?"
setTextLineTrigger TranswarpProtection :TranswarpProtection "Do you want to make this jump blind?"
setTextLineTrigger TransportProtection :TransportProtection "Do you want to make this transport blind?"

# Show Script Information and Setup information.
waitFor "Command [TL="
echo "**" ANSI_4 "     --" ANSI_12 "===| " ANSI_15 "Photon HomeSector Protection and Hunting Script Version 0.95" ANSI_12 " |===" ANSI_4 "--*"
echo ANSI_4 "     --" ANSI_12 "===| " ANSI_15 "This TWX Script is by pinky@exiled.org | Keep it to yourself" ANSI_12 " |===" ANSI_4 "--**"
echo ANSI_5 "*Stardock: " ANSI_14 $stardock ANSI_5 " Ore Planet: " ANSI_14 $stardockoreplanet 
echo ANSI_5 "*Homesector: " ANSI_14 $homesector ANSI_5 " Ore Planet: " ANSI_14 $homesectororeplanet ANSI_5 " Cash Planet: " ANSI_14 $cashplanet ANSI_5 " Transport Planet: " ANSI_14 $transportplanet
echo ANSI_5 "*Current Sector: " ANSI_14 $currentsector 
echo ANSI_5 "*Photons: " ANSI_14 $photonmissiles 
echo ANSI_5 "*Credits: " ANSI_14 $cashamount "**"

:getrefuel
echo ANSI_12 "[ Do not use Refuel Option if playing Evil! ]**"
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
	echo "*" ANSI_12 "Script Will Halt when Photons Are Gone!**"
end

if ($currentsector = $stardock)
	setVar $refuel 0
else
	setVar $refuel 1
end

setVar $linebroke 0

# get photon and move type
:getmovetype
echo ANSI_4 "**[" ANSI_7 "P" ANSI_4 "]lanetary Transporters [" ANSI_6 "Longest Range of Attack/Return" ANSI_4 "]*"
echo ANSI_4 "Ship [" ANSI_7 "T" ANSI_4 "]ransporters [" ANSI_6 "Best for Homesector Only Defense" ANSI_4 "]*"
echo ANSI_4 "Ship Trans[" ANSI_7 "W" ANSI_4 "]arp [" ANSI_6 "Use Only if you have no other Options" ANSI_4 "]*"
echo ANSI_4 "Standard Planet [" ANSI_7 "D" ANSI_4 "]rop [" ANSI_6 "You set the amount of Dammage to Quasar Blast" ANSI_4 "]*"
echo ANSI_4 "[" ANSI_7 "E" ANSI_4 "]nhanced Planet Drop [" ANSI_6 "Uses Protected List to Drop then Photon" ANSI_4 "]"
echo ANSI_4 "**"
echo ANSI_4 "What Type of Protection Would you like?"

getConsoleInput $movetype SINGLEKEY
if ($movetype <> "T") and ($movetype <> "W") and ($movetype <> "P") and ($movetype <> "D") and ($movetype <> "E") then
        echo "*" ANSI_4 $movetype ANSI_12 " is not a vailid response**"
        goto :getmovetype
end

if ($movetype <> "D") then
# get amount of photon torps
setVar $photonmissiles 0
send "i"
setTextLineTrigger GetAmountMissiles :getamountmissiles "Photon Missiles:"
pause
:getamountmissiles
getWord CURRENTLINE $photonmissiles 3
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
	send "m" $homesector "*y"
	waitFor "All Systems Ready, shall we engage?"
	send "yl" $homesectororeplanet "*t*t1*qm" $stardock "*y"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
	goto :protect

elseif ($currentsector = $homesector) and ($refuel = 1)
	echo ANSI_4 "**[ FUEL ORE ][3]"
	send "l" $homesectororeplanet "*t*t1*q"
	goto :protect

elseif ($refuel = 0)
	goto :protect
	
else
	echo ANSI_12 "**[ Something is wrong please check settigns! ]**"
	halt

end

:protect
if ($movetype = "D") or ($movetype = "E") then
 	setTextLineTrigger DeployedFighters :deployedfighters "Deployed Fighters"
else
	setTextLineTrigger DeployedLMine :deployedlmine "Limpet mine"
end

# setTextLineTrigger DeployedAMine :deployedamines "Your mines"

echo ANSI_4 "**[ Waiting For Breach ]**"
if ($photonmissiles = 0) and ($movetype <> "D") then
	waitFor "Blasting"
	echo "**" ANSI_4 "[ " ANSI_12 "YOU ARE OUT OF PHOTONS SCRIPT ENDING!!! " ANSI_12 "]**"
	send "*"
	halt
end
if ($movetype = "P") and ($incitadel = 0) then
	send "l" $transportplanet "*c"
	setVar $incitadel 1
end
if ($movetype = "D") and ($incitadel = 0) then
	send "l" $attackplanet "*c"
	setVar $incitadel 1
end
if ($movetype = "E") and ($incitadel = 0) then
	send "l" $attackplanet "*c"
	setVar $incitadel 1
end

pause

:deployedfighters
echo ANSI_4 "**[ DEPLOYED FIGHTERS ][ Getting sector information! ]"
setVar $deployline CURRENTLINE
stripText $deployline ":"
getWord $deployline $deploysector 5
getWord $deployline $intrudername 6
getWord $deployline $intruderstatus 7
setVar $deploypass 3
setVar $droppass 1
if ($movetype = "D") then
	goto :planet_drop
else
	goto :protect_deploysectortest
end

:deployedamines
echo ANSI_4 "**[ DEPLOYED ARMID MINES ][ Getting sector information! ]"
setVar $deployline CURRENTLINE
getWord $deployline $deploysector 4
getWord $deployline $intrudername 9
setVar $intruderstatus "Armid"
setVar $deploypass 3
goto :protect_deploysectortest

:deployedlmine
echo ANSI_4 "**[ DEPLOYED LIMPET MINES ][ Getting sector information! ]"
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
        Echo "**" ANSI_4 "[" ANSI_12 $deploysector ANSI_4 "] not a protected Sector! Returning to Protect Mode**"
	killTrigger DeployedFighters
        killTrigger DeployedLMine
	goto :protect

elseif ($deploysector = "a")
        killTrigger DeployedFighters
        killTrigger DeployedLMine
	goto :protect

elseif ($intruderstatus = "retreated.")
	echo "**" ANSI_4 "[" ANSI_12 $intrudername ANSI_4"] retreated seek and destroy!**"
        killTrigger DeployedFighters
        killTrigger DeployedLMine
        goto :protect

elseif ($intruderstatus = "is")
	echo "**" ANSI_4 "[" ANSI_12 $intrudername ANSI_4"] ship attacked! take care of him!**"
        killTrigger DeployedFighters
        killTrigger DeployedLMine
        goto :protect

elseif ($photonmissiles = 0)
	echo "**" ANSI_4 "[" ANSI_12 " YOU ARE OUT OF PHOTONS SCRIPT ENDING!!! " ANSI_4 "]**"
	send "*"
	halt


elseif ($deploysector = $protected) 
	echo ANSI_4 "*Pass: " ANSI_12 $deploypass ANSI_4 " Protector: " ANSI_12 $protector ANSI_4 " Protected: " ANSI_12 $protected
	Echo ANSI_13 "**Protected Sector Breached! Sending Photon!**"
	killTrigger DeployedFighters
        killTrigger DeployedLMine
	goto :sendphoton

else
	# echo ANSI_4 "*Pass: " ANSI_12 $deploypass ANSI_4 " Protector: " ANSI_12 $protector ANSI_4 " Protected: " ANSI_12 $protected
	add $deploypass 1
	goto :protect_deploysectortest
end


:sendphoton
if ($movetype = "E") then
	echo ANSI_4 "**[ SEND PLANET TO SECTOR ][ PHASE 1 ]"
	send "p" $protected "*y"
	echo ANSI_4 "**[ SEND SHIP TO SECTOR NEXT DOOR AND FIRE PHOTON ][ PHASE 2 ]"
	send "b" $protector "*"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
	waitFor "Command [TL="
	send "cpy" $protected "*q"
	waitFor "Command [TL="
	goto :Capture_Return_Test


elseif ($movetype = "P") and ($currentsector = $protector) then
	echo ANSI_4 "**[ SEND PHOTON ][FROM PLANET]"
	send "cpy" $protected "*q"
	waitFor "Command [TL="
	send "qqsh"
	waitFor "Command [TL="
	subtract $photonmissiles 1
	setVar $refuel 0
	setVar $incitadel 0
	send "' I just Lunched a Photon into Sector " $protected
	goto :photons

elseif ($movetype = "P") then
	echo ANSI_4 "**[ SEND PHOTON ][USING PLANETARY TRANSPORT]"
	send "b" $protector "*"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
	waitFor "Command [TL="
	send "cpy" $protected "*q"
	waitFor "Command [TL="
	send "sh"
	waitFor "Command [TL="
	send "m" $originsector "*y"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
	waitFor "Command [TL="
	subtract $photonmissiles 1
	setVar $refuel 1
	setVar $incitadel 0
	goto :photons

elseif ($currentsector = $protector) then
	echo ANSI_4 "**[ SEND PHOTON ][FROM SECTOR]*"
	send "cpy" $protected "*q"
	waitFor "Command [TL="
	send "Sh"
	waitFor "Command [TL="
	subtract $photonmissiles 1
	setVar $refuel 0
	goto :photons
	
elseif ($photonship = 0) or ($movetype = "W") then
	echo ANSI_4 "**[ SEND PHOTON ][USING TRANSWARP DRIVE]*"
	send "m" $protector "*y"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
        waitFor "Command [TL="
	send "cpy" $protected "*q"
	waitFor "Command [TL="
	send "sh"
	waitFor "Command [TL="
	send "m" $originsector "*y"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
	waitFor "Command [TL="
	subtract $photonmissiles 1
	setVar $refuel 1
	goto :photons

elseif ($movetype = "T") then
        # x34*qshx35*q
        echo ANSI_4 "**[ SEND PHOTON ][USING SHIP TRANSPORT SYSTEMS]*"
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
	send "m" $homesector "*y"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
	send "l" $homesectororeplanet "*t*t1*q"
	send "l" $cashplanet "*ctf" $photoncashamount "*qq" 
	send "l" $transportplanet "*cb" $stardock "*"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
	waitFor "Command [TL="
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
	send "l" $transportplanet "*cb" $stardock "*"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
	waitFor "Command [TL="
	send "pshp" $photonamount "*qq"	
	subtract $cashamount $photoncashamount
	add $photonmissiles $photonamount
	send "m" $homesector "*y"
	waitFor "All Systems Ready, shall we engage?"
	send "y"
	setVar $refuel 1
	goto :setup

else
	goto :setup
end

:planet_drop
# lets find out if its somebody we want to capture.
read pdrop.txt $pdropon $droppass
if ($pdropon = "EOF") then
 goto :protect
end
if ($pdropon = $intrudername) then
  #Drop Planet and end script.
  send "p" $deploysector "*ys"	
  setVar $incitedel = 1
  goto :CaptureShip
else
  add $droppass 1
  goto :planet_drop
end
goto :End

:Capture_Return_Test
setTextLineTrigger TraderStillThere :TraderStillThere "Traders"
send "sh"

:NothingReturnHome
waitFor "Command [TL="
setTextLineTrigger PlanetOnly :PlanetOnly "Planets"
send "sh"
waitFor "Command [TL="
send "m" $originsector "*y"
waitFor "All Systems Ready, shall we engage?"
send "y"
waitFor "Command [TL="
subtract $photonmissiles 1
setVar $refuel 1
setVar $incitadel 0
goto :photons

:TraderStillThere
killTrigger PlanetOnly
waitFor "Command [TL="
send "m" $protected "*"
setVar $incitadel 0
goto :CaptureShip

:PlanetOnly
killTrigger TraderStillThere
waitFor "Command [TL="
send "m" $protected "*"
send "l" $attackplanet "*cp" $originsector "*y"
send "qq"
setVar $refuel 1
setVar $incitadel 0
goto :photons

:CaptureShip
if ($incitadel = 1) then
send "qq"
setVar $incitadel = 0
end
:AttackLoop
send "a"
waitFor "Attack "
setVar $attackline CURRENTLINE
stripText $attackline " "
replaceText $attackline "-" " "
replaceText $attackline "(" " "
replaceText $attackline ")" " "
stripText $attackline ","
getWord $attackline $fightersonboard 2
getWord $attackline $deadfigs 3
setVar $attackfigs $deadfigs
divide $attackfigs 3
echo $attackline
 
echo "**[ Fighters on Board = " $fightersonboard " ]*"
echo "[ Enemy Figs to Kill = " $deadfigs " ]*"
echo "[ Fighters to attack with = " $attackfigs " ]**"
if ($fightersonboard >= $deadfigs) then
	send "y"
else
	send "Y" 
	# goto :End
end
waitFor "How many fig"
setVar $howmanyfigsline CURRENTLINE
stripText $howmanyfigsline "("
stripText $howmanyfigsline ")"
stripText $howmanyfigsline ","
getWord $howmanyfigsline $maxattackfigs 11

echo "**[ Max Attack Figs for this Ship = " $maxattackfigs " ]**"
setVar $pinkreloadfigs $maxattackfigs
add $pinkreloadfigs $maxattackfigs
add $pinkreloadfigs $maxattackfigs
echo "**[ Reload Figs when down to " $pinkreloadfigs " ]**"

if ($fightersonboard <= $pinkreloadfigs) then
	send "0*"
	send "l" $attackplanet "*t*t1*m***q"
	echo "*[ Drop to Attack Planet and Reload ]*"
	goto :AttackLoop

elseif ($deadfigs <= 10) and ($attackfigs <= 3) then
	echo "*[ Attack using 1 Fighter ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger defeat :Defeat "For defeating"
	send "1*"
	pause

elseif ($attackfigs > $maxattackfigs) and ($fightersonboard > $deadfigs) then
	echo "*[ Attack using " $maxattackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger defeat :Defeat "For defeating"
	send $maxattackfigs "*"
	pause

elseif ($attackfigs > $maxattackfigs) and ($fightersonboard < $deadfigs) then
	echo "*[ Attack using " $maxattackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger defeat :Defeat "For defeating"
	send $maxattackfigs "*"
	pause

elseif ($maxattackfigs > $deadfigs) and ($fightersonboard > $deadfigs) then
	echo "*[ Attack using " $attackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger defeat :Defeat "For defeating"
	send $attackfigs "*"
	pause

elseif ($attackfigs > $maxattackfigs) then
	echo "*[ Attack using " $maxattackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger defeat :Defeat "For defeating"
	send $maxattackfigs "*"
	pause
else
	echo "*[ Attack using " $attackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger defeat :Defeat "For defeating"
	send $attackfigs "*"
	pause
	
end


:Destroyed
setVar $destroyedline CURRENTLINE
stripText $destroyedline ","
getWord $destroyedline $enemyfigsleft 6
echo "*[ Fighters Remaining " $enemyfigsleft " ]*"
killTrigger defeat
killTrigger destroyed
goto :AttackLoop

:Defeat
echo "*[ This Should Have worked! -;) ]*"
goto :End


# Transwarp and Transport protection Use [ENTER] rather than [N] so doesn't conflict with other helpers.

:TransportProtection
echo ANSI_4 "**[ SENDING [ENTER] FOR TRANSPROT PROTECTION ]**"
send "*"
echo ANSI_4 "Transport Protection Hit Going into Protect Mode!"
killTrigger DeployedFighters
killTrigger DeployedLMine
setTextLineTrigger TransportProtection :TransportProtection "Do you want to make this transport blind?"
echo ANSI_4 "**[ Transport Protection Compleate ]**"
goto :protect

:TranswarpProtection
echo ANSI_4 "**[ SENDING [ENTER] FOR TRANSWARP PROTECTION ]**"
send "*"
echo ANSI_4 "Transwarp Protection Hit Going into Protect Mode!"
killTrigger DeployedFighters
killTrigger DeployedLMine
setTextLineTrigger TranswarpProtection :TranswarpProtection "Do you want to make this jump blind?"
echo ANSI_4 "**[ Transport Protection Compleate ]**"
goto :protect

:SelfDestructProtection
echo ANSI_4 "**[ SENDING [ENTER] [Q] FOR SELF DESTRUCT PROTECTION ]**"
send "*q"
echo ANSI_4 "Self Destruct Protection Hit Going into Protect Mode!"
killTrigger DeployedFighters
killTrigger DeployedLMine
setTextLineTrigger SelfDestructProtection :SelfDestructProtection "ARE YOU SURE CAPTIAN?"
echo ANSI_4 "**[ Self Destuct Protection Compleate ]**"
goto :protect

:End

# dumbest disrupted 10 of your mines in 2374
# Limpet mine in 2374 activated
# Your mines in 2374 did 1576 damage to dumbest
# Deployed Fighters Report Sector 2374: dumber's Merchant Cruiser entered sector.
# Deployed Fighters Report Sector 2374: dumber retreated.
# Deployed Fighters Report Sector 2374: dumbest is attacking!
# dumbie paid the toll of 50 credits to your fighters in sector 2374
