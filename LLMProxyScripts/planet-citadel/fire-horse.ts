# TWX SupG Script  : Fire Horse Advanced Online Combat Script
# Author           : SupG and Res Judicata
# Description      : Script Requires 2 ships and a warpable planet,
#                    clears sectors using 2 ships for safety.
#                    Warps planet to target sector after xporting.
#                    Can kill 1 hop or several Xport range dictates.
# Trigger Point    : Citadel command prompt
# Warnings         : Don't run it without checking max xport range
#                    Script has no defences but will xport upon being IG'd
#                    Will Destroy or be Destroyed. Use with Extreme Caution.
# Other            : A modified version of RES-EDIT Macro first used in WTC.
#                  : Muhahaha..
#www.twtavern.com


# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Citadel")
        clientMessage "This script must be run from the citadel"
        halt
end

logging off
getInput $ship1 "Ship 1(other ship)" 0
getInput $ship2 "Ship 2(current ship)" 0
getInput $planet "Planet Number" 0
:top
getInput $sector "Sector?" 0
send "qqx  " $ship1 "*  q"
gosub :move
send "f1*cdx  " $ship2 "*"
setTextLineTrigger nohop :no_xport "only has a transport range of"
setTextLineTrigger safe :xport "Security code accepted,"
setTextLineTrigger noship :no_xport "That is not an available ship."
setTextLineTrigger noturn :no_xport "any turns left."
pause

:xport
send "ql  " $planet "*  cp" $sector "*ys"
waitfor "<Scan Sector>"
setTextTrigger citmine :citmine "Mined Sector: Do you wish to Avoid this sector in the future? (Y/N)"
setTextTrigger incit :incit "Citadel command (?=help)"
pause

:citmine
send "n"

:incit
if ($ig = 1)
	halt
end
goto :top

:no_xport
send "q"
halt

:move
send "m" $sector "*"
waitfor "<Move>"
setTextTrigger twarp :no_twarp "Do you want to engage the TransWarp drive?"
setTextTrigger express :express_warp "Engage the Autopilot?"
setTextTrigger in_adj :there "Sector  : " & $sector
setTextLineTrigger voided_sec :voided "DANGER! You have marked sector"
pause

:voided
killtrigger twarp
killtrigger in_adj
killtrigger express
getWord CURRENTLINE $void 7
send "n"

:no_twarp
killtrigger in_adj
killtrigger express
killtrigger voided_sec
send "n"

:express_warp
killtrigger twarp
killtrigger in_adj
killtrigger voided_sec
send "e"

:there
killtrigger in_adj
killtrigger voided_sec
killtrigger twarp
killtrigger express

gosub :clear_sector
waitfor "Command [TL="
return

:clear_sector
setTextTrigger hitfig :hit_fig "Your fighters:"
setTextTrigger hitmine :hit_mine "Mined Sector: Do you wish to Avoid this sector in the future? (Y/N)"
setTextLineTrigger clear :ready_state "Autopilot disengaging."
setTextTrigger done :ready_state "Command [TL="
setTextTrigger continue :keep_rollin "Stop in this sector"
setTextLineTrigger escape :ig "An Interdictor Generator in this sector holds you fast!"
setTextTrigger pause :pause "[Pause]"
pause

:keep_rollin
killalltriggers
send "n"
goto :clear_sector

:hit_fig
killalltriggers
send "a999*"
goto :clear_sector

:hit_mine
killalltriggers
send "n"
goto :clear_sector

:pause
killalltriggers
send "*"
goto :clear_sector

:ready_state
killalltriggers
return

:ig
killalltriggers
setVar $ig 1
send "**"
waitfor "Command [TL="
getText CURRENTLINE $sector "]:[" "] (?="
return