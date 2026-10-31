# Script Name      : SupGKillFigs.ts
# Author           : SupG
# Description      : Warps you from one spot to another killing figs as it goes
#		     Safer and simpler version of SupGLawnMower
# Trigger Point    : Sector command prompt
# Warnings         : Script will not allow single step warping, even if you want to. If you need to single
#		     step, turn it off, or manually warp each sector
# Other            : You are free to modify this script to your liking, but please give me
#                    credit for any of my code that you use.
# Scripter's Tavern : www.twtavern.com
systemscript
:top
:clear_sector
setTextTrigger hitfig :hit_fig "Your fighters:"
setTextTrigger hitmine :hit_mine "Mined Sector: Do you wish to Avoid this sector in the future? (Y/N)"
setTextLineTrigger clear :ready_state "Autopilot disengaging."
setTextTrigger done :ready_state "Command [TL="
setTextTrigger continue :keep_rollin "Stop in this sector"
pause

:keep_rollin
killtrigger figgie
killtrigger hitmine
killtrigger clear
killtrigger hitfig
killtrigger done
send "n"
goto :clear_sector

:hit_fig
killtrigger hitmine
killtrigger clear
killtrigger continue
killtrigger done
send "a9999*"
goto :clear_sector

:hit_mine
killtrigger hitfig
killtrigger clear
killtrigger continue
killtrigger done
send "n"
goto :clear_sector

:ready_state
killtrigger hitmine
killtrigger hitfig
killtrigger continue
killtrigger done
killtrigger clear
goto :top