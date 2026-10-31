# TWX SupG Script  : SupGBWarp
# Author           : SupG
# Description      : Simple script that uses the planetary transporter
#                    to warp you to a sector being hit by an enemy.
# Trigger Point    : Citadel prompt

# Other            : Let me know of any problems with this script
#www.twtavern.com
:wait
setTextLineTrigger hitfig :get_distance "Deployed Fighters Report Sector"
pause
:get_distance
getWord CURRENTLINE $activated_sector 5
stripText $activated_sector ":"
send "b" $activated_sector "*"
setTextTrigger go :go "All Systems Ready, shall we engage?"
setTextTrigger no :no "Do you want to make this transport blind?"
pause

:no
send "n"
goto :wait

:go
send "y"
goto :wait

