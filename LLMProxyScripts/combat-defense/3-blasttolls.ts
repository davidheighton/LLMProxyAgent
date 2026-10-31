# BlastTolls
# Auto-attack toll fighters when you come across them
# by synack

:start
send "c;q"
setTextLineTrigger max_attack :max_attack "Max Figs Per Attack:"
pause

:max_attack
getWord CURRENTLINE $maxattack 5
stripText $maxattack ","
goto :wait

:wait
setTextLineTrigger go :go "You have to destroy the fighters"
pause

:go
setTextLineTrigger Attack :Attack "Your fighters: "
pause

:Attack
getWord CURRENTLINE $MyFigs 3
stripText $MyFigs ","
if ($MyFigs < $maxattack)
	setVar $attackwith $MyFigs
else
	setVar $attackwith $maxattack
end
getWord CURRENTLINE $TheirFigs 6
stripText $TheirFigs ","
if ($MyFigs > $TheirFigs)
	send "a" $attackwith "*"
else
	send "r"
end
goto :wait
