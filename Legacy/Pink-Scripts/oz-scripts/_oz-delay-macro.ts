# oz delay macro
# u set the macro and the delay and thats is :D

setVar $OZ_dmac_Version "OZ-Delay Macro 2.0"

# check if we can run it from here
cutText CURRENTLINE $location 1 7

if ($location <> "Command")
        clientMessage "This script must be run from the Command Prompt"
        halt
end

loadVar $ozdmacSaved

if ($ozdmacSaved)
	loadVar $delay
	loadVar $macro
	loadVar $cycles
else
	setVar $delay 1
	setVar $macro "NA"
	setVar $cycles 0

	saveVar $delay
	saveVar $macro
	saveVar $cycles 
		
	setVar $ozdmacSaved 1
	saveVar $ozdmacSaved
end

addMenu "" "OZ-Delay Macro" "OZ-Delay Macro Settings" "." "" "Main" FALSE
addMenu "OZ-Delay Macro" "Run" "Run Script." "Z" :Menu_Run "" TRUE
addMenu "OZ-Delay Macro" "Set Delay" "Set Delay" "D" :Menu_Delay "" FALSE
addMenu "OZ-Delay Macro" "Set Macro" "Set Macro" "M" :Menu_Macro "" FALSE
addMenu "OZ-Delay Macro" "Set Cycles" "Set Cycles" "C" :Menu_Cycles "" FALSE


setMenuHelp "Run" "This Option Activates the Script."
setMenuHelp "Set Delay" "This Option Sets the delay time in ms. 1000 ms = 1 sec"
setMenuHelp "Set Macro" "This Option Sets the macro to send. Use ^m for enter."
setMenuHelp "Set Cycles" "This Option Sets the number of macro cycles."


gosub :sub_setMenu
gosub :OZ_dmacHeader
openMenu "OZ-Delay Macro"

:OZ_dmacHeader
ECHO ANSI_2 "*----------------------------------*"
ECHO ANSI_10 "           " $OZ_dmac_Version " "
ECHO ANSI_2 "*----------------------------------*"
return

:menu_delay
getInput $delay "Enter the delay in ms."
isNumber $number $delay
	if ($number = 0)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_delay
	end
saveVar $delay
gosub :sub_SetMenu
gosub :OZ_dmacHeader
openMenu "OZ-Delay Macro"

:menu_macro
getInput $macro "Enter the macro to send."
saveVar $macro
gosub :sub_SetMenu
gosub :OZ_dmacHeader
openMenu "OZ-Delay Macro"

:Menu_Cycles
getInput $cycles "Enter the number of cycles."
isNumber $number $cycles
	if ($number = 0)
		echo ANSI_12 "*Invalid Number*"
		goto :Menu_cycles
	end
saveVar $cycles
gosub :sub_SetMenu
gosub :OZ_dmacHeader
openMenu "OZ-Delay Macro"


:sub_setMenu
	setMenuValue "Set Delay" $delay
	setMenuValue "Set Macro" $macro
	setMenuValue "Set Cycles" $cycles
return
:Menu_Run
replaceText $macro "^m" "*"
send "'Starting OZ-Delay Macro*"
setVar $count 0
:start
if ($count = $cycles)
	goto :end
end
setDelayTrigger delay :delay $delay
pause

:delay
send $macro
add $count 1
goto :start

:end
send "'OZ-Delay Macro Ended*"
halt








