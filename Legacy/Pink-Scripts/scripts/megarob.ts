# Quick Megarob script
# Author:	 Tweety
# Description:	 Just a lazy way to rob a port when your done buying it down.

getWord CURRENTLINE $prompt 1
if ($prompt <> "Command")
  clientMessage "Script Aborted: Must be at Command prompt."
  HALT
end

SEND "pr*r"

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
      clientMessage "Rob somewhere else, then run the script here again."
      HALT
    else
      SEND $robcredits&"*"
      WAITFOR "Command [TL="
    end
  else
    SEND "0*"
    SUBTRACT $credits 3000000
    MULTIPLY $credits -1
    WAITFOR "Command [TL="
    ECHO ANSI_12 "**Script Aborted: " ANSI_8 "Port needs " ANSI_14 $credits ANSI_8 " more creds before achieving " ANSI_9 "Success!"    
  end