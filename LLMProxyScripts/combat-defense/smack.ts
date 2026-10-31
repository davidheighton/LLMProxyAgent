# Exiled TWX Script: Exiled Smack Script Booby Trap Script
# Author           : Pinky
# Description      : Photon Attacks Protected Sectors (must be modified to work with your sectors)
# Warnings         :
# Other            : 
                    
# show script banner
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "Smack Script Version 0.01" ANSI_11 " |===" ANSI_3 "--**"
echo ANSI_7 "If you have it keep it to yourself. -;)."

# Set trigger to stop script if disconnected
setEventTrigger 0 :End "Connection lost"

:waitforsmack
setTextLineTrigger 1 :smack_main "Deployed Fighters"
pause

:smack_main
setVar $pass 1
setVar $line CURRENTLINE
stripText $line ":"
getWord $line $smacksector 5

:protected_test
#test if protected sector
read protect.txt $protected $pass
add $pass 1
read protect.txt $protector $pass
if ($protected = 0)
	clientMessage "Not Attacking!"
        goto :waitforsmack
end
if ($protected = $smacksector) then
	goto :smack_sector
else
	add $pass 1
	goto :protected_test
end
goto :End

:smack_sector
send "m" $protector "*yycpy" $protected "*qm" $protected "*ay20000*"
send "m" $protector "*sh"


:End