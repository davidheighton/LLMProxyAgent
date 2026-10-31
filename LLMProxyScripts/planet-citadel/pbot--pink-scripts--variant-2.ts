# Exiled TWX Script: planet/photon/hide/protect pbot script
# Author           : pinky
# Description      : script with many options
# Warnings         : still in testing don't blame me if ya get killed. -;)
# Other            : thanks to Hekate for testing with me! -;)

# check if we can run it from here
cutText CURRENTLINE $locationline 1 14
getWord $locationline $location 1

if ($location <> "Planet") and ($location <> "Citadel")
        clientMessage "This script must be run from the surface of a planet"
        halt
end

# setup environment
:SetupEnironment
# Set Value of StarDock or Script will do it for you at Startup.
setVar $pbotversioninfo "pbot v0.030803 by pink"
setVar $maxphotons 5
setVar $stardock 0 
SetVar $manualescape 0
SetVar $redalert 5
SetVar $orangealert 10
SetVar $reganlock 0
SetVar $photontoggle 1
SetVar $pbotenabled 1
setVar $photoncost 40000
setVar $currentphotons 0
setVar $currentfuelore 0
setVar $cashamount 0
setVar $currentturns 0

# Build Information
:BuildInformation
# Check Planet Number and Current Sector then Enter Citadel.
:GetCurrent
if ($location = "Planet")
	send "*c"
end
if ($location = "Citadel")
	send "q*c"
	setVar $location "Planet"
end
setTextLineTrigger GetPlanetInfo :getplanetinfo "Planet #"
pause
:getplanetinfo
getWord CURRENTLINE $currentplanet 2
stripText $currentplanet "#"
getWord CURRENTLINE $currentsector 5
stripText $currentsector ":"
getWord CURRENTLINE $currentplanetname 6
waitFor "Citadel command"
# Auto Load Stardock Info
if ($stardock = 0)
	send "qqv"
	settextlinetrigger StarDock :StarDockLocation "The StarDock is located in sector"
	pause
	:StarDockLocation
	setVar $stardockline CURRENTLINE
	getword $stardockline $stardock 7
	striptext $stardock "."
	waitfor "Command [TL="
	send "l" $currentplanet "*c"
	waitfor "Citadel command"
end

# Get Fuel Ore Amount Reload if less then full holds.
:GetFuelOre
send "i"
setTextLineTrigger GetHoldInformation :GetHoldInformation "Total Holds    :"
pause
:GetHoldInformation
KillAllTriggers
setVar $holdinformationline CURRENTLINE
striptext $holdinformationline "="
getWord $holdinformationline $currentholds 4
getWord $holdinformationline $holdstatus 6
if ($holdstatus <> "Fuel")
	setVar $currentfuelore 0
end
if ($holdstatus = "Fuel")
	getWord $holdinformationline $currentfuelore 7
	striptext $currentfuelore "Ore"
end

if ($currentfuelore < $currentholds)
	send "qtnl3*"
	settextlinetrigger NoRoomE :NoRoom "They don't have room"
	settextlinetrigger YDumpedE :DumpedE "You unload the"
	settexttrigger DumpedE :DumpedE "Planet command" 
	pause
	:DumpedE
	killAllTriggers
	send "tnl2*"
	settextlinetrigger NoRoomO :NoRoom "They don't have room"
	settextlinetrigger YdumpedO :DumpedO "You unload the"
	settexttrigger DumpedO :DumpedO "Planet command"
	pause
	:DumpedO
	killAllTriggers
	send "tnt1*c"
	waitfor "Citadel command"
	goto :GetFuelOre
	:NoRoom
	killAllTriggers
	send "qjyl" $currentplanet "*c"
	goto :GetFuelore
	 
end

# Get Photon Amount
if ($photontoggle = 1)
	setTextLineTrigger currentphotons :currentphotons  "Photon Missiles:"
	setDelayTrigger nocurrentphotons :nocurrentphotons 1000
	pause
	:nocurrentphotons
		KillAllTriggers
		setVar $currentphotons 0
		SetVar $photontoggle 0
		goto :pastphotons
	
	:currentphotons
		KillAllTriggers
		getWord CURRENTLINE $currentphotons 3
		SetVar $photontoggle 1
		waitfor "Citadel command"
		goto :pastphotons
end
:pastphotons

# Get Cash Amount
:GetCashAmount
if ($cashamount = 0)
	send "i"
	setTextLineTrigger GetCashAmount :getcashamount "Credits"
	pause
	:getcashamount
	killAllTriggers
	getWord CURRENTLINE $currentcredits 3
	stripText $currentcredits ","
	waitFor "Citadel command"
end

# Get Turns
:GetTurnAmount
	send "i"
	setTextLineTrigger GetTurnAmount :getturns "Turns left"
	pause
	:getturns
	killAllTriggers
	getWord CURRENTLINE $currentturns 4
	stripText $currentturns ","
waitFor "Citadel command"

# Display Environment Local
:DisplayEnvironment
echo ANSI_3 "**[STARDOCKLOCATION]:" ANSI_15 $stardock "*"
echo ANSI_3 "[CURRENTPLANET]:" ANSI_11 $currentplanetname " # "$currentplanet "*"
echo ANSI_3 "[CURRENTSECTOR]:" ANSI_11 $currentsector "*"
echo ANSI_3 "[CURRENTCREDITS]:" ANSI_4 $currentcredits "*"
echo ANSI_3 "[CURRENTPHOTONS]:" ANSI_4 $currentphotons "*"
echo ANSI_3 "[CURRENTTURNS]:" ANSI_4 $currentturns "*"
ECHO ANSI_3 "[CURRENTHOLDS]:" ANSI_6 $currentholds "*"
ECHO ANSI_3 "[CURRENTFUELORE]:" ANSI_6 $currentfuelore "**"


# Read Sector Info File and Find Current
:FindEscapeforCurrent
setVar $pass 1
:ReadHideFile
read hidesect.txt $hideline $pass
getWord $hideline $hidesector 1
if ($hidesector = $currentsector)
	getWord $hideline $escapesector1 2
	getWord $hideline $escapesector2 3
	getWord $hideline $escapesector3 4
	getWord $hideline $escapesectorzone 5
	setVar $manualescape 0
if ($pbotenabled = 1)
	if ($photontoggle = 1)
	send "'pbot: photon hiding zone " $escapesectorzone " (auto escape mode)*"
	else
	send "'pbot: hiding zone " $escapesectorzone " (auto escape mode)*"
	end
end
if ($pbotenabled = 1)
	goto :GetTriggers
else
	goto :BotOffTriggers
end
else
	add $pass 1
	if ($hidesector = 0)
	goto :ManualEscape
	end
	goto :ReadHideFile
end

# ManualEscape When Online and out in freespace.
:ManualEscape
read hidesect.txt $hideline 2
getWord $hideline $escapesector1 1
getWord $hideline $escapesector2 2
getWord $hideline $escapesector3 3
setVar $escapesectorzone "FreeSpace"
setVar $manualescape 1
if ($pbotenabled = 1)
	if ($photontoggle = 1)
		send "'pbot: photon protecting zone " $escapesectorzone " (manual escape mode)*"
	else
		send "'pbot: protecting zone " $escapesectorzone " (manual escape mode)*"
	end
end
if ($pbotenabled = 1)
	goto :GetTriggers
else
	goto :BotOffTriggers
end

# Set Triggers and Wait
:GetTriggers
setTextLineTrigger DeployedFighters :deployedfighters "Deployed Fighters"
# Quasar Cannon Trigger
setTextLineTrigger QCannonFire :QCannonFire "Quasar Cannon on"
# Limpet mode not implemented yet.
#setTextLineTrigger DeployedLMine :deployedlmine "Limpet mine"
:BotOffTriggers
setTextLineTrigger IncomingCommand :IncomingCommand ".pbot"
setTextLineTrigger SectorCheck :SectorCheck "Sector  :"
setTextTrigger LocalRefresh :LocalRefresh "Do you wish to end your turn now and remain here?"
setEventTrigger StandBy :StandBy "Connection lost"
:TriggerPause
pause

# Incoming Command Processor 
:IncomingCommand
setVar $commandline CURRENTLINE
echo ANSI_3 "*INCOMING COMMAND: " ANSI_4 $commandline

getWord $commandline $pbotuser 2
echo ANSI_3 "*INCOMING USER: " ANSI_4 $pbotuser
# User Access Control
#
if ($pbotuser = "local")
	setVar $CommandShift 9
	getWord $commandline $pbotcommand 3
else
	setVar $CommandShift 0
	getWord $commandline $pbotcommand 4
end

if ($pbotcommand = ".pbot")
	setVar $CommandShift 1
	getWord $commandline $pbotcommand 5
end

echo ANSI_3 "*INCOMING COMMAND: " ANSI_4 $pbotcommand "*"
# Process Command
if ($pbotcommand = "off") or ($pbotcommand = "sleep")
	Goto :BotToggleOff
end

if ($pbotcommand = "on") or ($pbotcommand = "wakie")
	Goto :BotToggleOn
end

if ($pbotcommand = "refresh") or ($pbotcommand = "re")
	Goto :RefreshLocation
end

if ($pbotcommand = "status") or ($pbotcommand = "stat")
	Goto :PbotStatus
end

if ($pbotcommand = "help") or ($pbotcommand = "?")
	Goto :PbotHelp
end

if ($pbotcommand = "version") or ($pbotcommand = "ver")
	Goto :PbotVersion
end

if ($pbotenabled = 1)
	if ($pbotcommand = "reload")
		if ($commandShift = 1)
			getWord $commandline $getphotons 6
		else
			getWord $commandline $getphotons 5
		end
		if ($CommandShift = 9)
			getWord $commandline $getphotons 4
		end
		Goto :ReloadPhotons
	end
	if ($pbotcommand = "photon") or ($pbotcommand = "pho") or ($pbotcommand = "photons")
		if ($commandShift = 1)
			getWord $commandline $photoninfo 6
		else
			getWord $commandline $photoninfo 5
		end
		if ($CommandShift = 9)
			getWord $commandline $photoninfo 4
		end
		Goto :PhotonToggle
	end
	if ($pbotcommand = "come") or ($pbotcommand = "here")
		Goto :BringBot
	end
end

setTextLineTrigger IncomingCommand :IncomingCommand ".pbot"
goto :TriggerPause

:SectorCheck
setVar $SectorCheckLine CURRENTLINE
getWord $SectorCheckLine $SectorCheckSector 3
if ($SectorCheckSector = $currentsector)
	setTextLineTrigger SectorCheck :SectorCheck "Sector  :"
	goto :TriggerPause
end
killAllTriggers
send "q"
goto :GetCurrent

:ReloadPhotons
isnumber $getphotonyes $getphotons
if ($getphotonyes = "TRUE")
	goto :YesPhoton	
end

:NoPhoton
send "'pbot: not a number.*"
	setTextLineTrigger IncomingCommand :IncomingCommand ".pbot"
	goto :TriggerPause

:YesPhoton
setVar $totalphotons $getphotons
add $totalphotons $currentphotons
if ($totalphotons > $maxphotons)
	send "'pbot: can't get " $getphotons " photons, max is " $maxphotons " and i have " $currentphotons " photons already.*"
	setTextLineTrigger IncomingCommand :IncomingCommand ".pbot"
	goto :TriggerPause
end
setVar $totalphotoncost $photoncost
multiply $totalphotoncost $getphotons

if ($getphotons = 0) or ($getphotonsyes = "FALSE")
	send "'pbot: invalid photon amount*"
	setTextLineTrigger IncomingCommand :IncomingCommand ".pbot"
	goto :TriggerPause
end
KillAllTriggers
#getcredits for photons
send "tf" $totalphotoncost "*"
send "b" $stardock "*"
setTextlineTrigger StardockReady :StardockReady "Locating beam pinpointed"
setTextlineTrigger StardockBlind :StardockBlind "No locating beam found"
pause
:StardockReady
killalltriggers
send "ypshp" $getphotons "*qq"
send "cuyqm" $currentsector "*y"
setTextlineTrigger LockOnHome :LockOnHome "Locating beam pinpointed"
setTextlineTrigger NoLockOnHome :NoLockOnHome "No locating beam found"
setTextlineTrigger NoFuelOreOnShip :NoFuelOreOnShip "You do not have enough"
pause
:LockOnHome
	KillAllTriggers
	send "yl" $currentplanet "*c"
	goto :PhotonExitPoint

:NoLockOnHome
	KillAllTriggers
	send "n"
	send "'pbot: Script Ending Can't Gain Lock on Planet Location. I'll hide on dock.*"
	send "psg"
	goto :END

:NoFuelOreOnShip
	KillAllTriggers
	send "'pbot: Script Ending Not Enough Fuel Ore to Return. I'll hide on dock.*"
	send "psg"
	goto :END

:StarDockBlind
	KillAllTriggers
	send "n"
	send "'pbot: I don't have the ability to warp to stardock.*"
	goto :PhotonExitPoint


:PhotonExitPoint
send "q"
goto :GetCurrent


:BringBot
KillAllTriggers
setVar $userbring CURRENTLINE
getWord $userbring $username 2
send "xa"
setTextLineTrigger BringBotXA :BringBotXA $username
setTextLineTrigger BringBotNA :BringBotNA "P indicates Trader"
pause
:BringBotXA
killAllTriggers
setVar $locationline CURRENTLINE
getWord $locationline $userlocation 2
StripText $userlocation "P"
isnumber $namecheck $userlocation
if ($namecheck = "TRUE")
	send "qp" $userlocation "*"
	setTextlineTrigger AllSystemsReady7 :Ready7 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind7     :CannotBlind7 "You cannot"
	setTextlineTrigger AlreadyThere     :AlreadyThere "You are already in that sector"
	pause
	:Ready7
	KillAllTriggers
	send "yq"
	goto :GetCurrent
	:CannotBlind7
	send "q"
	send "'pbot: Could Not Gain Lock on your Location.*"
	goto :GetCurrent	
	:AlreadyThere
	KillAllTriggers
	send "q"
	send "'pbot: i'm already here!*"
	goto :GetCurrent
else
	getWord $locationline $userlocation 3
	isnumber $namecheck2 $userlocation
	if ($namecheck2 = "TRUE")
		send "qp" $userlocation "*"
		setTextlineTrigger AllSystemsReady8 :Ready8 "Locating beam pinpointed"
		setTextlineTrigger CannotBlind8     :CannotBlind8 "You cannot"
		setTextlineTrigger AlreadyThere     :AlreadyThere1 "You are already in that sector"
		pause
		:Ready8
		KillAllTriggers
		send "yq"
		goto :GetCurrent
		:CannotBlind8
		send "q"
		send "'pbot: Could Not Gain Lock on your Location.*"
		goto :GetCurrent
		:AlreadyThere1
		KillAllTriggers
		send "q"
		send "'pbot: i'm already here!*"
		goto :GetCurrent
	end
end
send "'pbot: Error Getting Your Location*"
send "qq"
goto :GetCurrent
:BringBotNA
send "'pbot: Your Not On the Corp!*"
killAllTriggers
send "qq"
goto :GetCurrent


# Photons on/off FIRE!
:PhotonToggle
killAllTriggers
isnumber $sendphoton $photoninfo
if ($sendphoton = "TRUE")
	if ($photoninfo > 0)
	getdistance $distance $currentsector $photoninfo
	if ($distance = 1)
		send "cpy" $photoninfo "*q"
		send "'pbot: photon fired!*"
		send "q"
		goto :GetCurrent
	else
		send "'pbot: that sector is not adjacent*"
		send "q"
		goto :GetCurrent
	end	
	end
end	
if ($photoninfo = "on")
	setVar $photontoggle 1
	send "'pbot: photons enabled.*"
end
if ($photoninfo = "off")
	setVar $photontoggle 0
	send "'pbot: photons disabled.*"
end
send "q"
goto :GetCurrent

#refresh Location
:LocalRefresh
send "N"
:RefreshLocation
KillAllTriggers
send "q"
goto :GetCurrent

# Deployed Fighter Test
:deployedfighters
KillAllTriggers
setVar $deployline CURRENTLINE
stripText $deployline ":"
getWord $deployline $deploysector 5
getWord $deployline $intrudername 6
striptext $intrudername "'s"
getWord $deployline $intruderstatus 7
goto :FindSolution

# Quasar Cannon
:QCannonFire
setVar $qcannonline CURRENTLINE
getWord $qcannonline $qcannonplanetname 4
getWord $qcannonline $intrudername 6
ECHO ANSI_7 "QCANNONPLANET:" $qcannonplanetname
ECHO ANSI_9 "CURRENTPLANET:" $currentplanetname
if ($qcannonplanetname = $currentplanetname)
	setVar $deploysector $currentsector	
	goto :FindSolution
else
	setTextLineTrigger QCannonFire :QCannonFire "Quasar Cannon on"
	goto :TriggerPause
end

# Deployed Limpet Mine
:deployedlimpet
killAllTriggers
setVar $deployline CURRENTLINE
getWord $deployline $deploysector 4
setVar $intrudername "Unknown"
setVar $intruderstatus "Limpet"
goto :FindSolution


# Find out what to do.
:FindSolution
if ($deploysector = $currentsector)
	#GET OUT
	send "p" $escapesector1 "*"
	setTextlineTrigger AllSystemsReady1 :Ready1 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind1     :CannotBlind1 "You cannot"
	setTextlineTrigger AlreadyThere3    :AlreadyThere3 "You are already in that sector"
	pause
	:Ready1
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: MANUAL ESCAPE MODE GET ON PLANET AND SEND GO!!*"
	setTextLineTrigger Go1 :Go1 ".go"
	pause
	:Go1
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone1
	:AlreadyThere3
		KillAllTriggers
		send "q"
		goto :GetCurrent
	:CannotBlind1
	KillAllTriggers
	send "p" $escapesector2 "*"
	setTextlineTrigger AllSystemsReady2 :Ready2 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind2     :CannotBlind2 "You cannot"
	setTextlineTrigger AlreadyThere4    :AlreadyThere4 "You are already in that sector"
	pause
	:Ready2
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: MANUAL ESCAPE MODE GET ON PLANET AND SEND GO!!*"
	setTextLineTrigger Go2 :Go2 ".go"
	pause
	:Go2
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone1	
	:AlreadyThere4
		KillAllTriggers
		send "q"
		goto :GetCurrent
	:CannotBlind2
	KillAllTriggers
	send "p" $escapesector3 "*"
	setTextlineTrigger AllSystemsReady3 :Ready3 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind3     :CannotBlind3 "You cannot"
	setTextlineTrigger AlreadyThere5    :AlreadyThere5 "You are already in that sector"
	pause	
	:Ready3
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: MANUAL ESCAPE MODE GET ON PLANET AND SEND GO!!*"
	setTextLineTrigger Go3 :Go3 ".go"
	pause
	:Go3
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone1
	:AlreadyThere5
		KillAllTriggers
		send "q"
		goto :GetCurrent
	:CannotBlind3
	# Warning All Escape Sectors for this Zone Missing Escape By Hand Warning/End
		send "'pbot: WARNING NO ESCAPE SECTOR FOUND CLEAR OUT BY HAND!*"
		setVar $pbotenabled 0
		send "'pbot: auto disable.*"
		goto :GetCurrent
	:SolutionDone1
	#exit cit and get current status.
	send "q"
	send "'pbot: relocaed zone after " $intrudername " entered sector*"
	goto :GetCurrent
else
	#FIND DISTANCE
	getdistance $distance $currentsector $deploysector
	if ($distance = 1)
	# Fire Photon and GET OUT
	if ($photontoggle = 1) 
	send "cpy" $deploysector "*q"
	end
	send "p" $escapesector1 "*"
	setTextlineTrigger AllSystemsReady4 :Ready4 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind4     :CannotBlind4 "You cannot"
	setTextlineTrigger AlreadyThere6    :AlreadyThere6 "You are already in that sector"
	pause
	:Ready4
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: MANUAL ESCAPE MODE GET ON PLANET AND SEND GO!!*"
	setTextLineTrigger Go4 :Go4 ".go"
	pause
	:Go4
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone2
	:AlreadyThere6
		KillAllTriggers
		send "q"
		goto :GetCurrent
	:CannotBlind4
	KillAllTriggers
	if ($photontoggle = 1) 
	send "cpy" $deploysector "*q"
	end
	send "p" $escapesector2 "*"
	setTextlineTrigger AllSystemsReady5 :Ready5 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind5     :CannotBlind5 "You cannot"
	setTextlineTrigger AlreadyThere7    :AlreadyThere7 "You are already in that sector"
	pause
	:Ready5
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: MANUAL ESCAPE MODE GET ON PLANET AND SEND GO!!*"
	setTextLineTrigger Go5 :Go5 ".go"
	pause
	:Go5
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone2	
	:AlreadyThere7
		KillAllTriggers
		send "q"
		goto :GetCurrent
	:CannotBlind5
	KillAllTriggers
	if ($photontoggle = 1) 
	send "cpy" $deploysector "*q"
	end
	send "p" $escapesector3 "*"
	setTextlineTrigger AllSystemsReady6 :Ready6 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind6     :CannotBlind6 "You cannot"
	setTextlineTrigger AlreadyThere8    :AlreadyThere8 "You are already in that sector"
	pause	
	:Ready6
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: MANUAL ESCAPE MODE GET ON PLANET AND SEND GO!!*"
	setTextLineTrigger Go6 :Go6 ".go"
	pause
	:Go6
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone2
	:AlreadyThere8
		KillAllTriggers
		send "q"
		goto :GetCurrent
	:CannotBlind6
	# Warning All Escape Sectors for this Zone Missing Escape By Hand Warning/End
		send "'pbot: WARNING NO ESCAPE SECTOR FOUND CLEAR OUT BY HAND!*"
		setVar $pbotenabled 0
		send "'pbot: Auto Disable*"
		goto :GetCurrent
	:SolutionDone2
	if ($photontoggle = 1) 
	send "'pbot: photoned " $intrudername " in sector " $deploysector " and relocated zone.*"
	else
	send "'pbot: relocated zone after " $intrudername " entered adjacent sector."
	end
	#Regain Lock on Old Current (Not Implemented yet.)
	#send "p" $currentsector "*"
	#setTextlineTrigger AllSystemsReady7 :Ready7 "Locating beam pinpointed"
	#setTextlineTrigger CannotBlind7     :CannotBlind7 "You cannot"
	#pause
	#:Ready7
	#KillAllTriggers
	#setVar $reganlock 1
	#
	#goto :ExitCit
	#:CannotBlind7
	#KillAllTriggers
	#exit cit and get current status.
	:ExitCit
	send "q"
	goto :GetCurrent

	else
		# Report Distance and Threat on SubSpace
		:SubSpaceReport	
		if ($distance <= $redalert)
			#RED ALERT!!
			send "'RED ALERT: " $intrudername " is " $distance " sectors away!!!*"
			goto :GetTriggers
			end

		if ($distance <= $orangealert)
			#ORANGE ALERT!!
			send "'ORANGE ALERT: " $intrudername " is " $distance " sectors away!!!*"	
			goto :GetTriggers
			end
	
		#YELLOW ALERT!!
			send "'YELLOW ALERT: " $intrudername " is " $distance " sectors away!!!*"
			goto :GetTriggers	

	end		
end


:PbotVersion
send "'[pbot version:" $pbotversioninfo "]*"
setTextLineTrigger IncomingCommand :IncomingCommand ".pbot"
goto :TriggerPause

:PbotHelp
send "'pbot cmds: [on, off, status, refresh, photon (on, off, #sector)]*"
send "'pbot cmds: [come, reload #amount, version, help, {.go}]*"
setTextLineTrigger IncomingCommand :IncomingCommand ".pbot"
goto :TriggerPause


:PbotStatus
	#GET ALL STATUS
	send "'[pbot:"
	if ($pbotenabled = 1) 
		send "ON" 
	else 
		send "OFF" 
	end
	send "|Photons:" 
	if ($photontoggle = 1) 
		send "ON" 
	else 
		send "OFF" 
	end
	send "|Turns:" $currentturns
	send "|Escape:"
	if ($manualescape = 1) 
		send "MANUAL" 
	else 
		send "AUTO" 
	end
	send "|Missiles:" $currentphotons "|Planet:" $currentplanetname "]*" 
	
setTextLineTrigger IncomingCommand :IncomingCommand ".pbot"
goto :TriggerPause


:BotToggleOff
killAllTriggers
setVar $pbotenabled 0
send "'pbot: disabled*"
send "q"
goto :GetCurrent

:BotToggleOn
killAllTriggers
setVar $pbotenabled 1
send "'pbot: enabled*"
send "q"
goto :GetCurrent

:StandBy
#Disconnected Standby On Reconnect and Login Script to put you back on Planet.
KillAllTriggers
waitFor "Citadel command"
send "q"
goto :GetCurrent


:END