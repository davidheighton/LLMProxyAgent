# Script Name : pinkfiglayer.ts
# Author      : pink (thanks SupG/Xide for lawnmower code/macro)
# Description : Lawnmower to each sector in a list. Turns around at Limpets and 500+ Density
# Problems    : Fedspace will kill you
#	      : Avoids will kill you
#             : Photons will kill you
setVar $avoid_navhaz 20
setVar $avoid_density 500
setVar $pass 1
setVar $listpass 1
# check if we can run it from here
cutText CURRENTLINE $location 1 12

if ($location <> "Command [TL=")
        clientMessage "This script must be run from the Command Prompt"
        halt
end


# show script banner
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "Fig Layer Version 0.01" ANSI_11 " |===" ANSI_3 "--**"
echo ANSI_7 "If you have it keep it to yourself. -;)."

# Set trigger to stop script if disconnected
setEventTrigger 0 :End "Connection lost"

:Menu
echo "**" ANSI_5 "Destination Sector List:*"
:SectorList
read sectclr.txt $Dest_Sector $listpass
if ($Dest_Sector = 0)
	Goto :Selection
end	
echo "*" $Dest_sector
add $listpass 1
Goto :SectorList
:Selection

echo "**Ready? (Y/N)"
getConsoleInput $heh singlekey
upperCase $heh
if ($heh = "Y")
	goto :top
else
	echo "**SCRIPT ENDED!!*"
	goto :end
end

:top
send "d"
setTextLineTrigger sector :sector "Sector  :"
pause

:sector
getWord CURRENTLINE $current 3
read sectclr.txt $destination $pass
if ($destination = 0)
	Goto :End
end	
add $pass 1

getDistance $distance $current $destination
getCourse $lane $current $destination
setVar $spanky 1
:spanky

if ($spanky < $lane)
	add $spanky 1
	#macro by Xide
	GetSector $lane[$spanky] $sectinfo
	echo "**DENSITY!!!: " $sectinfo.DENSITY "**"
	if ($sectinfo.DENSITY > $avoid_density)
	goto :top
	end
	if ($sectinfo.ANOMOLY = YES)
	echo "**ANOMOLY!!!**"
	goto :top
	end
	if ($sectinfo.NAVHAZ = $avoid_navhaz)
	echo "**NAVHAZ!!!: " $sectinfo.NAVHAZ "**"
	goto :top
	end
	send "m" $lane[$spanky] "*atny9999**f1*ctsd"
	waitFor "Long Range Scan"
	waitFor "Command [TL="
	goto :spanky
end

GetSector $destination $sectinfo
echo "**DENSITY-2!!!: " $sectinfo.DENSITY "**" 
if ($sectinfo.DENSITY > $avoid_density)
goto :top
end
if ($sectinfo.ANOMOLY = YES)
echo "**ANOMOLY-2!!!**"
goto :top 
end
if ($sectinfo.NAVHAZ = $avoid_navhaz)
echo "**NAVHAZ-2!!!: " $sectinfo.NAVHAZ "**"
goto :top
end
send "m" $destination "*atny9999**f1*ctsd"
waitFor "Long Range Scan"
waitFor "Command [TL="
end
goto :top

:END