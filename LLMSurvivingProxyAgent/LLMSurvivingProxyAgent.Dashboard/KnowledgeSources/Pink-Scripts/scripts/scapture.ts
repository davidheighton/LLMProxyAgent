# Exiled TWX Script: Exiled Surround Capture Script Rev. 03
# Author           : Pinky
# Description      : 
# Warnings         : Some Ships May Over Power Other Ships Make sure to try and match odds!
# Other            : 

logging off

# show script banner
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "Exiled Surround Capture Script Version 0.03" ANSI_11 " |===" ANSI_3 "--**"
echo ANSI_7 "If you have it keep it to yourself. -;)."

# Set trigger to stop script if disconnected
setEventTrigger 0 :End "Connection lost"


# get the sector we're in
send "d" 
setTextLineTrigger GetSector :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $currentsector 3
waitFor "Command [TL="

#determine warps out of sector
setTextLineTrigger GetWarps :GetWarps "Warps to Sector(s) :"
send "*"
pause
:GetWarps
setVar $line CURRENTLINE
stripText $line "Warps to Sector(s) :"
stripText $line "("
stripText $line ")"
stripText $line "-"
setVar $numWarps 0
:FINDWARPSLOOP
if ($numWarps < 7)
Add $numWarps 1 
getWord $line $Warp[$numWarps] $numWarps
Goto :FINDWARPSLOOP
end
waitFor "Command [TL="

echo "*[ Warps : " $line " ]*"
# Get Info
getInput $numFighters "*How many fighters to drop per sector? "


#move to each warp 
setVar $i 1
:MOVEAGAIN
if ($i <= 7)
if ($Warp[$i] <> 0)
if (1 > 0)
send $Warp[$i] "*"
waitFor "Command [TL="
send "f" $numFighters "*ct" 
waitFor "Command [TL="
send $CurrentSector "*"
waitFor "Command [TL="
else
send $Warp[$i] "*"
waitFor "Command [TL="
send "f0*" 
waitFor "Command [TL="
send $CurrentSector "*"
waitFor "Command [TL="
end
end
Add $i 1
Goto :MOVEAGAIN
end
#drop current line
if ("Y" = "Y")
if (1 > 0)
send "f" $numfighters "*ct"
waitFor "Command [TL="
else
send "f0*"
waitFor "Command [TL="
endif
end 


#Send attack and wait for fig amounts.
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

#if ($fightersonboard <= $maxattackfigs) then
#	
#	echo "*[ Manual Bail-out Point Unless you can code in a P-Drop ]*"
#	goto :End
#
#else

if ($deadfigs <= 10) and ($attackfigs <= 3) then
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
:End