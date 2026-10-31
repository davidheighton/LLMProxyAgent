# Grid - Beta : clear avoids account and have your warp paths planned.

# Setup
:Setup
# Leave Ship Above Planet
	setVar $planetship 6
# Start In This Ship
	setVar $walkship 1
# If Anomoly or Density Warp Back Here
	setVar $escapesector 299
# Planet to use
	setVar $planet 6
# Max Density 
	setVar $turnarounddensity 500

send "s"
setTextLineTrigger SectorCheck :SectorCheck "Sector  :"
pause
:SectorCheck
killalltriggers
setVar $SectorLine CURRENTLINE
getWord $SectorLine $currentsector 3
settexttrigger minedsector :minedsector "Mined Sector: Do you wish"
settexttrigger pastminedsector :pastminedsector "Citadel command"
pause
:minedsector
send "n"
:pastminedsector

# Main
:Main
killAllTriggers
GetInput $destination "[ Enter Sector to Grid To ]: "

:Grid
getcourse $warppath $currentsector $destination
getdistance $distance $currentsector $destination
setVar $warploop 1
:GridLoop
if ($distance = 1)
   Goto :SingleStepMode
end
if ($warploop <= $warppath)
	add $warploop 1

	# Gridding script by Gravedigr and Xide
	# Macro to save ship: b4577*yx60**l86*c

	getsector $warppath[$warploop] $sectorinfo
	if ($sectorinfo.ANOMOLY = "YES")
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $destination $escapesector
		goto :Grid
	end
	if ($sectorinfo.DENSITY > $turnarounddensity)
		subtract $warploop 1
		setVar $currentsector $warppath[$warploop]
		setVar $destination $escapesector
		goto :Grid
	end
	setvar $dens $sectorinfo.DENSITY
	send "qq"
	waitfor "Command [TL="
	send "sd"
	waitfor "Command [TL="
	getsector $warppath[$warploop] $sectorinfo1
	setvar $dens1 $sectorinfo1.DENSITY
	if ($dens1 > $dens)
		send "l" $planet "*cs'" $griddisplayname ": density change in " $warppath[$warploop] "*"
		goto :Main
	else
		send $warppath[$warploop] "* at9999** f1*ctsdx " $planetship "**  l" $planet "*c"
		waitfor "Citadel command"
		send "p" $warppath[$warploop] "*yqq x" $walkship "**h1t4*cq*l" $planet "*c"
		waitFor "Citadel command"
	end
	goto :GridLoop
end

:SingleStepMode
getsector $destination $sectorinfo
setvar $dens $sectorinfo.DENSITY
if ($sectorinfo.ANOMOLY = "YES")
	subtract $warploop 1
	setVar $currentsector $warppath[$warploop]
	setVar $destination $escapesector
	goto :Grid
end
if ($sectorinfo.DENSITY > $turnarounddensity)
	subtract $warploop 1
	setVar $currentsector $warppath[$warploop]
	setVar $destination $escapesector
	goto :Grid
end
send "qq"
waitfor "Command [TL="
send "sd"
waitfor "Command [TL="
getsector $destination $sectorinfo1
setvar $dens1 $sectorinfo1.DENSITY
if ($dens1 > $dens)
	send "l" $planet "*cs'" $griddisplayname ": density change in " $destination "*"
	goto :Main
else
	send $destination "* at9999** f1*ctsdx " $planetship "**  l" $planet "*c"
	waitfor "Citadel command"
	send "p" $destination "*yqq x" $walkship "**h1t4*cq*l" $planet "*c"
	waitfor "Citadel command"

end
setVar $currentsector $destination
:END
