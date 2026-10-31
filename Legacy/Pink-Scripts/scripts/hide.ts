# Exiled TWX Script: Planet Relocation Hide Script V. 0.01
# Author           : Pinky
# Description      : 
# Warnings         : 
# Other            :

# check if we can run it from here
cutText CURRENTLINE $location 1 14

if ($location <> "Planet command")
        clientMessage "This script must be run from the surface of a planet"
        halt
end

SetVar $manualescape 0
SetVar $redalert 5
SetVar $orangealert 10
SetVar $reganlock 0
SetVar $photontoggle 1
SetVar $botenabled 1

# Check Planet Number and Current Sector then Enter Cit.
:GetCurrent
send "*c"
setTextLineTrigger GetPlanetInfo :getplanetinfo "Planet #"
pause
:getplanetinfo
getWord CURRENTLINE $currentplanet 2
stripText $currentplanet "#"
getWord CURRENTLINE $currentsector 5
stripText $currentsector ":"
waitFor "Citadel command"

# Read Sector Info File Find Current
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
	if ($photontoggle = 1)
	send "'pbot: Photon Protecting Zone " $escapesectorzone "*"
	else
	send "'pbot: Protecting Zone " $escapesectorzone "*"
	end
	goto :GetTriggers
else
	add $pass 1
	if ($hidesector = 0)
	goto :ManualEscape
	end
	goto :ReadHideFile
end

# ManualEscape When Online and out in freespace.
:ManualEscape
read hidesect.txt $hideline 1
getWord $hideline $escapesector1 1
getWord $hideline $escapesector2 2
getWord $hideline $escapesector3 3
setVar $escapesectorzone "FreeSpace"
setVar $manualescape 1
	if ($photontoggle = 1)
	send "'pbot: Photon Protecting Zone " $escapesectorzone "*"
	else
	send "'pbot: Protecting Zone " $escapesectorzone "*"
	end
goto :GetTriggers

# Set Triggers and Wait
:GetTriggers
setTextLineTrigger DeployedFighters :deployedfighters "Deployed Fighters"
setTextLineTrigger RefreshLocation :RefreshLocation ".re" 
setTextLineTrigger RefreshLocation1 :RefreshLocation ".refresh"
setTextLineTrigger RefreshLocation2 :RefreshLocation ".pbot refresh"
setTextLineTrigger PhotonToggle :PhotonToggle ".photon"
setTextLineTrigger PhotonToggle2 :PhotonToggle ".pbot photon"
setTextLineTrigger BotToggleOff :BotToggleOff ".pbot off"
setTextLineTrigger BringBot :BringBot ".pbot come"
setTextLineTrigger SectorCheck :SectorCheck "Sector  :"
setEventTrigger StandBy :StandBy "Connection lost"
:TriggerPause
pause


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
isnumber $namecheck $userlocation
if ($namecheck = "TRUE")
	send "qp" $userlocation "*"
	setTextlineTrigger AllSystemsReady7 :Ready7 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind7     :CannotBlind7 "You cannot"
	pause
	:Ready7
	KillAllTriggers
	send "yq"
	goto :GetCurrent
	:CannotBlind7
	send "q"
	send "'pbot: Could Not Gain Lock on your Location.*"
	goto :GetCurrent	
else
	getWord $locationline $userlocation 3
	isnumber $namecheck2 $userlocation
	if ($namecheck2 = "TRUE")
		send "qp" $userlocation "*"
		setTextlineTrigger AllSystemsReady8 :Ready8 "Locating beam pinpointed"
		setTextlineTrigger CannotBlind8     :CannotBlind8 "You cannot"
		pause
		:Ready8
		KillAllTriggers
		send "yq"
		goto :GetCurrent
		:CannotBlind8
		send "q"
		send "'pbot: Could Not Gain Lock on your Location.*"
		goto :GetCurrent
	end
end
send "'HideBot Status: Error Getting Location*"
echo "LOCATION LINE: " $locationline
echo "NAMECHECK: " $namecheck
echo "NAMECHECK2: " $namecheck2
send "qq"
goto :GetCurrent
:BringBotNA
send "'pbot: Not On Corp!*"
killAllTriggers
send "qq"
goto :GetCurrent


#Photons on or off
:PhotonToggle
killAllTriggers
if ($photontoggle = 1)
	setVar $photontoggle 0
else
	setVar $photontoggle 1
end
send "q"
goto :GetCurrent

#refresh Location
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

# Deployed Limpet Mine
killAllTriggers
echo ANSI_4 "**[ DEPLOYED LIMPET MINES ][ Getting sector information! ]"
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
	pause
	:Ready1
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: Manual Escape Mode GET ON PLANET and send go!!*"
	setTextLineTrigger Go1 :Go1 ".go"
	pause
	:Go1
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone1
	:CannotBlind1
	KillAllTriggers
	send "p" $escapesector2 "*"
	setTextlineTrigger AllSystemsReady2 :Ready2 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind2     :CannotBlind2 "You cannot"
	pause
	:Ready2
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: Manual Escape Mode GET ON PLANET and send go!!*"
	setTextLineTrigger Go2 :Go2 ".go"
	pause
	:Go2
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone1	
	:CannotBlind2
	KillAllTriggers
	send "p" $escapesector3 "*"
	setTextlineTrigger AllSystemsReady3 :Ready3 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind3     :CannotBlind3 "You cannot"
	pause	
	:Ready3
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: Manual Escape Mode GET ON PLANET and send go!!*"
	setTextLineTrigger Go3 :Go3 ".go"
	pause
	:Go3
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone1
	:CannotBlind3
	# Warning All Escape Sectors for this Zone Missing Escape By Hand Warning/End
		send "'Warning All Escape Sectors for this Zone Missing Clear out by Hand!*"
		goto :END
	:SolutionDone1
	#exit cit and get current status.
	send "q"
	send "'pbot: " $intrudername " entered current sector. Relocated Zone*"
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
	pause
	:Ready4
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: Manual Escape Mode GET ON PLANET and send go!!*"
	setTextLineTrigger Go4 :Go4 ".go"
	pause
	:Go4
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone1
	:CannotBlind4
	KillAllTriggers
	if ($photontoggle = 1) 
	send "cpy" $deploysector "*q"
	end
	send "p" $escapesector2 "*"
	setTextlineTrigger AllSystemsReady5 :Ready5 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind5     :CannotBlind5 "You cannot"
	pause
	:Ready5
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: Manual Escape Mode GET ON PLANET and send go!!*"
	setTextLineTrigger Go5 :Go5 ".go"
	pause
	:Go5
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone1	
	:CannotBlind5
	KillAllTriggers
	if ($photontoggle = 1) 
	send "cpy" $deploysector "*q"
	end
	send "p" $escapesector3 "*"
	setTextlineTrigger AllSystemsReady6 :Ready6 "Locating beam pinpointed"
	setTextlineTrigger CannotBlind6     :CannotBlind6 "You cannot"
	pause	
	:Ready6
	KillAllTriggers
	if ($manualescape = 1)
	send "'pbot: Manual Escape Mode GET ON PLANET and send go!!*"
	setTextLineTrigger Go6 :Go6 ".go"
	pause
	:Go6
	KillAllTriggers
	send "y"
	else
	send "y"
	end
	goto :SolutionDone1
	:CannotBlind6
	# Warning All Escape Sectors for this Zone Missing Escape By Hand Warning/End
		send "'Warning All Escape Sectors for this Zone Missing Clear out by Hand!*"
		goto :END
	:SolutionDone1
	if ($photontoggle = 1) 
	send "'pbot: Photoned " $intrudername " in sector " $deploysector " and Relocated Zone.*"
	else
	send "'pbot: Relocated Zone"
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



:BotToggleOff
#Turn Bot off per Corp/Sub Request and wait for on again.
killAllTriggers
:BotToggleReOff
setTextLineTrigger BotToggleOn :BotToggleOn ".pbot on"
send "'pbot: Disabled*"
pause
:BotToggleOn
KillAllTriggers
send "q"
goto :GetCurrent


:StandBy
#Disconnected Standby On Reconnect and Login Script to put you back on Planet.
KillAllTriggers
waitFor "Citadel command"
send "q"
goto :GetCurrent


:END