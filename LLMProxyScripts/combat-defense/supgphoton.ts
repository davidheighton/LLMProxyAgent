#SupGPhoton Ver 1.0
#by SupG
#Run from Citadel or Command prompt
#simple FAST photon script
#this photon script should be faster than those that check to see if 
#the sector in question is adjacent by using the getSector command
#this script uses the getDistance command so there is no looping involved
#ONLY USE THIS SCRIPT IF YOU HAVE MAPPED THE UNIVERSE USING ZTM OR HAVE EXPLORED A LOT
cutText CURRENTLINE $location 1 7
if ($location = "Command") or ($location = "Citadel")
	send "I"
	waitfor "Current Sector :"
	getWord CURRENTLINE $current_sector 4
	goto :wait
else
	send "'Run this script from either the Citadel or Command prompt!*"
	halt
end

:wait
setVar $pos 0
setTextLineTrigger limpettrip :trip_limpet "Limpet mine in"
setTextLineTrigger trip :trip_fig "Deployed Fighters Report Sector"
setTextLineTrigger keepalive :keepalive "INACTIVITY WARNING:"
pause

:trip_limpet
killalltriggers
getWord CURRENTLINE $sector 4
getWord CURRENTLINE $filter 1
goto :check

:trip_fig
killalltriggers
getWord CURRENTLINE $sector 5
getWord CURRENTLINE $filter 1
goto :check

:check
stripText $sector ":"
if ($filter <> "Limpet") and ($filter <> "Deployed")
	goto :wait
else
	getDistance $distance $current_sector $sector
	if ($distance = 1)
		goto :torp
	elseif ($distance = 0)
		goto :current_sector_response
	else
		goto :wait
	end
end

:torp
send "cpy" $sector "*q"
sound "message.wav"
goto :hold_up

:current_sector_response
#=-=-=-=-=-=-=-= Put reaction commands below in the case that someone enters your current sector. =-=-=-=-=-=-=-=-=-=-
waitfor "(?="
cutText CURRENTLINE $location 1 7
#if ($location = "Citadel")
#	send "qq"
#end
:sec_wait
setTextLineTrigger keepalive :keepalive_2 "INACTIVITY WARNING:"
pause
#-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-end current sector reaction commands =-=-=-=-=-=-=-=-=-=-=-=-=-=-

:keepalive 
killalltriggers
send "#"
goto :wait

:keepalive_2
killalltriggers
send "#"
goto :sec_wait

:hold_up
setDelayTrigger heh :wait 1000
pause