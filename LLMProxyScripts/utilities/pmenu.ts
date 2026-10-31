setVar $PBot_VersionInfo "pbot v2.1beta by pink 05/03/03"

setArray $PBot_Escapefig_Database SECTORS
setArray $PBot_Fighter_Database SECTORS

setVar $PBot_CurrentPhotons 0
setVar $PBot_CurrentCash 0
setVar $PBot_CurrentTurns 0
setVar $PBot_LoadDataFiles 1
setVar $PBot_StarDock 0

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
getInput $PBot_BotEnabled "Enter Enable Toggle (1=On 2=Off):" 
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
openMenu "PBot"

:Menu_OrangeAlertDistance
getInput $PBot_OrangeAlertDistance "Enter Red Alert Distance:" 
saveVar $PBot_OrangeAlertDistance
gosub :sub_SetBotMenu
openMenu "PBot"

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
ECHO "*RUNNING....*"