# Quick script to calculate % for amount to fire on sector q-gun.
# Author:	 Gravedigr
# Description:	 Start in citadel and will set % for accurate firing of sector cannon.
getWord CURRENTLINE $prompt 1
if ($prompt <> "Citadel")
  clientMessage "Script Aborted: Must be at Planet prompt."
  HALT
end
Getinput $fire "How much damage do you want to inflict?"
send qd
killAllTriggers
setTextLineTrigger fig :Ore "Fuel Ore"
PAUSE

:Ore
killAllTriggers
getWord CURRENTLINE $fuel 6
stripText $fuel ","
waitfor "Planet command (?=help"
send "c"
multiply $fire 3
multiply $fire 100
divide $fire $fuel
add $fire 1
waitfor "Citadel command (?=help)"
send "ls" $fire "*"
end 



