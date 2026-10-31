# density torp
# checks for torps and fedspace...then density scans for greater dens
# fires one time sends subspace sector info and halts

setVar $OZ_dtorp_Version "OZ-DTorp 2.0"

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Command")
        clientMessage "This script must be run from the Command Prompt"
        halt
end

# loadVar $ozdtorpSaved

# if ($ozdtorpSaved)
#	loadVar 
# else
#	setVar 
#
#	saveVar 
#	
#	
#	setVar $ozdtorpSaved 1
#	saveVar $ozdtorpSaved
# end

addMenu "" "OZ-DTorp" "OZ-Charge Settings" "." "" "Main" FALSE
addMenu "OZ-DTorp" "Run" "Run Script." "Z" :Menu_Run "" TRUE
# addMenu "OZ-DTorp" "Sector List" "Sector List" "1" :Menu_List "" FALSE


setMenuHelp "Run" "This Option Activates the Script."
# setMenuHelp "Sector List" "This Option Sets the sectors to clear.*Please enter the sectors seperated by a semicolon.*Example - 399;455;677"

gosub :sub_setMenu
gosub :OZ_dtorpHeader
openMenu "OZ-DTorp"

:OZ_dtorpHeader
ECHO ANSI_2 "*----------------------------------*"
ECHO ANSI_10 "           " $OZ_dtorp_Version " "
ECHO ANSI_2 "*----------------------------------*"
return

:sub_setMenu
#	setMenuValue "Sector List" $sec_list
#	setMenuValue "Figs to Lay" $figs
#	setMenuValue "Type of Figs" $type
#	setMenuValue "Mines to Lay" $mines
#	setMenuValue "Limpets to Lay" $limp
#	setMenuValue "Pickup-Photon" $photon
#	setMenuValue "Pickup-Done" $pickup
#	setMenuValue "TWarp-Done" $twarp
	
return


:Menu_Run
:begin
send "cp*q"
:checkntorps
setTextLineTrigger anyphots :anyphots "Photon Missiles left."
setTextLineTrigger hmmtorps :hmmtorps "You do not have any Photon Missiles!"
setTextLineTrigger fed :feds "The Feds do not permit protected"
pause

:anyphots
killTrigger fed
killTrigger hmmtorps
send "cn1qq"
goto :check_dens


:feds
send "'OZ-DTorp cant launch from fedspace*"
halt

:hmmtorps
send "'OZ-DTorp No Photons*"
halt

:check_dens
setVar $mm 0
setVar $i 0
send "sd"
waitFor "Long Range Scan"
:Start
killTrigger alldone
setTextLineTrigger getSec :getSec "Sector"
setTextTrigger allDone :allDone "Command [TL="
pause
:getSec
add $i 1
getText CURRENTLINE $Adj[$i] "Sector" "==>"
stripText $adj[$i] "("
stripText $adj[$i] ")"
stripText $adj[$i] " "
getText CURRENTLINE $Dens[$i] "==>" "Warps :"
stripText $dens[$i] ","
stripText $dens[$i] " "
goto :Start
:allDone
killTrigger getSec
gosub :firechk
:letslook 
setVar $w 0
:sublooky
add $w 1
if ($w > $i)
	goto :alldone
elseif ($density[$w] > $dens[$w])
	send "cpy" $adj[$w] "*Q"
	send "'OZ-DTorp - P-Missle Fired into sector " $adj[$w] "*"
	send "cn1qq"
	halt
else
	goto :sublooky
end

:firechk
add $mm 1
if ($mm = 20)
	send "'OZ-DTorp -  WARNING - Density Photon Running at My TA!!!*"
	setVar $mm 0
end
setVar $y 0
send "sd"
waitFor "Long Range Scan"
:looky
setTextLineTrigger getSec :looksec "Sector"
setTextTrigger allDone :donelook "Command [TL="
pause
:looksec
add $y 1
getText CURRENTLINE $Adjsec[$y] "Sector" "==>"
stripText $adjsec[$y] "("
stripText $adjsec[$y] ")"
stripText $adjsec[$y] " "
getText CURRENTLINE $Density[$y] "==>" "Warps :"
stripText $density[$y] ","
stripText $density[$y] " "
killTrigger alldone
goto :looky
:donelook
killtrigger getSec
return





