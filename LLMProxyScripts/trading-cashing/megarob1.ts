# Quick Megarob script
# Author:	 Tweety/Grave
# Description:	 Just a lazy way to rob a port when your done buying it down.
getWord CURRENTLINE $prompt 1
if ($prompt <> "Citadel")
  clientMessage "Script Aborted: Must be at Planet prompt."
  HALT
end
getInput $planet "Enter planet #" 0
:start
waitfor "mega please"

SEND "qqpr*r"

setTextLineTrigger portcreds :grabCreds "The Trade Journals estimate"
PAUSE

:grabCreds
  getWord CURRENTLINE $credits 11
  stripText $credits ","
  setVar $robcredits $credits

  MULTIPLY $robcredits 100
  DIVIDE $robcredits 9
  DIVIDE $robcredits 10

  if ($credits >= 2970000)
    if ($credits > 6400000)
      SEND "3400000*"
      WAITFOR "Command [TL="
      send "l" $planet "*c"
      send "'Rob somewhere else, then run the script here again.*"
      goto :start
    else
      SEND $robcredits&"*"
      WAITFOR "Command [TL="
      send "l" $planet "*ctt" $robcredits "*"
      send "'" $robcredits " credits stolen.*"
      goto :start
    end
  else
    SEND "0*"
    SUBTRACT $credits 3000000
    MULTIPLY $credits -1
    WAITFOR "Command [TL="
    send "l" $planet "*c"
    send "'Script Aborted:Port needs " $credits " more creds before achieving Success!*"    
    goto :start


