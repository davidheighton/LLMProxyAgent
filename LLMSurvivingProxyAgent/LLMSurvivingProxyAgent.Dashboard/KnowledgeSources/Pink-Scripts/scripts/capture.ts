# Exiled TWX Script: Exiled Capture Script Rev. 01
# Author           : Pinky
# Description      : 
# Warnings         :
# Other            : 

logging off

# show script banner
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "Exiled Capture Script Version 0.02" ANSI_11 " |===" ANSI_3 "--**"
echo ANSI_7 "If you have it keep it to yourself. -;)."

# Set trigger to stop script if disconnected
setEventTrigger 0 :End "Connection lost"


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
	setTextLineTrigger lost :Destroyed "You lost"
	setTextLineTrigger defeat :Defeat "For defeating"
	send "1*"
	pause

elseif ($attackfigs > $maxattackfigs) and ($fightersonboard > $deadfigs) then
	echo "*[ Attack using " $maxattackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger lost :Destroyed "You lost"
	setTextLineTrigger defeat :Defeat "For defeating"
	send $maxattackfigs "*"
	pause

elseif ($attackfigs > $maxattackfigs) and ($fightersonboard < $deadfigs) then
	echo "*[ Attack using " $maxattackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger lost :Destroyed "You lost"
	setTextLineTrigger defeat :Defeat "For defeating"
	send $maxattackfigs "*"
	pause

elseif ($maxattackfigs > $deadfigs) and ($fightersonboard > $deadfigs) then
	echo "*[ Attack using " $attackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger lost :Destroyed "You lost"
	setTextLineTrigger defeat :Defeat "For defeating"
	send $attackfigs "*"
	pause

elseif ($attackfigs > $maxattackfigs) then
	echo "*[ Attack using " $maxattackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger lost :Destroyed "You lost"
	setTextLineTrigger defeat :Defeat "For defeating"
	send $maxattackfigs "*"
	pause
else
	echo "*[ Attack using " $attackfigs " fighters ]*"
	setTextLineTrigger destroyed :Destroyed "You destroyed"
	setTextLineTrigger lost :Destroyed "You lost"
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