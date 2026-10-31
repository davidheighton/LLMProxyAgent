#run from command prompt, when corp/personal fig is tripped, the script will report the distance from your current location over fedcomm
setVar $trip_file GAMENAME & ".trp"
cutText CURRENTLINE $location 1 12

if ($location <> "Command [TL=")
        clientMessage "This script must be run from the command prompt"
        halt
end
send "d"
:wait
killalltriggers
setTextLineTrigger currentsector :get_sector "Sector  :"
setTextLineTrigger hitfig :get_distance "Deployed Fighters Report Sector"
pause
:get_distance
 getWord CURRENTLINE $activated_sector 5
 stripText $activated_sector ":"
 getWord CURRENTLINE $person 6
striptext $person "^M" 
striptext $person "'s"
getDistance $hops $activated_sector $current_sector
if ($hops > 1)
	waitfor "(?="
	send "'" $person " tripped fig " $hops " hops from current location.*"
end
if ($hops > 1) and ($hops < 4)
	sound whoosh.wav
end
goto :wait

:get_sector
 getWord CURRENTLINE $current_sector 3 
goto :wait