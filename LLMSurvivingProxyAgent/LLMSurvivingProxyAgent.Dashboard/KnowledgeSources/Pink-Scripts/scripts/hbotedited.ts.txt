# Exiled TWX Script: planet/photon/hide/protect pbot script
# Author           : pinky
# Description      : script with many options
# Warnings         : still in testing don't blame me if ya get killed. -;)
# Other            : thanks to Hekate/Phoe6e for testing with me! -;)

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
setVar $pbotversioninfo "pbot v1.032303-beta by pink - edited by Phoe6e"
setVar $pbottriggercommand ".pbot"
setVar $pbotdisplayname "pbot"
setVar $pbotescapedistance 12
setVar $pbotminescapedistance 8
setVar $escapemaxwarps 1
setVar $minescapesectors 7
setVar $maxphotons 5
setVar $stardock 0
setVar $findescape 1 
SetVar $manualescape 0
SetVar $redalert 5
SetVar $orangealert 10
SetVar $reganlock 0
SetVar $photontoggle 1
SetVar $pbotenabled 1
# pbot mode: 1:AutoEscape, 2:DefensiveEscape 3:OffensiveEscape
# AutoEscape = Will Auto TWarp Planet to First Escape Sector It Gets Lock.
# DefensiveEscape = Will get Lock on Escape Sector and Wait on ".go/.pbot go"
# OffensiveEscape = Will Fire Photons into Ajacent Sectors on Limpet/Fig Hits With No Escape!
setVar $pbotmode 1
setVar $photoncost 40000
setVar $currentphotons 0
setVar $currentfuelore 0
setVar $cashamount 0
setVar $currentturns 0
setVar $usedescapepass 1
setVar $photonrefirerate 3001
setVar $minautoexploreturns 100
setVar $loaddatafiles 1
# pbot won't deploy over this amount.
setVar $maxdarmids 100
setVar $maxdlimpets 10
setVar $maxdfighters 500

# Build Information
:BuildInformation
send "'" $pbotdisplayname ": starting pbot.... be with you in a moment..*"
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

#Build Escape Sector Data! - Proccess Could Take Awhile.
:LoadEscapeSectorData
if ($loaddatafiles = 1)
setVar $fightercount 0
setVar $escapefigcount 0
fileexists $escapefileexists "escapes.txt"
if ($escapefileexists = "TRUE")
	setVar $findescape 0
	# Read Escape DataBase
	setVar $pass 1
	:readescapes
	read "escapes.txt" $escapefig_database[$pass] $pass
	#echo ANSI_12 "S:" $escapefig_database[$pass]
	if ($escapefig_database[$pass] = 0)
		subtract $escapefigcount 1
		goto :donereadingescapes
	end
	add $escapefigcount 1
	add $pass 1
	goto :readescapes
	:donereadingescapes
	setVar $pass 1
	:readfighters
	read "fighters.txt" $fighter_database[$pass] $pass
	#echo ANSI_15 "F:" $fighter_database[$pass]
	if ($fighter_database[$pass] = 0)
		subtract $fightercount 1
		goto :donereadingfighters
	end
	setVar $placedfighters[$fighter_database[$pass]] 1
	add $fightercount 1
	add $pass 1
	goto :readfighters
	:donereadingfighters
	setVar $loaddatafiles 0
else
	setVar $findescape 1
end
end
:BuildEscapeSectorData
if ($findescape = 1)
send "'" $pbotdisplayname ": i'm building an escape sector list, please standby*"
# TEST BUILD FIND FIG ESCAPE ZONES
setVar $fightercount 0
setVar $escapefigcount 0

send "qq"
waitFor "Command [TL="
send "sd"
waitFor "Command [TL="
send "g"
WaitFor "==="
:resetfigbuild
	killAllTriggers
	setTextLineTrigger corptag :AddFig "Corp"
	setTextTrigger prompt :FigBuildDone "Command [TL="
	pause
	
:Addfig
	add $fightercount 1
	getWord CURRENTLINE $fighter_database[$fightercount] 1
	write "fighters.txt" $fighter_database[$fightercount]
	setVar $placedfighters[$fighter_database[$fightercount]] 1
	goto :resetfigbuild

:FigBuildDone
	setVar $escapefigpass 1
	setVar $figfinderpass 1


send "l" $currentplanet "*cc"
waitfor "Citadel command"
echo ANSI_6 "**[ " $pbotdisplayname " ] counting sectors and finding escape zones, standby.*"
SetVar $pass 1
:FigFinderLoop
	#send "'" $pbotdisplayname " : building figed escape sector list pass, " $pass "*"
	if ($fightercount > $figfinderpass)
		getdistance $distance $currentsector $fighter_database[$figfinderpass]
		if ($distance > $pbotescapedistance)
			setVar $figescapesector $fighter_database[$figfinderpass]
			setVar $escapewarpspass 1
			setVar $alreadyescapesector 1
			:AlreadyEscapeSector
			        if ($escapefig_database[$alreadyescapesector] = $figescapesector)
					add $figfinderpass 1
					add $pass 1
					goto :FigFinderLoop
				end
				if ($alreadyescapesector < $escapefigcount)
					add $alreadyescapesector 1
					goto :AlreadyEscapeSector
				end
			 		   
	
			:FindWarps
	
				send "I" $figescapesector "*"
				setTextLineTrigger InterWarps :InterWarps "Sector"
				setTextLineTrigger UnexploredFig :UnexploredFig "You have never visited sector"
				pause
			#TESTING STILL MIGHT GET YOU KILLED!!!
			:UnexploredFig
				KillAllTriggers
				if ($currentturns > $minautoexploreturns) 
				send "q"
				# Photon Spender
				:UnloadPhotons
				if ($currentphotons > 0)
					# Unload Photons to Avoid Turn Loss.
					send "s"
					setTextLineTrigger UnloadPhotonSector :UnloadPhotonSector "Warps to Sector"
					pause
					:UnloadPhotonSector
					GetWord CURRENTLINE $unloadphotonto 5
					StripText $unloadphotonto ")"
					StripText $unloadphotonto "("
					send "cpy" $unloadphotonto "*q"
					send "'" $pbotdisplayname ": auto-unloading photons for auto-explore.*"
					subtract $currentphotons 1
					setDelayTrigger photonrefirerate :photonrefirerate $photonrefirerate
					pause
					:photonrefirerate
					KillAllTriggers
					goto :UnloadPhotons	
				end
				killAllTriggers
				send "qtnl3*tnl2*tnt1*c"
				waitFor "Citadel command"
				send "b" $figescapesector "*"
				setTextlineTrigger AutoExploreReady :AutoExploreReady "Locating beam pinpointed"
				setTextlineTrigger AutoExploreBlind :AutoExploreBlind "No locating beam found"
				pause
				:AutoExploreReady
				KillAllTriggers
				send "y"
				setTextTrigger AutoExploreMined :AutoExploreMined "Mined Sector: Do you wish"
				setTextTrigger AutoExploreNoMine :AutoExploreNoMine "Command [TL="
				pause			
				:AutoExploreMined
				KillAllTriggers
				send "y"
				:AutoExploreNoMine
				KillAllTriggers
				send "sdshm" $stardock "*y"
				setTextlineTrigger AutoExploreSReady :AutoExploreSReady "Locating beam pinpointed"
				setTextlineTrigger AutoExploreSBlind :AutoExploreSBlind "No locating beam found"			
				setTextlineTrigger NoFuelOreOnShipS :NoFuelOreOnShipA "You do not have enough"
				pause
				:AutoExploreSReady 
				KillAllTriggers
				send "y"
				waitFor "Command [TL="
				#at dock
				send "ps"
				setTextLineTrigger DockLimpetCheck :DockLimpetCheck "A port official runs up to you"
				setTextTrigger NoLimpetAtDock :NoLimpetAtDock "<StarDock>"
				pause
				:DockLimpetCheck
				KillAllTriggers
				send "y"
				:NoLimpetAtDock
				KillAllTriggers
				send "qm" $currentsector "*y"
				setTextlineTrigger AutoExploreHReady :AutoExploreHReady "Locating beam pinpointed"
				setTextlineTrigger AutoExploreHBlind :AutoExploreHBlind "No locating beam found"
				setTextlineTrigger NoFuelOreOnShipH :NoFuelOreOnShipA "You do not have enough"
				pause
				:AutoExploreHReady
				KillAllTriggers
				send "y"
				waitfor "Command [TL="
				send "l" $currentplanet "*cc"
				waitFor "Computer command"
				goto :FindWarps
				
				:AutoExploreBlind
					add $figfinderpass 1
					send "c"
					goto :FigFinderLoop
				:AutoExploreHBlind
					send "psg"
					send "'" $pbotdisplayname ": lost track of planet auto hiding on dock.*"	
				:AutoExploreSBlind
				:NoFuelOreOnShipA
					send "'" $pbotdisplayname ": i lost track of locating beam or ran out of fuel ore.*"
					HALT

				else
					add $figfinderpass 1
					add $pass 1
					goto :FigFinderLoop
				end
												
			:InterWarps
			KillAllTriggers
			setVar $warpinfoline CURRENTLINE
			striptext $warpinfoline ":"
			striptext $warpinfoline "-"
			striptext $warpinfoline "Sector"
			striptext $warpinfoline $figescapesector
			striptext $warpinfoline "has warps to sector(s)"
			getWord $warpinfoline $escapewarps[$escapewarpspass] $escapewarpspass
			if ($escapewarps[$escapewarpspass] > 0)
				ECHO ANSI_12 "*[ " $escapewarps[$escapewarpspass] " ]"
				add $escapewarpspass 1
				Goto :InterWarps		
			end
			subtract $escapewarpspass 1
			ECHO ANSI_5 "*MAX: " $escapemaxwarps " WARPS: " $escapewarpspass "*"
			if ($escapemaxwarps >= $escapewarpspass)	
			setVar $escapefig_database[$escapefigpass] $fighter_database[$figfinderpass]
			ECHO ANSI_12 "[Escape Sector Found]: " ANSI_15 $figescapesector ANSI_14 " - WARPS: " ANSI_15 $escapewarpspass "**"
			write "escapes.txt" $figescapesector
			add $escapefigcount 1
			add $escapefigpass 1
			add $figfinderpass 1
			add $pass 1
			goto :FigFinderLoop
			end
		end
		add $figfinderpass 1
		add $pass 1
		goto :FigFinderLoop
	end
if ($escapefigcount < $minescapesectors)
	setVar $figfinderpass 1
	if ($pbotescapedistance > $pbotminescapedistance)
		subtract $pbotescapedistance 1
	else
		setVar $pbotescapedistance 12
		if ($escapemaxwarps < 6)
			add $escapemaxwarps 1
		else
			setVar $escapemaxwarps 1
			subtract $pbotminescapedistance 1
		end
	end
	goto :FigFinderLoop
end

send "q"
# Escape Figs Found
write "escapes.txt" 0
write "fighters.txt" 0
setVar $findescape 0
KillAllTriggers
# TEST BUILD FIND FIG ESCAPE ZONES
end

# Display Environment Local
:DisplayEnvironment
echo ANSI_3 "**[STARDOCKLOCATION]: " ANSI_15 $stardock "*"
echo ANSI_3 "[Figged Sectors]: " ANSI_15 $fightercount "*"
echo ANSI_3 "[Escape Sectors]: " ANSI_9 $escapefigcount "*"
echo ANSI_3 "[CURRENTPLANET]: " ANSI_11 $currentplanetname " # "$currentplanet "*"
echo ANSI_3 "[CURRENTSECTOR]: " ANSI_11 $currentsector "*"
echo ANSI_3 "[CURRENTCREDITS]: " ANSI_4 $currentcredits "*"
echo ANSI_3 "[CURRENTPHOTONS]: " ANSI_4 $currentphotons "*"
echo ANSI_3 "[CURRENTTURNS]: " ANSI_4 $currentturns "*"
ECHO ANSI_3 "[CURRENTHOLDS]: " ANSI_6 $currentholds "*"
ECHO ANSI_3 "[CURRENTFUELORE]: " ANSI_6 $currentfuelore "**"
ECHO ANSI_3 "Escape information: *"
echo ANSI_7 "   Escape Sectors Found: " ANSI_8 $escapefigcount "*"
echo ANSI_7 "   Escape Sectors Used: " ANSI_8 $usedescapepass "*"
echo ANSI_7 "   Escape Sectors List: [" ANSI_8
setVar $figlocaldisplayloop 1
:FigLocalDisplayLoop
if ($escapefigcount >= $figlocaldisplayloop)
	echo " : " $escapefig_database[$figlocaldisplayloop]
	add $figlocaldisplayloop 1
	goto :FigLocalDisplayLoop
end
echo " :" ANSI_7 " ]**"

# Display pbot Status line on Subspace and goto get triggers.
:pbotInformationLine
if ($pbotenabled = 1)
	send "'" $pbotdisplayname ": "
	if ($photontoggle = 1)
	send "photon "
	end
	if ($pbotmode = 1)
	send "hiding planet (auto escape mode)*"
	end
	if ($pbotmode = 2)
	send "protecting planet (manual escape mode){.go}*"
	end 
	if ($pbotmode = 3)
	send "attacking from planet (offensive mode, auto escape mode)*"
	end
	goto :GetTriggers
else
	goto :BotOffTriggers
end

# Set Triggers and Wait
:GetTriggers
setTextLineTrigger DeployedFighters :deployedfighters "Deployed Fighters"
# Quasar Cannon Trigger.
setTextLineTrigger QCannonFire :QCannonFire "Quasar Cannon on"
# Limpet Hit Trigger.
setTextLineTrigger DeployedLimpetMine :DeployedLimpetMine "Limpet mine in"
setTextLineTrigger SectorCheck :SectorCheck "Sector  :"
setTextTrigger LocalRefresh :LocalRefresh "Do you wish to end your turn now and remain here?"
# Standby
:BotOffTriggers
setTextLineTrigger IncomingCommand :IncomingCommand $pbottriggercommand

setEventTrigger StandBy :StandBy "Connection lost"
# Pause
:TriggerPause
pause

# Incoming Command Processor 
:IncomingCommand
setVar $commandline CURRENTLINE
echo ANSI_3 "*INCOMING MESSAGE: " ANSI_4 $commandline
getWord $commandline $userverify 1

if ($userverify = "'.pbot")
  getWord $commandline $pbotuser 2
  	if ($pbotuser = "local")
	  echo ANSI_3 "*INCOMING USER: " ANSI_4 $pbotuser
	  setVar $CommandShift 9
	  getWord $commandline $pbotcommand 3
	else
	  echo ANSI_4 "*INCOMING USER: Unverified"
	  setVar $CommandShift 9
	  setVar $pbotcommand None
	end

elseif ($userverify = "R")
	getWord $commandline $pbotuser 2
   # User Access Control

	if ($pbotuser = "Pink") or ($pbotuser = "Racer") or ($pbotuser = "White") or ($pbotuser = "Corrup") or ($pbotuser = "Lewdpo")
	  echo ANSI_3 "*INCOMING USER: " ANSI_4 $pbotuser
	  setVar $CommandShift 1
	  getWord $commandline $pbotcommand 4
	else
	  echo ANSI_4 "*INCOMING USER: Unverified"
	  setVar $CommandShift 1
	  setVar $pbotcommand None
	end

elseif ($userverify = "F") or ($userverify = "P")
  echo ANSI_4 "*INCOMING USER: Unverified"
  setVar $CommandShift 1
  setVar $pbotcommand None

else
  echo ANSI_4 "*INCOMING USER: Unverified"
  setVar $CommandShift 1
  setVar $pbotcommand None
end

if ($pbotcommand = $pbottriggercommand)
	setVar $CommandShift 1
	getWord $commandline $pbotcommand 3
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

if ($pbotcommand = "rebuild")
	Goto :RebuildEscapeList
end

if ($pbotcommand = "status") or ($pbotcommand = "stat")
	Goto :pbotStatus
end

if ($pbotcommand = "help") or ($pbotcommand = "?")
	Goto :pbotHelp
end

if ($pbotcommand = "version") or ($pbotcommand = "ver")
	Goto :pbotVersion
end

if ($pbotcommand = "laugh") or ($pbotcommand = "giggle")
	Goto :pbothaha
end

if ($pbotcommand = "None")
	Goto :pbotNone
end

if ($pbotenabled = 1)
	if ($pbotcommand = "go")
		goto :getoutnow	
	end
	if ($pbotcommand = "list")
		Goto :SendUserEscapeList
	end

	if ($pbotcommand = "mode") or ($pbotcommand = "mo")
		if ($commandShift = 1)
			getWord $commandline $modechange 6
		else
			getWord $commandline $modechange 5
		end
		if ($CommandShift = 9)
			getWord $commandline $modechange 4
		end
		Goto :ChangeMode
	end

	if ($pbotcommand = "reload")
		if ($commandShift = 1)
			getWord $commandline $getphotons 6
			getWord $commandline $getarmids 7
			getWord $commandline $getlimpets 8
		else
			getWord $commandline $getphotons 5
			getWord $commandline $getarmids 6
			getWord $commandline $getlimpets 7
		end
		if ($CommandShift = 9)
			getWord $commandline $getphotons 4
			getWord $commandline $getarmids 5
			getWord $commandline $getlimpets 6
		end
		Goto :ReloadPhotons
	end

	if ($pbotcommand = "deploy") or ($pbotcommand = "holo")
		if ($commandShift = 1)
			getWord $commandline $darmids 6
			getWord $commandline $dlimpets 7
			getWord $commandline $dfighters 8
		else
			getWord $commandline $darmids 5
			getWord $commandline $dlimpets 6
			getWord $commandline $dfighters 7
		end
		if ($CommandShift = 9)
			getWord $commandline $darmids 4
			getWord $commandline $dlimpets 5
			getWord $commandline $dfighters 6
		end
		Goto :SectorDeploy
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
goto :BotTrigger

:SectorCheck
setVar $SectorCheckLine CURRENTLINE
getWord $SectorCheckLine $SectorCheckSector 3
settexttrigger minedsector :minedsector "Mined Sector: Do you wish"
settextlinetrigger pastminedsector :pastminedsector "Citadel treasury"
pause
:minedsector
send "n"
:pastminedsector
killtrigger pastminedsector
killtrigger minedsector
SetVar $currentsector $SectorCheckSector
setTextLineTrigger SectorCheck :SectorCheck "Sector  :"
goto :TriggerPause

:ReloadPhotons
isnumber $getphotonyes $getphotons
isnumber $getarmidsyes $getarmids
isnumber $getlimpetsyes $getlimpets
if ($getphotonyes = "TRUE")
	goto :YesPhoton	
end

:NoPhoton
send "'" $pbotdisplayname ": not a number.*"
	goto :BotTrigger

:YesPhoton
setVar $totalphotons $getphotons
add $totalphotons $currentphotons
if ($totalphotons > $maxphotons)
	send "'" $pbotdisplayname ": can't get " $getphotons " photons, max is " $maxphotons " and i have " $currentphotons " photons already.*"
	setTextLineTrigger IncomingCommand :IncomingCommand $pbottriggercommand
	goto :TriggerPause
end
setVar $totalphotoncost $photoncost
multiply $totalphotoncost $getphotons

if ($getphotonsyes = "FALSE")
	send "'" $pbotdisplayname ": invalid photon amount*"
	goto :BotTrigger
end
KillAllTriggers
#getcredits for photons
setVar $photontoggle 1
send "tf" $totalphotoncost "*"
if ($getarmidsyes = "TRUE")
	if ($getarmids > 0) and ($getarmids < 250)
	setVar $totalarmidcost 1000
	multiply $totalarmidcost $getarmids
	send "tf" $totalarmidcost "*"
	end
end
if ($getlimpetsyes = "TRUE")
	if ($getlimpets > 0) and ($getlimpets < 250)
	setVar $totallimpetcost 10000
	multiply $totallimpetcost $getlimpets 
	send "tf" $totallimpetcost "*"
	end
end
send "b" $stardock "*"
setTextlineTrigger StardockReady :StardockReady "Locating beam pinpointed"
setTextlineTrigger StardockBlind :StardockBlind "No locating beam found"
pause
:StardockReady
killalltriggers
send "ypsh"
send "p" $getphotons "*"
if ($getarmidsyes = "TRUE")
	if ($getarmids > 0) and ($getarmids < 250)
	send "m"
	setTextTrigger maxarmids :maxarmids "How many mines do you want"
	pause
	:maxarmids
	setVar $maxmineline CURRENTLINE
	striptext $maxmineline ")"
	getWord $maxmineline $maxmines 8
	if ($maxmines < $getarmids) and ($maxmines > 0)
	 	send $maxmines
	else
		send $getarmids
	end
	send "*"
	end
end
if ($getlimpetsyes = "TRUE")
	if ($getlimpets > 0) and ($getlimpets < 250)
	send "l"
	setTextTrigger maxlimpets :maxlimpets "How many mines do you want"
	pause
	:maxlimpets
	setVar $maxmineline CURRENTLINE
	striptext $maxmineline ")"
	getWord $maxmineline $maxmines 8
	if ($maxmines < $getarmids) and ($maxmines > 0)
	 	send $maxmines
	else
		send $getlimpets
	end
	send "*"
	end
end
send "qq"
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
	send "'" $pbotdisplayname ": Script Ending Can't Gain Lock on Planet Location. I'll hide on dock.*"
	send "psg"
	goto :END

:NoFuelOreOnShip
	KillAllTriggers
	send "'" $pbotdisplayname ": Script Ending Not Enough Fuel Ore to Return. I'll hide on dock.*"
	send "psg"
	goto :END

:StarDockBlind
	KillAllTriggers
	send "n"
	send "'" $pbotdisplayname ": I don't have the ability to warp to stardock.*"
	goto :PhotonExitPoint


:PhotonExitPoint
send "q"
goto :GetCurrent


:BringBot
KillAllTriggers
send "xa"
setTextLineTrigger BringBotXA :BringBotXA $pbotuser
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
	setTextlineTrigger AllSystemsReadyBring :ReadyBring "Locating beam pinpointed"
	setTextlineTrigger CannotBlindBring     :CannotBlindBring "You cannot"
	setTextlineTrigger AlreadyThereBring    :AlreadyThereBring "You are already in that sector"
	pause
	:ReadyBring
	KillAllTriggers
	send "yq"
	goto :GetCurrent
	:CannotBlindBring
	send "q"
	send "'" $pbotdisplayname ": could not gain lock on your location.*"
	goto :GetCurrent	
	:AlreadyThereBring
	KillAllTriggers
	send "q"
	send "'" $pbotdisplayname ": i'm already here!*"
	goto :GetCurrent
else
	getWord $locationline $userlocation 3
	isnumber $namecheck2 $userlocation
	if ($namecheck2 = "TRUE")
		send "qp" $userlocation "*"
		setTextlineTrigger AllSystemsReadyBring2 :ReadyBring2 "Locating beam pinpointed"
		setTextlineTrigger CannotBlindBring2     :CannotBlindBring2 "You cannot"
		setTextlineTrigger AlreadyThereBring2    :AlreadyThereBring2 "You are already in that sector"
		pause
		:ReadyBring2
		KillAllTriggers
		send "yq"
		goto :GetCurrent
		:CannotBlindBring2
		send "q"
		send "'" $pbotdisplayname ": could not gain lock on your location.*"
		goto :GetCurrent
		:AlreadyThereBring2
		KillAllTriggers
		send "q"
		send "'" $pbotdisplayname ": i'm already here!*"
		goto :GetCurrent
	end
end
send "'" $pbotdisplayname ": error getting your location*"
send "qq"
goto :GetCurrent
:BringBotNA
send "'" $pbotdisplayname ": your not on the corp!*"
killAllTriggers
send "qq"
goto :GetCurrent

# Sector Deploy 
:SectorDeploy
killAllTriggers
isnumber $deployarmids $darmids
isnumber $deploylimpets $dlimpets
isnumber $deployfighters $dfighters
ECHO ANSI_12 " Deploy armids: " ANSI_4 $darmids
ECHO ANSI_12 " Deploy limpets: " ANSI_4 $dlimpets
ECHO ANSI_12 " Deploy Fighters: " ANSI_4 $dfighters
send "qqsdsh"
if ($deployarmids = "TRUE") 
	if ($darmids > 0) and ($darmids <= $maxdarmids)
	send "h1t" $darmids "*cq*"
	end
end
if ($deploylimpets = "TRUE")
	if ($dlimpets > 0) and ($dlimpets <= $maxdlimpets)
	send "h2t" $dlimpets "*cq*"
	end
end
if ($deployfighters = "TRUE") 
	if ($dfighters > 0) and ($dfighters <= $maxdfighters)
	send "f" $dfighters "*cd"
	end
end
send "l" $currentplanet "*c"
waitFor "Citadel command"
send "q"
Goto :GetCurrent

#pbotnone Unauthorized Command Sender
:pbotNone
echo ANSI_4 "*Unauthorized Command*"
goto :BotTrigger

#Silly Bot
:pbothaha
KillAllTriggers
send "'hehehe I'm a blonde yeah yeah yeah**"
goto :BotTrigger

# Photons on/off FIRE!
:PhotonToggle
killAllTriggers
isnumber $sendphoton $photoninfo
if ($sendphoton = "TRUE")
	if ($photoninfo > 0)
	getdistance $distance $currentsector $photoninfo
	if ($distance = 1)
		send "cpy" $photoninfo "*q"
		send "'" $pbotdisplayname ": photon fired!*"
		send "q"
		goto :GetCurrent
	else
		send "'" $pbotdisplayname ": that sector is not adjacent*"
		send "q"
		goto :GetCurrent
	end	
	end
end	
if ($photoninfo = "on")
	setVar $photontoggle 1
	send "'" $pbotdisplayname ": photons enabled.*"
end
if ($photoninfo = "off")
	setVar $photontoggle 0
	send "'" $pbotdisplayname ": photons disabled.*"
end
send "q"
goto :GetCurrent

#refresh Location
:LocalRefresh
KillAllTriggers
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
getWordPos $deployline $retreated "retreated."
getWordPos $deployline $attacking "attacking!"
if ($retreated > 0) or ($attacking > 0)
	goto :GetTriggers
end
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
:DeployedLimpetMine
killAllTriggers
setVar $deployline CURRENTLINE
getWord $deployline $deploysector 4
setVar $intrudername "Unknown"
setVar $intruderstatus "Limpet"
goto :FindSolution


# Find out what to do.
:FindSolution
KillAllTriggers
if ($deploysector = $currentsector)
		goto :getoutnow
	else 
	getdistance $distance $currentsector $deploysector
		if ($distance = 1)
			if ($photontoggle = 1) 
				# PHOTON NOW !
				send "cpy" $deploysector "*q"
			end
			goto :getoutnow

		else
		# Report Distance and Threat on SubSpace
		:SubSpaceReport	
		if ($distance <= $redalert)
			if ($pbotmode = 3) and ($photontoggle = 1)
				getSector $deploysector $offensivesector
				setVar $offensivepass 1
				:attack
				if ($offensivesector.warp[$offensivepass] = 0)
					goto :GetTriggers
				end
				if ($placedfighters[$offensivesector.warp[$offensivepass]] = 1)
					send "p" $offensivesector.warp[$offensivepass] "*ycpy" $deploysector "*q"
					setTextTrigger offensivelaunch :offensivelaunch "Photon Missile launched into sector"
					setTextTrigger offensivemisfire :offensivemisfire "That is not an adjacent sector"
					pause
					:offensivemisfire
						killalltriggers
						send "q"
						setVar $placedfighters[$offensivesector.warp[$offensivepass]] 0
						goto :GetTriggers
					:offensivelaunch
						killalltriggers
						subtract $currentphotons 1
						send "p" $currentsector "*y"
						send "'" $pbotdisplayname ": photoned " $intrudername " in " $deploysector " from " $offensivesector.warp[$offensivepass] ".*"
						if ($currentphotons = 0)
							send "q"
							goto :GetCurrent
						else
							goto :GetTriggers
						end

				else
					add $offensivepass 1
					goto :attack
				end
			else
				#RED ALERT!!
				send "'RED ALERT: " $intrudername " is " $distance " sectors away!!!*"
				goto :GetTriggers
			end
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
# Get Out Now
:getoutnow
send "p" $escapefig_database[$usedescapepass] "*" 
settextlinetrigger escapeready :escapeready "Locating beam pinpointed"
settextlinetrigger connotblind :lostescape "You cannot"
settextlinetrigger alreadythere :alreadythere "You are already in that sector"
pause
:escapeready
killalltriggers
if ($pbotmode = 1) or ($pbotmode = 3)
	send "y"
end
if ($pbotmode = 2)
	send "'" $pbotdisplayname ": escape sector locked send go command!!*"
	setTextLineTrigger photdontgo :dontgo $pbottriggercommand&" abort"
	setTextLineTrigger pbotgo :go $pbottriggercommand&" go"
	setTextLineTrigger go :go ".go"
	pause
	:go
	killalltriggers
	send "y"
	goto :pastgo
	:dontgo
	send "nq"
	goto :GetCurrent
	:pastgo
end
add $usedescapepass 1
send "q"
goto :GetCurrent

:lostescape
	killalltriggers
	add $usedescapepass 1
	goto :FindSolution

:alreadythere
	killalltriggers
	send "q"
	goto :GetCurrent


:pbotVersion
send "'[pbot version:" $pbotversioninfo "]*"
goto :BotTrigger

:pbotHelp
send "'pbot cmds: [on, off, status, re (refresh), photon X (on, off, #sector)]*"
send "'pbot cmds: [come, list (escape sectors), rebuild, version, help, {.go}]*"
send "'pbot cmds: [ reload # # # (photons, armids, limpets) [reloads at SD   ]*"
send "'pbot cmds: [ deploy # # # (armids, limpets, fighters)[lift,holo,deploy]*"
send "'pbot cmds: [   modes 1)Auto Escape  2)Defensive Escape  3)Offensive   ]*"
goto :BotTrigger

:pbotStatus
	#GET ALL STATUS
	send "'[" $pbotdisplayname ":"
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
	send "|Mode:"
	if ($pbotmode = 1) 
		send "Escape" 
	end
	if ($pbotmode = 2)
		send "Defensive" 
	end
	if ($pbotmode = 3)
		send "Offensive"
	end
	send "|Missiles:" $currentphotons "|Planet:" $currentplanet "]*" 
	
goto :BotTrigger


:SendUserEscapeList
killalltriggers
send "xaq"
setTextLineTrigger SendEscapeListXA :SendEscapeListXA $pbotuser
setTextLineTrigger SendEscapeListNA :SendEscapeListNA "P indicates Trader"
pause
:SendEscapeListXA
KillAllTriggers
SetVar $figdisplayloop 1
send "=" $pbotuser "*y"
waitFor "Type private message"
send $pbotdisplayname ": escape sectors ["
:FigUserDisplayLoop
if ($escapefigcount > $figdisplayloop)
	send " :" $escapefig_database[$figdisplayloop]
	add $figdisplayloop 1
	goto :FigUserDisplayLoop
end
send ": ]**"
goto :GetTriggers
:SendEscapeListNA
KillAllTriggers
send "'" $pbotdisplayname ": your not on the corp.*"
goto :GetTriggers


:ChangeMode
isnumber $numentry $modechange
if ($numentry = "TRUE")
	if ($modechange = 1)
		goto :AutoEscapeBotOn
	end
	if ($modechange = 2)
		goto :DefensiveBotOn
	end
	if ($modechange = 3)
		goto :OffensiveBotON
	end	
        send "'" $pbotdisplayname ": invalid mode option.*"
	killalltriggers
	goto :GetTriggers
else
	if ($modechange = "auto") or ($modechange = "escape")
		goto :AutoEscapeBotOn
	end
	if ($modechange = "defensive") or ($modechange = "def")
		goto :DefensiveBotOn
	end
	if ($modechange = "offensive") or ($modechange = "off")
		goto :OffensiveBotON
	end	
        send "'" $pbotdisplayname ": invalid mode option.*"
	killalltriggers
	goto :GetTriggers	
end

# Bot Command Continue without refresh
:BotTrigger
setTextLineTrigger IncomingCommand :IncomingCommand $pbottriggercommand
goto :TriggerPause

:BotToggleOff
killAllTriggers
setVar $pbotenabled 0
send "'" $pbotdisplayname ": counting electric sheep.*"
send "q"
goto :GetCurrent

:BotToggleOn
killAllTriggers
setVar $pbotenabled 1
send "'" $pbotdisplayname ": my electrons glow.*"
send "q"
goto :GetCurrent

:AutoEscapeBotON
killAllTriggers
setVar $pbotmode 1
send "'" $pbotdisplayname ": i wont let you down.*"
send "q"
goto :GetCurrent

:DefensiveBotON
killAlltriggers
setVar $pbotmode 2
send "'" $pbotdisplayname ": just give me the word!*"
send "q"
goto :GetCurrent

:OffensiveBotOn
killAllTriggers
setVar $pbotmode 3
send "'" $pbotdisplayname ": lets toast em boss. -;)*"
send "q"
goto :GetCurrent

:RebuildEscapeList
killAllTriggers
setVar $loaddatafiles 1
delete "escapes.txt"
delete "fighters.txt"
send "q"
goto :GetCurrent

:StandBy
#Disconnected Standby On Reconnect and Login Script to put you back on Planet.
KillAllTriggers
waitFor "Citadel command"
send "q"
goto :GetCurrent


:END