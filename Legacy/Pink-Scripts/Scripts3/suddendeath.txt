--------------
# TWX : Generic Sudden Death
# Author : Fornax. Parts and ideas borrowed from other scripts.
# Credits to : Xide, SupG
# Description :
# Trigger Point : Victim getting available for attacks
#================================================
:Setup1
#SetVar $victim "Oban"
SetVar $victim "Fornax2"
SetVar $UseFigs "0"
#SetVar $UseFigs "10000"
SetVar $TDrop "TRUE"
SetVar $TDropAuto "TRUE"
SetVar $TDropWide "NotTRUE"
SetVar $WarpHome "NotTRUE"
SetVar $KillOnSight "TRUE"
#-----
SetVar $FedSafe "FALSE"
SetVar $AddDelay "TRUE"
SetVar $ForcedLoopOnce "NotTRUE"
SetVar $InDrop "notTRUE"
SetVar $OnSight "notTRUE"
SetVar $InSight "notTRUE"
SetVar $Debug "NotTRUE"
SetVar $WarpSucces "-1"
#SetVar $Debug "1"
Setvar $Version "0.136"
Echo ANSI_6 & "* Generic Sudden Death " & $Version & " by Lord Fornax. "
Echo "* NOT for distribution. "
Echo ANSI_14 & "* Defaults are : " & $Victim & ", figs: " & $UseFigs & ", TDrop: " & $TDrop & "*"

# ==> If you hardcode the settings and rename the script accordingly
# to indicate the aimed target you can uncomment the next line
# to skip interactive setup.
#Goto :Setup2

Echo * ANSI_7 & " Use current settings or [C]hange Name or [F]ull config? : *"
getConsoleInput $inputVar SingleKey
#Echo "* Inputvar = " & $Inputvar & "*"
UpperCase $InputVar
If ($InputVar = "F")
Goto :FullConfig
ElseIf ($InputVar = "C")
SetVar $CFGName "name"
Goto :FullConfig
Else
Echo ANSI_10 & "*Using defaults*"
Goto :Setup2
End

:Setup2
# Set all text-strings that will trigger us into attack:
mergeText $victim " appears in a" $victimappears
mergeText $victim " blasts off fr" $victimblastsof
mergeText $victim " lifts off fr" $victimliftsof
mergeText $victim " warps into" $victimwarpsinto
mergeText $victim " enters the game." $victimentersthegame
mergeText $victim " is powering up weapon" $victimattacks
mergeText $victim "'s Escape Pod tried to" $victimpod

mergeText $victim " lands on" $victimlandson
mergeText $victim " docks at" $victimdocks
mergeText $victim " warps out of" $victimwarpsout
mergeText $victim " is surrounded by" $victimtwarpsout
mergeText $victim "An Escape Pod warps out of this sector!" $victimpodwarpsout

mergeText $victim "'s" $victimhis
mergeText $victim "," $victiminsector
GetLength $victimhis $victimhislength

#================================================ :restart
:restart
SetVar $InSight "notTRUE"
if ($ForcedLoopOnce = "Finished")
Goto :EndOfScript
end
# Set number of fighters to use
SetVar $NumFigs $Usefigs
#Gosub :GetState
#Gosub :Debug1

KillAllTriggers
setTextLineTrigger det1 :attackvictim $victimappears
setTextLineTrigger det2 :attackvictim $victimblastsof
setTextLineTrigger det3 :attackvictim $victimliftsof
setTextLineTrigger det4 :attackvictim $victimwarpsinto
setTextLineTrigger det5 :attackvictim $victimpod
setTextLineTrigger det6 :attackvictim $victimattacks
setTextLineTrigger det7 :attackvictim $victimentersthegame
#setTextLineTrigger det8 :attackvictim "$Victim"

if ($ForcedLoopOnce = "TRUE")
SetVar $ForcedLoopOnce "Finished"
Goto :AttackVictim
end

#if ($Debug = "TRUE")
# SetVar $ForcedLoopOnce "Finished"
# Goto :AttackVictim
# end

If ($TDrop = "TRUE")
setTextLineTrigger trip :TDropper1 "Deployed Fighters Report Sector"
end

If ($KillOnSight = "TRUE")
SetVar $OnSight "TRUE"
KillTrigger Trader1
setTextLineTrigger Trader1 :ExamineTrader ", w/ "
Else
SetVar $OnSight "notTRUE"
end

If ($InDrop = "TRUE")
SetVar $InDrop "notTRUE"
SetVar $OnSight "TRUE"
KillTrigger Trader1
setTextLineTrigger Trader1 :ExamineTrader ", w/ "
end

pause
Goto :restart
:navpoint
echo " - Navpoint - Debug -*"
send "*"
Goto :restart

#------------------------------------------------
:ExamineTrader
setvar $line CURRENTLINE
getWordPos $line $textPosOwnedBy "[Owned by]"
getWordPos $line $textPosVictim $Victim
if ($textPosOwnedBy > 0)
setTextLineTrigger Trader1 :ExamineTrader ", w/ "
Goto :Restart
elseif ($textPosVictim > 0)
SetVar $InSight "TRUE"
Goto :attackvictim
else
Goto :Restart
End
#------------------------------------------------
:TDropper1
setVar $line CURRENTLINE
# Deployed Fighters Report Sector 1014: AxiSofEviL's System Recon entered sector.
getWord $Line $fed 1
if ($fed = "F")
send "*"
goto :Restart
end
getWord $line $sectorNum 5
striptext $sectornum "B"
striptext $sectornum "^"
stripText $sectorNum " "
stripText $sectorNum ":"
GetWordPos $Line $result $VictimHis
If ($Result > 0)
SetVar $InDrop "TRUE"
SetVar $WideDropping "0"
Goto :TDropper2
Else
Goto :restart
End
#----------------------------
:TDropper2
SetVar $WarpSucces "-1"
Send "m" $sectorNum "*y"
Waitfor ""
Gosub :BWProtect
If ($WarpSucces = "YES")
SetVar $InDrop "TRUE"
Goto :restart
End
If (($WarpSucces = "NO") AND ($TDropWide = "TRUE")
SetVar $WarpSucces "-1"
Add $WideDropping 1
SetVar $InDrop "FALSE"
Goto :WideDropper
End

SetVar $WarpSucces "-1"
SetVar $InDrop "FALSE"
Goto :Restart
#----------------------------

#----------------------------
:attackvictim
KillAllTriggers
## Fedcom-protection
setVar $TripLine CURRENTLINE
getWord $TripLine $fed 1
if ($fed = "F")
goto :Restart
end
send "a"

# ==> Enable lines below if you think a tiny delay is necessary here.
# if ($AddDelay = "TRUE")
# Gosub :DelayAA setdelaytrigger wait1 :Wait01 20
# end

waitFor ""
:pickvictim
killtrigger abort
setTextTrigger abort :restart "Command [TL="
waitFor "Attack "
setvar $line CURRENTLINE
GetWord $line $action 1
GetWord $line $trader 2
getWordPos $line $textPosUnmanned " unmanned "
cuttext $line $thistraderhis 8 $victimhislength

if ($textPosUnmanned > 1)
send "*"
goto :pickvictim
end

if ($action = "There") OR ($action = "Command")
goto :restart
end

if ($thistraderhis = $victimhis)
goto :mainkill
else
#Gosub :DelayAA
if ($action = "Attack")
Send "*"
end
goto :pickvictim
end
goto :restart

#------------------------------------------------
:mainkill
Send "y" & $NumFigs & "*"
If ($Debug = "1")
goto :Restart
End
goto :attackvictim

#------------------------------------------------
#------------------------------------------------
:GetState
SetVar $TempLine CURRENTLINE
getWord $TempLine $Prompt 1
If ($Prompt = "Command")
ReplaceText $TempLine "[" " "
ReplaceText $TempLine "]" " "
getWord $TempLine $Current_Sector 4
# Echo "*CurrentSector = " & $Current_Sector
End
Return

#------------------------------------------------
:Delay20
:DelayAA
if ($AddDelay = "TRUE")
setdelaytrigger wait2 :DelayAAb 15
pause
:DelayAAb
end
Return
#------------------------------------------------
:BWProtect
SetTextTrigger AutoTW1 :BWProtectYES "All Systems Ready, shall we engage?"
SetTextTrigger AutoTW2 :BWProtectNO "*** WARNING *** No locating beam found for secto"
SetTextTrigger AutoTW3 :BWProtectOVER "Command [TL"
PAUSE
:BWProtectNO
KillTrigger AutoTW1
KillTrigger AutoTW3
SetVar $WarpSucces "NO"
Send "*"
Return
:BWProtectYES
KillTrigger AutoTW2
KillTrigger AutoTW3
SetVar $WarpSucces "YES"
Send "y"
Return
:BWProtectOVER
KillTrigger AutoTW1
KillTrigger AutoTW2
SetVar $WarpSucces "0"
Return

#------------------------------------------------
:Debug1
Return
#------------------------------------------------
:FullConfig
If ($CFGName = "name")
Else
Echo "* -- Full configuration --"
End
getInput $inputVar "Victim's name: *"
If ($InputVar = " " Or $InputVar = ".")
# Do nothing actually :-)
Else
if ($InputVar = "Q")
Halt
end
Setvar $Victim $InputVar
End
If ($CFGName = "name")
Goto :Setup2
End

:CFG_UseFigs
getInput $inputVar "Number of fighters to use: *"
If ($InputVar = " " Or $InputVar = ".")
# Do nothing actually :-)
Else
if ($InputVar = "Q")
Halt
end
Setvar $UseFigs $InputVar
End
IsNumber $Boolean $UseFigs
If ($Boolean <> "TRUE")
Echo "* How about something valid?"
Goto :CFG_UseFigs
End

Echo ANSI_7 & "Kill on sight? [Y/N/q] *"
GetConsoleInput $InputVar Singlekey
UpperCase $InputVar
If ($InputVar = "Y")
SetVar $KillOnSight "TRUE"
else
if ($InputVar = "Q")
Halt
end
SetVar $KillOnSight "NotTRUE"
end

Echo ANSI_7 & "Enable TW-Drop? [Y/N/q] *"
GetConsoleInput $InputVar SingleKey
UpperCase $InputVar
If ($InputVar = "Y")
SetVar $TDrop "TRUE"
else
if ($InputVar = "Q")
Halt
end
SetVar $TDrop "NotTRUE"
end

Echo ANSI_7 & "Return to a safe place after TW-Dropping? [Y/N/q] *"
GetConsoleInput $InputVar SingleKey
UpperCase $InputVar
If ($InputVar = "Y")
SetVar $WarpHome "TRUE"
else
if ($InputVar = "Q")
Halt
end
SetVar $WarpHome "NotTRUE"
end

Goto :EndOfConfig

Echo ANSI_7 & "Enable TW-Drop WIDE-Region? [Y/N/q] *"
Echo ANSI_7 & " ==> This is work in progress, I suggest 'N' for now. *"
GetConsoleInput $InputVar SingleKey
UpperCase $InputVar
If ($InputVar = "Y")
SetVar $TDropWide "TRUE"
else
if ($InputVar = "Q")
Halt
end
SetVar $TDrop "NotTRUE"
end

:EndOfConfig
#Echo "* Done *"
Echo "*Set : " & $Victim & ", figs: " & $UseFigs & ", TDrop: " & $TDrop &"*"
Goto :Setup2

#------------------------------------------------
:EndOfScript
