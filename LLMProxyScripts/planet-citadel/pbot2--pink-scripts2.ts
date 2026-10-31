# Exiled TWX Script: planet/photon/hide/protect pbot script
# Author           : pinky
# Description      : script with many options
# Warnings         : still in testing don't blame me if ya get killed. -;)
# Other            : thanks to Phoe6e for testing with me! -;)

setVar $PBot_VersionInfo "pbot v2.1beta by pink 06/08/03"

# check if we can run it from here
cutText CURRENTLINE $locationline 1 14
getWord $locationline $location 1

if ($location <> "Planet") and ($location <> "Citadel")
        clientMessage "This script must be run from the surface of a planet"
        halt
end

# setup environment
:SetupEnironment
# pbot mode: 1:AutoEscape, 2:DefensiveEscape 3:OffensiveEscape
# AutoEscape = Will Auto TWarp Planet to First Escape Sector It Gets Lock.
# DefensiveEscape = Will get Lock on Escape Sector and Wait on ".go/.pbot go"
# OffensiveEscape = Will Fire Photons into Ajacent Sectors on Red Alert Limpet/Fig Hits then Escape.
# - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -
setArray $PBot_Escapefig_Database SECTORS
setArray $PBot_Fighter_Database SECTORS

setVar $PBot_CurrentPhotons 0
setVar $PBot_CurrentFuelOre 0
setVar $PBot_CashAmount 0
setVar $PBot_CurrentTurns 0
setVar $PBot_LoadDataFiles 1
setVar $PBot_FindEscape 1

loadVar $PBotSaved

if ($PBotSaved)
	loadVar $PBot_TriggerCommand
	loadVar $PBot_DisplayName
	loadVar $PBot_AccessList1
	loadVar $PBot_AccessList2
	loadVar $PBot_AccessList3
	loadVar $PBot_AccessList4
	loadVar $PBot_AccessList5
	loadVar $PBot_AccessList6
	loadVar $PBot_AccessList7
	loadVar $PBot_AccessList8
	loadVar $PBot_AccessList9
	loadVar $PBot_AccessList10
	loadVar $PBot_EscapeDistance
	loadVar $PBot_MinEscapeDistance
	loadVar $PBot_EscapeMaxWarps
	loadVar $PBot_MinEscapeSectors
	loadVar $PBot_MaxPhotons
	loadVar $PBot_StarDock
	loadVar $PBot_RedAlertDistance
	loadVar $PBot_orangeAlertDistance
	loadVar $PBot_PhotonToggle
	loadVar $PBot_BotEnabled
	loadVar $PBot_BotMode
	loadVar $PBot_PhotonCost
	loadVar $PBot_UsedEscapePass
	loadVar $PBot_PhotonRefireRate
	loadVar $PBot_MinAutoExploreTurns
	loadVar $PBot_MaxDeployArmids
	loadVar $PBot_MaxDeployLimpets
	loadVar $PBot_MaxDeployFighters
else

	setVar $PBot_TriggerCommand ".pbot"
	setVar $PBot_DisplayName "pbot"
	setVar $PBot_AccessList1 "[N/A]"
	setVar $PBot_AccessList2 "[N/A]"
	setVar $PBot_AccessList3 "[N/A]"
	setVar $PBot_AccessList4 "[N/A]"
	setVar $PBot_AccessList5 "[N/A]"
	setVar $PBot_AccessList6 "[N/A]"
	setVar $PBot_AccessList7 "[N/A]"
	setVar $PBot_AccessList8 "[N/A]"
	setVar $PBot_AccessList9 "[N/A]"
	setVar $PBot_AccessList10 "[N/A]"
	setVar $PBot_EscapeDistance 12
	setVar $PBot_MinEscapeDistance 8
	setVar $PBot_EscapeMaxWarps 1
	setVar $PBot_MinEscapeSectors 7
	setVar $PBot_MaxPhotons 5
	setVar $PBot_RedAlertDistance 5
	setVar $PBot_orangeAlertDistance 10
	setVar $PBot_PhotonToggle 1
	setVar $PBot_BotEnabled 1
	setVar $PBot_BotMode 1
	setVar $PBot_PhotonCost 40000
	setVar $PBot_UsedEscapePass 1
	setVar $PBot_PhotonRefireRate 3001
	setVar $PBot_MinAutoExploreTurns 100
	setVar $PBot_MaxDeployArmids 100
	setVar $PBot_MaxDeployLimpets 10
	setVar $PBot_MaxDeployFighters 500
	setVar $PBot_StarDock 0

	saveVar $PBot_TriggerCommand
	saveVar $PBot_DisplayName
	saveVar $PBot_AccessList1
	saveVar $PBot_AccessList2
	saveVar $PBot_AccessList3
	saveVar $PBot_AccessList4
	saveVar $PBot_AccessList5
	saveVar $PBot_AccessList6
	saveVar $PBot_AccessList7
	saveVar $PBot_AccessList8
	saveVar $PBot_AccessList9
	saveVar $PBot_AccessList10
	saveVar $PBot_EscapeDistance
	saveVar $PBot_MinEscapeDistance
	saveVar $PBot_EscapeMaxWarps
	saveVar $PBot_MinEscapeSectors
	saveVar $PBot_MaxPhotons
	saveVar $PBot_RedAlertDistance
	saveVar $PBot_orangeAlertDistance
	saveVar $PBot_PhotonToggle
	saveVar $PBot_BotEnabled
	saveVar $PBot_BotMode
	saveVar $PBot_PhotonCost
	saveVar $PBot_UsedEscapePass
	saveVar $PBot_PhotonRefireRate
	saveVar $PBot_MinAutoExploreTurns
	saveVar $PBot_MaxDeployArmids
	saveVar $PBot_MaxDeployLimpets
	saveVar $PBot_MaxDeployFighters

	setVar $PBotSaved 1
	saveVar $PBotSaved
end

addMenu "" "PBot" "PBot Settings" "." "" "PBot" FALSE
addMenu "PBot" "Run" "Run Script" "Z" :Menu_Run "" TRUE
addMenu "PBot" "BotTriggerCommand" "Bot Trigger Command:" "A" :Menu_BotTriggerCommand "" FALSE
addMenu "PBot" "BotDisplayName" "Bot Display Name:" "B" :Menu_BotDisplayName "" FALSE
addMenu "PBot" "BotMaxPhotons" "Bot Max Photons" "C" :Menu_BotMaxPhotons "" FALSE
addMenu "PBot" "BotPhotonToggle" "Bot Photon Toggle:" "D" :Menu_BotPhotonToggle "" FALSE
addMenu "PBot" "BotEnabled" "Bot Enabled:" "E" :Menu_BotEnabled "" FALSE
addMenu "PBot" "BotMode" "Bot Escape Mode:" "F" :Menu_BotMode "" FALSE
addMenu "PBot" "PhotonCost" "Photon Cost:" "G" :Menu_PhotonCost "" FALSE
addMenu "PBot" "PhotonRefireRate" "Photon Refire Rate:" "H" :Menu_PhotonRefireRate "" FALSE
addMenu "PBot" "MinAutoExploreTurns" "MinAutoExploreTurns:" "I" :Menu_MinAutoExploreTurns "" FALSE
addMenu "PBot" "BotSecurity" "Security Settings" "J" "" "PBot" FALSE
addMenu "BotSecurity" "AccessList1" "Bot Access:" "1" :Menu_BotAccess1 "" FALSE
addMenu "BotSecurity" "AccessList2" "Bot Access:" "2" :Menu_BotAccess2 "" FALSE
addMenu "BotSecurity" "AccessList3" "Bot Access:" "3" :Menu_BotAccess3 "" FALSE
addMenu "BotSecurity" "AccessList4" "Bot Access:" "4" :Menu_BotAccess4 "" FALSE
addMenu "BotSecurity" "AccessList5" "Bot Access:" "5" :Menu_BotAccess5 "" FALSE
addMenu "BotSecurity" "AccessList6" "Bot Access:" "6" :Menu_BotAccess6 "" FALSE
addMenu "BotSecurity" "AccessList7" "Bot Access:" "7" :Menu_BotAccess7 "" FALSE
addMenu "BotSecurity" "AccessList8" "Bot Access:" "8" :Menu_BotAccess8 "" FALSE
addMenu "BotSecurity" "AccessList9" "Bot Access:" "9" :Menu_BotAccess9 "" FALSE
addMenu "BotSecurity" "AccessList10" "Bot Access:" "0" :Menu_BotAccess10 "" FALSE
addMenu "PBot" "BotEscape" "Escape Settings" "K" "" "PBot" FALSE
addMenu "BotEscape" "EscapeDistance" "Escape Distance:" "D" :Menu_EscapeDistance "" FALSE
addMenu "BotEscape" "MinEscapeDistance" "Min Escape Distance:" "E" :Menu_MinEscapeDistance "" FALSE
addMenu "BotEscape" "EscapeMaxWarps" "Escape Max Warps:" "M" :Menu_EscapeMaxWarps "" FALSE
addMenu "BotEscape" "MinEscapeSectors" "Min Escape Sectors:" "S" :Menu_MinEscapeSectors "" FALSE
addMenu "PBot" "BotWarningSettings" "Warning Settings" "L" "" "PBot" FALSE
addMenu "BotWarningSettings" "RedAlertDistance" "Red Alert Distance:" "R" :Menu_RedAlertDistance "" FALSE
addMenu "BotWarningSettings" "OrangeAlertDistance" "Orange Alert Distance:" "O" :Menu_OrangeAlertDistance "" FALSE
addMenu "PBot" "BotDeploySettings" "Deploy Settings" "M" "" "PBot" FALSE
addMenu "BotDeploySettings" "MaxDeployLimpets" "Max Deploy Limpets:" "L" :Menu_MaxDeployLimpets "" FALSE
addMenu "BotDeploySettings" "MaxDeployArmids" "Max Deploy Armids:" "A" :Menu_MaxDeployArmids "" FALSE
addMenu "BotDeploySettings" "MaxDeployFighters" "Max Deploy Fighters:" "F" :Menu_MaxDeployFighters "" FALSE

gosub :sub_setBotMenu
openMenu "PBot"

:sub_setBotMenu
	setMenuValue "BotTriggerCommand" $PBot_TriggerCommand
	setMenuValue "BotDisplayName" $PBot_DisplayName
	setMenuValue "AccessList1" $PBot_AccessList1
	setMenuValue "AccessList2" $PBot_AccessList2
	setMenuValue "AccessList3" $PBot_AccessList3
	setMenuValue "AccessList4" $PBot_AccessList4
	setMenuValue "AccessList5" $PBot_AccessList5
	setMenuValue "AccessList6" $PBot_AccessList6
	setMenuValue "AccessList7" $PBot_AccessList7
	setMenuValue "AccessList8" $PBot_AccessList8
	setMenuValue "AccessList9" $PBot_AccessList9
	setMenuValue "AccessList10" $PBot_AccessList10
	setMenuValue "EscapeDistance" $PBot_EscapeDistance
	setMenuValue "MinEscapeDistance" $PBot_MinEscapeDistance
	setMenuValue "EscapeMaxWarps" $PBot_EscapeMaxWarps
	setMenuValue "MinEscapeSectors" $PBot_MinEscapeSectors
	setMenuValue "BotMaxPhotons" $PBot_MaxPhotons
	setMenuValue "RedAlertDistance" $PBot_RedAlertDistance
	setMenuValue "OrangeAlertDistance" $PBot_orangeAlertDistance
	setMenuValue "BotPhotonToggle" $PBot_PhotonToggle
	setMenuValue "BotEnabled" $PBot_BotEnabled
	setMenuValue "BotMode" $PBot_BotMode
	setMenuValue "PhotonCost" $PBot_PhotonCost
	setMenuValue "PhotonRefireRate" $PBot_PhotonRefireRate
	setMenuValue "MinAutoExploreTurns" $PBot_MinAutoExploreTurns
	setMenuValue "MaxDeployArmids" $PBot_MaxDeployArmids
	setMenuValue "MaxDeployLimpets" $PBot_MaxDeployLimpets
	setMenuValue "MaxDeployFighters" $PBot_MaxDeployFighters
	gosub :PHeader
return

:PHeader
ECHO ANSI_2 "*----------------------------------*"
ECHO ANSI_10 " " $PBot_VersionInfo " "
ECHO ANSI_2 "*----------------------------------*"
return

:Menu_BotTriggerCommand
getInput $PBot_TriggerCommand "Enter The Bot Trigger Command:"
saveVar $PBot_TriggerCommand
gosub :sub_SetBotMenu
openMenu "PBot"

:Menu_BotDisplayName
getInput $PBot_DisplayName "Enter the Display Name for Bot:" 
saveVar $PBot_DisplayName
gosub :sub_SetBotMenu
openMenu "PBot"

:Menu_BotMaxPhotons
getInput $PBot_MaxPhotons "Enter the Max Photons for Bot:" 
saveVar $PBot_MaxPhotons
gosub :sub_SetBotMenu
openMenu "PBot"

:Menu_BotPhotonToggle
getInput $PBot_PhotonToggle "Enter Photon Toggle Switch (1=On 2=Off):" 
saveVar $PBot_PhotonToggle
gosub :sub_SetBotMenu
openMenu "PBot"

:Menu_BotEnabled
getInput $PBot_BotEnabled "Enter Bot Enable Toggle (1=On 2=Off):" 
saveVar $PBot_BotEnabled
gosub :sub_SetBotMenu
openMenu "PBot"

:Menu_BotMode
getInput $PBot_BotMode "Enter Bot Mode (1=AutoEscape 2=Defensive 3=Offensive):" 
saveVar $PBot_BotMode
gosub :sub_SetBotMenu
openMenu "PBot"

:Menu_PhotonCost
getInput $PBot_PhotonCost "Enter Photon Cost:" 
saveVar $PBot_PhotonCost
gosub :sub_SetBotMenu
openMenu "PBot"

:Menu_PhotonRefireRate
getInput $PBot_PhotonRefireRate "Enter Photon Refire Rate (1000=1sec):"
saveVar $PBot_PhotonRefireRate
gosub :sub_SetBotMenu
openMenu "PBot"

:Menu_MinAutoExploreTurns
getInput $PBot_MinAutoExploreTurns "Enter Min AutoExplore Turns:" 
saveVar $PBot_MinAutoExploreTurns
gosub :sub_SetBotMenu
openMenu "PBot"

:Menu_RedAlertDistance
getInput $PBot_RedAlertDistance "Enter Red Alert Distance:" 
saveVar $PBot_RedAlertDistance
gosub :sub_SetBotMenu
openMenu "BotWarningSettings"

:Menu_OrangeAlertDistance
getInput $PBot_OrangeAlertDistance "Enter Red Alert Distance:" 
saveVar $PBot_OrangeAlertDistance
gosub :sub_SetBotMenu
openMenu "BotWarningSettings"

:Menu_MaxDeployArmids 
getInput $PBot_MaxDeployArmids "Enter Max Armid Mines Bot will Deploy:" 
saveVar $PBot_MaxDeployArmids
gosub :sub_SetBotMenu
openMenu "BotDeploySettings"

:Menu_MaxDeployLimpets
getInput $PBot_MaxDeployLimpets "Enter Max Limpet Mines Bot will Deploy:" 
saveVar $PBot_MaxDeployLimpets
gosub :sub_SetBotMenu
openMenu "BotDeploySettings"

:Menu_MaxDeployFighters
getInput $PBot_MaxDeployFighters "Enter Max Fighters Bot will Deploy:" 
saveVar $PBot_MaxDeployFighters
gosub :sub_SetBotMenu
openMenu "BotDeploySettings"

:Menu_EscapeDistance 
getInput $PBot_EscapeDistance "Enter Max Escape Distance:" 
saveVar $PBot_EscapeDistance
gosub :sub_SetBotMenu
openMenu "BotEscape"

:Menu_MinEscapeDistance 
getInput $PBot_MinEscapeDistance "Enter Min Escape Distance:" 
saveVar $PBot_MinEscapeDistance
gosub :sub_SetBotMenu
openMenu "BotEscape"

:Menu_EscapeMaxWarps 
getInput $PBot_EscapeMaxWarps "Enter Max Warps in Escape Sectors:" 
saveVar $PBot_EscapeMaxWarps
gosub :sub_SetBotMenu
openMenu "BotEscape"

:Menu_MinEscapeSectors
getInput $PBot_MinEscapeSectors "Enter Min Escape Sectors:" 
saveVar $PBot_MinEscapeSectors
gosub :sub_SetBotMenu
openMenu "BotEscape"

:Menu_BotAccess1
getInput $Pbot_AccessList1 "Enter the name of user (case matters 6 chars)" 
saveVar $PBot_AccessList1
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_BotAccess2
getInput $Pbot_AccessList2 "Enter the name of user (case matters 6 chars)"
saveVar $PBot_AccessList2
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_BotAccess3
getInput $Pbot_AccessList3 "Enter the name of user (case matters 6 chars)"
saveVar $PBot_AccessList3
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_BotAccess4
getInput $Pbot_AccessList4 "Enter the name of user (case matters 6 chars)"
saveVar $PBot_AccessList4
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_BotAccess5
getInput $Pbot_AccessList5 "Enter the name of user (case matters 6 chars)"
saveVar $PBot_AccessList5
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_BotAccess6
getInput $Pbot_AccessList6 "Enter the name of user (case matters 6 chars)"
saveVar $PBot_AccessList6
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_BotAccess7
getInput $Pbot_AccessList7 "Enter the name of user (case matters 6 chars)"
saveVar $PBot_AccessList7
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_BotAccess8
getInput $Pbot_AccessList8 "Enter the name of user (case matters 6 chars)"
saveVar $PBot_AccessList8
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_BotAccess9
getInput $Pbot_AccessList9 "Enter the name of user (case matters 6 chars)"
saveVar $PBot_AccessList9
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_BotAccess10
getInput $Pbot_AccessList10 "Enter the name of user (case matters 6 chars)"
saveVar $PBot_AccessList10
gosub :sub_SetBotMenu
openMenu "BotSecurity"

:Menu_Run
#---------------------------------Script Run Time--------------------------------------------------------------
:BuildInformation
send "'" $PBot_DisplayName ": starting pbot.... be with you in a moment..*"
# Check Planet Number and Current Sector then Enter Citadel.
:GetCurrent
if ($location = "Planet")
	send "*"
end
if ($location = "Citadel")
	send "q*"
	setVar $location "Planet"
end
setTextLineTrigger GetPlanetInfo :getplanetinfo "Planet #"
pause
:getplanetinfo
send "c"
getWord CURRENTLINE $currentplanet 2
stripText $currentplanet "#"
getWord CURRENTLINE $currentsector 5
stripText $currentsector ":"
getWord CURRENTLINE $currentplanetname 6
waitFor "Citadel command"

# Auto Load Stardock Info
if ($PBot_StarDock = 0)
	send "qqv"
	settextlinetrigger StarDock :StarDockLocation "The StarDock is located in sector"
	pause
	:StarDockLocation
	setVar $stardockline CURRENTLINE
	getword $stardockline $PBot_StarDock 7
	striptext $PBot_StarDock "."
	waitfor "Command [TL="
	send "l" $currentplanet "*c"
	saveVar $PBot_StarDock
	waitfor "Citadel command"
end

# Get Turns
:GetInformation
send "i"
setTextLineTrigger GetTurnAmount :getturns "Turns left"
pause
:getturns
killAllTriggers
getWord CURRENTLINE $PBot_CurrentTurns 4
stripText $PBot_CurrentTurns ","
setTextLineTrigger GetHoldInformation :GetHoldInformation "Total Holds    :"
pause
:GetHoldInformation
KillAllTriggers
setVar $holdinformationline CURRENTLINE
striptext $holdinformationline "="
getWord $holdinformationline $currentholds 4
getWord $holdinformationline $holdstatus 6

# Get Photon Amount
if ($PBot_PhotonToggle = 1)
	setTextLineTrigger currentphotons :currentphotons  "Photon Missiles:"
	setDelayTrigger nocurrentphotons :nocurrentphotons 1000
	pause
	:nocurrentphotons
		KillAllTriggers
		setVar $PBot_CurrentPhotons 0
		SetVar $PBot_PhotonToggle 0
		goto :pastphotons
	
	:currentphotons
		KillAllTriggers
		getWord CURRENTLINE $PBot_CurrentPhotons 3
		SetVar $PBot_PhotonToggle 1
		waitfor "Citadel command"
		goto :pastphotons
end
:pastphotons


if ($holdstatus <> "Fuel")
	setVar $PBot_CurrentFuelOre 0
end
if ($holdstatus = "Fuel")
	getWord $holdinformationline $PBot_CurrentFuelOre 7
	striptext $PBot_CurrentFuelOre "Ore"
end

if ($PBot_CurrentFuelOre < $currentholds)
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
	goto :GetInformation
	:NoRoom
	killAllTriggers
	send "qjyl" $currentplanet "*c"
	waitfor "Citadel command"
	goto :GetInformation
	 
end

# Get Cash Amount
:GetCashAmount
send "i"
setTextLineTrigger GetCashAmount :sgetcashamount "Credits"
pause
:sgetcashamount
killAllTriggers
getWord CURRENTLINE $currentcredits 3
stripText $currentcredits ","
setVar $PBot_CashAmount $currentcredits
saveVar $Pbot_CashAmount
waitFor "Citadel command"


#Build Escape Sector Data! - Proccess Could Take Awhile.

:LoadEscapeSectorData
#ECHO ANSI_14 "**LOAD DATA FILES: " $PBot_LoadDataFiles "**"
if ($PBot_LoadDataFiles = 1)
setVar $fightercount 0
setVar $escapefigcount 0
fileexists $escapefileexists "escapes.txt"
#ECHO ANSI_14 "**ESCAPE LIST FILE: " $escapefileexists "**"

if ($escapefileexists = 1)
	setVar $PBot_FindEscape 0
	# Read Escape DataBase
	setVar $pass 1
	ECHO ANSI_11 "*Loading Escape Sector Database...."
	:readescapes
	read "escapes.txt" $PBot_Escapefig_Database[$pass] $pass
	#echo ANSI_12 "S:" $PBot_Escapefig_Database[$pass]
	if ($PBot_Escapefig_Database[$pass] = 0)
		subtract $escapefigcount 1
		goto :donereadingescapes
	end
	add $escapefigcount 1
	add $pass 1
	goto :readescapes
	:donereadingescapes
	ECHO ANSI_15 "Done"
	setVar $pass 1
	ECHO ANSI_11 "*Loading Fighter Database...."
	:readfighters
	read "fighters.txt" $PBot_Fighter_Database[$pass] $pass
	#echo ANSI_15 "F:" $PBot_Fighter_Database[$pass]
	if ($PBot_Fighter_Database[$pass] = 0)
		subtract $fightercount 1
		goto :donereadingfighters
	end
	setVar $placedfighters[$PBot_Fighter_Database[$pass]] 1
	add $fightercount 1
	add $pass 1
	goto :readfighters
	:donereadingfighters
	ECHO ANSI_15 "Done*"
	setVar $PBot_FindEscape 0
	setVar $PBot_LoadDataFiles 0
else
	setVar $PBot_FindEscape 1
end

end
:BuildEscapeSectorData
if ($PBot_FindEscape = 1)
send "'" $PBot_DisplayName ": i'm building an escape sector list, please standby*"
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
	setTextLineTrigger perstag :AddFig "Personal"
	setTextTrigger prompt :FigBuildDone "Command [TL="
	pause
	
:Addfig
	add $fightercount 1
	getWord CURRENTLINE $PBot_Fighter_Database[$fightercount] 1
	write "fighters.txt" $PBot_Fighter_Database[$fightercount]
	setVar $placedfighters[$PBot_Fighter_Database[$fightercount]] 1
	goto :resetfigbuild

:FigBuildDone
	setVar $escapefigpass 1
	setVar $figfinderpass 1


send "l" $currentplanet "*cc"
waitfor "Citadel command"
echo ANSI_6 "**[ " $PBot_DisplayName " ] counting sectors and finding escape zones, standby.*"
setVar $pass 1
#ECHO ANSI_14 "** FIG FINDER LOOP! **"
:FigFinderLoop
	#send "'" $PBot_DisplayName " : building figed escape sector list pass, " $pass "*"
	if ($fightercount > $figfinderpass)
		getdistance $distance $currentsector $PBot_Fighter_Database[$figfinderpass]
		if ($distance > $PBot_EscapeDistance)
			setVar $figescapesector $PBot_Fighter_Database[$figfinderpass]
			setVar $escapewarpspass 1
			setVar $alreadyescapesector 1
			:AlreadyEscapeSector
			        if ($PBot_Escapefig_Database[$alreadyescapesector] = $figescapesector)
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
				if ($PBot_CurrentTurns > $PBot_MinAutoExploreTurns) or ($PBot_CurrentTurns = "Unlimited")
				send "q"
				# Photon Spender
				:UnloadPhotons
				if ($PBot_CurrentPhotons > 0)
					# Unload Photons to Avoid Turn Loss.
					send "s"
					setTextLineTrigger UnloadPhotonSector :UnloadPhotonSector "Warps to Sector"
					pause
					:UnloadPhotonSector
					GetWord CURRENTLINE $unloadphotonto 5
					StripText $unloadphotonto ")"
					StripText $unloadphotonto "("
					send "cpy" $unloadphotonto "*q"
					send "'" $PBot_DisplayName ": auto-unloading photons for auto-explore.*"
					subtract $PBot_CurrentPhotons 1
					setDelayTrigger photonrefirerate :photonrefirerate $PBot_PhotonRefireRate
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
				send "sdshh2t1*cq*m" $PBot_StarDock "*y"
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
					send "'" $PBot_DisplayName ": lost track of planet auto hiding on dock.*"	
				:AutoExploreSBlind
				:NoFuelOreOnShipA
					send "'" $PBot_DisplayName ": i lost track of locating beam or ran out of fuel ore.*"
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
			ECHO ANSI_5 "*MAX: " $PBot_EscapeMaxWarps " WARPS: " $escapewarpspass "*"
			if ($PBot_EscapeMaxWarps >= $escapewarpspass)	
			setVar $PBot_Escapefig_Database[$escapefigpass] $PBot_Fighter_Database[$figfinderpass]
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
		#ECHO ANSI_13 "*PASS" $pass "*"
		goto :FigFinderLoop
	end

if ($escapefigcount < $PBot_MinEscapeSectors)
	setVar $figfinderpass 1
	if ($PBot_EscapeDistance > $PBot_MinEscapeDistance)
		subtract $PBot_EscapeDistance 1
	else
		setVar $PBot_EscapeDistance 12
		if ($PBot_EscapeMaxWarps < 6)
			add $PBot_EscapeMaxWarps 1
		else
			setVar $PBot_EscapeMaxWarps 1
			subtract $PBot_MinEscapeDistance 1
		end
	end
	#ECHO ANSI_12 "*PASS" $pass "*"
	goto :FigFinderLoop
end

send "q"
# Escape Figs Found
write "escapes.txt" 0
write "fighters.txt" 0
setVar $PBot_FindEscape 0
KillAllTriggers
# TEST BUILD FIND FIG ESCAPE ZONES
end

# Display Environment Local
:DisplayEnvironment
echo ANSI_3 "**[StarDock Location]: " ANSI_15 $PBot_StarDock "*"
echo ANSI_3 "[   Figged Sectors]: " ANSI_15 $fightercount "*"
echo ANSI_3 "[   Escape Sectors]: " ANSI_9 $escapefigcount "*"
echo ANSI_3 "[   Current Planet]: " ANSI_11 $currentplanetname " #" $currentplanet "*"
echo ANSI_3 "[   Current Sector]: " ANSI_11 $currentsector "*"
echo ANSI_3 "[  Current Credits]: " ANSI_4 $PBot_CashAmount "*"
echo ANSI_3 "[  Current Photons]: " ANSI_4 $PBot_CurrentPhotons "*"
echo ANSI_3 "[    Current Turns]: " ANSI_4 $PBot_CurrentTurns "*"
ECHO ANSI_3 "[    Current Holds]: " ANSI_6 $currentholds "*"
ECHO ANSI_3 "[  Current FuelOre]: " ANSI_6 $PBot_CurrentFuelOre "*"
ECHO ANSI_3 "*Escape information: *"
echo ANSI_7 "   Escape Sectors Found: " ANSI_8 $escapefigcount "*"
echo ANSI_7 "   Escape Sectors Used: " ANSI_8 $PBot_UsedEscapePass "*"
echo ANSI_7 "   Escape Sectors List: [" ANSI_8
setVar $figlocaldisplayloop 1
:FigLocalDisplayLoop
if ($escapefigcount >= $figlocaldisplayloop)
	echo " : " $PBot_Escapefig_Database[$figlocaldisplayloop]
	add $figlocaldisplayloop 1
	goto :FigLocalDisplayLoop
end
echo " :" ANSI_7 " ]**"

# Display pbot Status line on Subspace and goto get triggers.
:pbotInformationLine
if ($PBot_BotEnabled = 1)
	send "'" $PBot_DisplayName ": "
	if ($PBot_PhotonToggle = 1)
	send "photon "
	end
	if ($PBot_BotMode = 1)
	send "hiding planet (auto escape mode)*"
	end
	if ($PBot_BotMode = 2)
	send "protecting planet (manual escape mode){.go}*"
	end 
	if ($PBot_BotMode = 3)
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
#setTextLineTrigger QCannonFire :QCannonFire "Quasar Cannon on"
# Limpet Hit Trigger.
setTextLineTrigger DeployedLimpetMine :DeployedLimpetMine "Limpet mine in"
setTextLineTrigger SectorCheck :SectorCheck "Sector  :"
setTextTrigger LocalRefresh :LocalRefresh "Do you wish to end your turn now and remain here?"
#setTextLineTrigger SaveMe :SaveMe "=saveme"
# Standby
:BotOffTriggers
setTextLineTrigger IncomingCommand :IncomingCommand $PBot_TriggerCommand

setEventTrigger StandBy :StandBy "Connection lost"
# Pause
:TriggerPause
pause

# Incoming Command Processor 
:IncomingCommand
setVar $commandline CURRENTLINE
echo ANSI_3 "*INCOMING MESSAGE: " ANSI_4 $commandline
getWord $commandline $userverify 1
striptext $userverify "'"

if ($userverify = $PBot_TriggerCommand)
  getWord $commandline $pbotuser 2
  	if ($pbotuser = "local")
	  echo ANSI_3 "*INCOMING USER: " ANSI_4 $pbotuser
	  setVar $CommandShift 9
	  getWord $commandline $pbotcommand 3
	else
	  echo ANSI_3 "*INCOMING USER: " ANSI_4 "Unverified"
	  setVar $CommandShift 9
	  setVar $pbotcommand None
	end

elseif ($userverify = "R")
	getWord $commandline $pbotuser 2
   # User Access Control
	if ($pbotuser = $PBot_AccessList1) or ($pbotuser = $PBot_AccessList2) or ($pbotuser = $PBot_AccessList3) or ($pbotuser = $PBot_AccessList4) or ($pbotuser = $PBot_AccessList5) or ($pbotuser = $PBot_AccessList6) or ($pbotuser = $PBot_AccessList7) or ($pbotuser = $PBot_AccessList8) or ($pbotuser = $PBot_AccessList9) or ($pbotuser = $PBot_AccessList10)
	  echo ANSI_3 "*INCOMING USER: " ANSI_4 $pbotuser
	  setVar $CommandShift 0
	  getWord $commandline $pbotcommand 4
	else
	  echo ANSI_3 "*INCOMING USER: " ANSI_4 "Unverified"
	  setVar $CommandShift 1
	  setVar $pbotcommand None
	end

elseif ($userverify = "F") or ($userverify = "P")
  echo ANSI_3 "*INCOMING USER: " ANSI_4 "Unverified"
  setVar $CommandShift 1
  setVar $pbotcommand None

else
  echo ANSI_3 "*INCOMING USER: " ANSI_4 "Unverified"
  setVar $CommandShift 1
  setVar $pbotcommand None
end

if ($pbotcommand = $PBot_TriggerCommand)
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

if ($PBot_BotEnabled = 1)
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
	if ($pbotcommand = "saveme") or ($pbotcommand = "save")
		setVar $PBot_BotMode 2
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
if ($getphotonyes = 1)
	goto :YesPhoton	
end

:NoPhoton
send "'" $PBot_DisplayName ": not a number.*"
	goto :BotTrigger

:YesPhoton
setVar $totalphotons $getphotons
add $totalphotons $PBot_CurrentPhotons
if ($totalphotons > $PBot_MaxPhotons)
	send "'" $PBot_DisplayName ": can't get " $getphotons " photons, max is " $PBot_MaxPhotons " and i have " $PBot_CurrentPhotons " photons already.*"
	setTextLineTrigger IncomingCommand :IncomingCommand $PBot_TriggerCommand
	goto :TriggerPause
end
setVar $totalphotoncost $PBot_PhotonCost
multiply $totalphotoncost $getphotons

if ($getphotonsyes = "FALSE")
	send "'" $PBot_DisplayName ": invalid photon amount*"
	goto :BotTrigger
end
KillAllTriggers
#getcredits for photons
setVar $PBot_PhotonToggle 1
send "tf" $totalphotoncost "*"
if ($getarmidsyes = 1)
	if ($getarmids > 0) and ($getarmids < 250)
	setVar $totalarmidcost 1000
	multiply $totalarmidcost $getarmids
	send "tf" $totalarmidcost "*"
	end
end
if ($getlimpetsyes = 1)
	if ($getlimpets > 0) and ($getlimpets < 250)
	setVar $totallimpetcost 10000
	multiply $totallimpetcost $getlimpets 
	send "tf" $totallimpetcost "*"
	end
end
send "b" $PBot_StarDock "*"
setTextlineTrigger StardockReady :StardockReady "Locating beam pinpointed"
setTextlineTrigger StardockBlind :StardockBlind "No locating beam found"
pause
:StardockReady
killalltriggers
send "yps"
setTextLineTrigger DockLimpetCheck :ReDockLimpetCheck "A port official runs up to you"
setTextTrigger NoLimpetAtDock :ReNoLimpetAtDock "<StarDock>"
pause
:ReDockLimpetCheck
KillAllTriggers
send "y"
:ReNoLimpetAtDock
KillAllTriggers
send "hp" $getphotons "*"
if ($getarmidsyes = 1)
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
if ($getlimpetsyes = 1)
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
	send "'" $PBot_DisplayName ": Script Ending Can't Gain Lock on Planet Location. I'll hide on dock.*"
	send "psg"
	goto :END

:NoFuelOreOnShip
	KillAllTriggers
	send "'" $PBot_DisplayName ": Script Ending Not Enough Fuel Ore to Return. I'll hide on dock.*"
	send "psg"
	goto :END

:StarDockBlind
	KillAllTriggers
	send "n"
	send "'" $PBot_DisplayName ": I don't have the ability to warp to StarDock.*"
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
if ($namecheck = 1)
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
	send "'" $PBot_DisplayName ": could not gain lock on your location.*"
	goto :GetCurrent	
	:AlreadyThereBring
	KillAllTriggers
	send "q"
	send "'" $PBot_DisplayName ": i'm already here!*"
	goto :GetCurrent
else
	getWord $locationline $userlocation 3
	isnumber $namecheck2 $userlocation
	if ($namecheck2 = 1)
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
		send "'" $PBot_DisplayName ": could not gain lock on your location.*"
		goto :GetCurrent
		:AlreadyThereBring2
		KillAllTriggers
		send "q"
		send "'" $PBot_DisplayName ": i'm already here!*"
		goto :GetCurrent
	end
end
send "'" $PBot_DisplayName ": error getting your location*"
send "qq"
goto :GetCurrent
:BringBotNA
send "'" $PBot_DisplayName ": your not on the corp!*"
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
if ($deployarmids = 1) 
	if ($darmids > 0) and ($darmids <= $PBot_MaxDeployArmids)
	send "h1t" $darmids "*cq*"
	end
end
if ($deploylimpets = 1)
	if ($dlimpets > 0) and ($dlimpets <= $PBot_MaxDeployLimpets)
	send "h2t" $dlimpets "*cq*"
	end
end
if ($deployfighters = 1) 
	if ($dfighters > 0) and ($dfighters <= $PBot_MaxDeployFighters)
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
send "'heehehehahahaha**"
goto :BotTrigger

# Photons on/off FIRE!
:PhotonToggle
killAllTriggers
isnumber $sendphoton $photoninfo
if ($sendphoton = 1)
	if ($photoninfo > 0)
	getdistance $distance $currentsector $photoninfo
	if ($distance = 1)
		send "cpy" $photoninfo "*q"
		send "'" $PBot_DisplayName ": photon fired!*"
		send "q"
		goto :GetCurrent
	else
		send "'" $PBot_DisplayName ": that sector is not adjacent*"
		send "q"
		goto :GetCurrent
	end	
	end
end	
if ($photoninfo = "on")
	setVar $PBot_PhotonToggle 1
	send "'" $PBot_DisplayName ": photons enabled.*"
end
if ($photoninfo = "off")
	setVar $PBot_PhotonToggle 0
	send "'" $PBot_DisplayName ": photons disabled.*"
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
			if ($PBot_PhotonToggle = 1) 
				# PHOTON NOW !
				send "cpy" $deploysector "*q"
			end
			goto :getoutnow

		else
		# Report Distance and Threat on SubSpace
		:SubSpaceReport	
		if ($distance <= $PBot_RedAlertDistance)
			if ($PBot_BotMode = 3) and ($PBot_PhotonToggle = 1)
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
						subtract $PBot_CurrentPhotons 1
						send "p" $currentsector "*y"
						send "'" $PBot_DisplayName ": photoned " $intrudername " in " $deploysector " from " $offensivesector.warp[$offensivepass] ".*"
						if ($PBot_CurrentPhotons = 0)
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

		if ($distance <= $PBot_OrangeAlertDistance)
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
send "p" $PBot_Escapefig_Database[$PBot_UsedEscapePass] "*" 
settextlinetrigger escapeready :escapeready "Locating beam pinpointed"
settextlinetrigger connotblind :lostescape "You cannot"
settextlinetrigger alreadythere :alreadythere "You are already in that sector"
pause
:escapeready
killalltriggers
if ($PBot_BotMode = 1) or ($PBot_BotMode = 3)
	send "y"
end
if ($PBot_BotMode = 2)
	send "'" $PBot_DisplayName ": escape sector locked send go command!!*"
	setTextLineTrigger photdontgo :dontgo $PBot_TriggerCommand&" abort"
	setTextLineTrigger pbotgo :go $PBot_TriggerCommand&" go"
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
add $PBot_UsedEscapePass 1
saveVar $PBot_UsedEscapePass
send "q"
goto :GetCurrent

:lostescape
	killalltriggers
	add $PBot_UsedEscapePass 1
	saveVar $PBot_UsedEscapePass
	goto :FindSolution

:alreadythere
	killalltriggers
	send "q"
	goto :GetCurrent


:pbotVersion
send "'[pbot version:" $PBot_VersionInfo "]*"
goto :BotTrigger

:pbotHelp
send "'*[ on, off, status, re (refresh), photon X ( on, off, sector # ) ]*"
send "[come, rebuild, version, help, go {or in mode 2 .go/.pbot abort}]*"
send "[   save, saveme (sets mode 2 enroute), list (escape sectors)   ]*"
send "[ reload # # # (photons, armids, limpets) ( reloads at Stardock)]*"
send "[ deploy # # # (armids, limpets, fighters) (lift,holo,deploy)   ]*"
send "[   modes 1)Auto Escape  2)Defensive Escape  3)Offensive Photon ]**"
goto :BotTrigger

:pbotStatus
	#GET ALL STATUS
	send "'[" $PBot_DisplayName ":"
	if ($PBot_BotEnabled = 1) 
		send "ON" 
	else 
		send "OFF" 
	end
	send "|Photons:" 
	if ($PBot_PhotonToggle = 1) 
		send "ON" 
	else 
		send "OFF" 
	end
	send "|Turns:" $PBot_CurrentTurns
	send "|Mode:"
	if ($PBot_BotMode = 1) 
		send "Escape" 
	end
	if ($PBot_BotMode = 2)
		send "Defensive" 
	end
	if ($PBot_BotMode = 3)
		send "Offensive"
	end
	send "|Missiles:" $PBot_CurrentPhotons "|Planet:" $currentplanet "]*" 
	
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
send $PBot_DisplayName ": escape sectors ["
:FigUserDisplayLoop
if ($escapefigcount > $figdisplayloop)
	send " :" $PBot_Escapefig_Database[$figdisplayloop]
	add $figdisplayloop 1
	goto :FigUserDisplayLoop
end
send ": ]**"
goto :GetTriggers
:SendEscapeListNA
KillAllTriggers
send "'" $PBot_DisplayName ": your not on the corp.*"
goto :GetTriggers


:ChangeMode
isnumber $numentry $modechange
ECHO ANSI_14 "NUM ENTRY: " $numentry " MODE: " $modechange
if ($numentry = 1)
	if ($modechange = 1)
		goto :AutoEscapeBotOn
	end
	if ($modechange = 2)
		goto :DefensiveBotOn
	end
	if ($modechange = 3)
		goto :OffensiveBotON
	end	
        send "'" $PBot_DisplayName ": invalid mode option.*"
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
        send "'" $PBot_DisplayName ": invalid mode option.*"
	killalltriggers
	goto :GetTriggers	
end

# Bot Command Continue without refresh
:BotTrigger
setTextLineTrigger IncomingCommand :IncomingCommand $PBot_TriggerCommand
goto :TriggerPause

:BotToggleOff
killAllTriggers
setVar $PBot_BotEnabled 0
send "'" $PBot_DisplayName ": counting electric sheep.*"
send "q"
saveVar $PBot_BotEnabled
goto :GetCurrent

:BotToggleOn
killAllTriggers
setVar $PBot_BotEnabled 1
send "'" $PBot_DisplayName ": my electrons glow.*"
send "q"
saveVar $PBot_BotEnabled
goto :GetCurrent

:AutoEscapeBotON
killAllTriggers
setVar $PBot_BotMode 1
send "'" $PBot_DisplayName ": i wont let you down.*"
send "q"
saveVar $PBot_BotMode
goto :GetCurrent

:DefensiveBotON
killAlltriggers
setVar $PBot_BotMode 2
send "'" $PBot_DisplayName ": just give me the word!*"
send "q"
saveVar $PBot_BotMode
goto :GetCurrent

:OffensiveBotOn
killAllTriggers
setVar $PBot_BotMode 3
send "'" $PBot_DisplayName ": lets toast em boss. -;)*"
send "q"
saveVar $PBot_BotMode
goto :GetCurrent

:RebuildEscapeList
killAllTriggers
setVar $PBot_UsedEscapePass 1
setVar $PBot_LoadDataFiles 1
delete "escapes.txt"
delete "fighters.txt"
send "q"
saveVar $PBot_UsedEscapePass
goto :GetCurrent

:StandBy
#Disconnected Standby On Reconnect and Login Script to put you back on Planet.
KillAllTriggers
waitFor "Citadel command"
send "q"
goto :GetCurrent

:SaveMe
KillAllTriggers
setVar $SaveMeLine CURRENTLINE
striptext $SaveMeLine "=saveme"
getWord $SaveMeLine $SaveMeSector 3
isnumber $SaveMeGo $SaveMeSector
if ($SaveMeGo)
	send "p" $SaveMeSector "*"
end
send "q"
goto :GetCurrent

:END