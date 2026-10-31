# TWX Script       : SupGNearFig
# Author           : SupG
# Description      : Bot script that calculates the nearest fig to sector
#                    specified.  
# Directions       : Script is activated by you or a corpie talking on subspace
#		     There is a trigger symbol the script looks for.
#	 	     <ampersand><sector> finds the closest fig to the sector specified
#		     Example: ' <ampersand>181 would find your corpie the closest fig to sector 181
# Trigger Point    : Command Prompt
#
# Warnings         : This script may not get you a jump fig every time. It will
#		     not check through one ways, so you may get a 'No figs found' 
#		     message if it runs out of options.  I've been working on a
#		     work-around for this. Keep checking back for updates  
# Other            : This script may error out if you have not done a thorough ZTM
#		     or have not explored a lot of the universe
#		     If you are the person running this script for your corpies and
#		     you wish to use it, you have to talk into subspace the same as your
#		     corpies would, just simply put a space before you type.
#		     Example:  ' <ampersand>181
cutText CURRENTLINE $location 1 12
if ($location <> "Command [TL=")
        clientMessage "This script must be run from the command prompt"
        halt
end
send "G"
:update
setVar $fig_sec 0
waitfor "==============="
:get_figs
getWord CURRENTLINE $sector 1
getWord CURRENTLINE $stopper 2
isNumber $secnum $sector
if ($secnum = "TRUE")
	setVar $figgies[$sector] 1
end
if ($stopper = "Total")
	goto :top
end
setTextLineTrigger grab :get_figs
pause
:top
setVar $backs 0
setVar $hops 0
setVar $adjacents 0
setVar $targetcount 0
setTextLineTrigger checksec :calculate "&"
pause

:calculate
killalltriggers
getWord CURRENTLINE $tester 1
if ($tester = "'")
	getWord CURRENTLINE $figcheck 2
elseif ($tester = "R")
	getWord CURRENTLINE $figcheck 3
else
	goto :top
end
stripText $figcheck "&"
isNumber $result $figcheck
if ($result <> "TRUE")
	goto :top
end
if ($figcheck > SECTORS) or ($figcheck < 1)
	goto :top
end
if ($figgies[$figcheck] = 1)
	setVar $jumpfig $figcheck
	goto :done
end
setVar $target $figcheck
gosub :checkadj

:checkhops
setVar $preadj $adjacents
if ($adjacents = 0)
	send "'No figs found*"
	goto :top
end
setVar $adjacents 0
add $hops 1
setVar $prehop $hops
subtract $prehop 1
setVar $hopscount 0
:hopscount
if ($hopscount < $preadj)
	add $hopscount 1
	setVar $target $adjacent[$hopscount]_[$prehop]
	gosub :checkadj
	goto :hopscount
end
goto :checkhops
	

:checkadj
setVar $warpcount 0
getsector $target $targetinfo
add $targetcount 1
setVar $donesecs[$targetcount] $target
:warpcount
if ($warpcount < 7)
	add $warpcount 1
	setVar $thesec $targetinfo.warp[$warpcount]
	setVar $doneloop 0
	if ($thesec = 0)
		return
	end
:doneloop
	if ($doneloop < $targetcount)
		add $doneloop 1
		if ($donesecs[$doneloop] = $thesec)
			goto :warpcount
		end
		goto :doneloop
	end
	getdistance $backdoorcheck $thesec $target
	if ($backdoorcheck > 1)
		goto :warpcount
	end
	if ($figgies[$thesec] = 1)
		add $hops 1
		setVar $jumpfig $thesec
		goto :done
	else
		add $adjacents 1
		setVar $adjacent[$adjacents]_[$hops] $targetinfo.warp[$warpcount]
	end
	goto :warpcount
end

:done
getDistance $hoppy $jumpfig $figcheck
send "'Nearest fig to " $figcheck " is in " $jumpfig ", " $hoppy " hop(s) away.*"
goto :top
