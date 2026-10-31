# Author           : The Bounty Hunter
# Description      : Simple script that protects you from blind
#                    warping.
# Trigger Point    : Any blind Warp
# Advertising      : www.wherechaosreigns.org

:wait
setTextTrigger no :no "Do you want to make this jump blind?"
pause

:no
send "n"
goto :wait


