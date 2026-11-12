setVar $Header~Script "World Trade"

# check location
cutText CURRENTLINE $location 1 12
if ($location <> "Command [TL=")
        clientMessage "This script must be run from the command menu"
        halt
end

reqRecording
logging off

gosub :Header~Pack2Header

# get defaults
loadVar $WorldTradeSaved

if ($WorldTradeSaved)
  loadVar $Haggle~HaggleFactor
else
  setVar $Haggle~HaggleFactor 7
  saveVar $Haggle~HaggleFactor
  
  setVar $WorldTradeSaved 1
  saveVar $WorldTradeSaved
end

# create setup menu
addMenu "" "WorldTrade" "World Trade Settings" "." "" "Main" FALSE
addMenu "WorldTrade" "GO" "GO!" "G" :Menu_Go "" TRUE

# set move routine prefs
loadVar $Move~Saved

if ($Move~Saved)
  loadVar $Move~ScanHolo
  loadVar $Move~Evasion
  loadVar $Move~Attack
  loadVar $Move~PortPriority
  loadVar $Move~ExtraSend
  replaceText $Move~ExtraSend #42 "*"
else
  setVar $Move~ScanHolo 1
  setVar $Move~Evasion 0
  setVar $Move~Attack 0
  setVar $Move~PortPriority 1
  setVar $Move~ExtraSend "f1" & #42 & "ct"
  saveVar $Move~ExtraSend
  replaceText $Move~ExtraSend #42 "*"
  
  saveVar $Move~ScanHolo
  saveVar $Move~Evasion
  saveVar $Move~Attack
  saveVar $Move~PortPriority
  
  setVar $Move~Saved 1
  saveVar $Move~Saved
end

# WorldTrade must stay compatible with Density-only scanners. Do not issue holo-scan or saved extra-send commands.
# The scanner-dependent settings are applied by :DetectScanner when the trade loop starts.
setVar $Move~ScanHolo 0
setVar $Move~ExtraSend ""
# Pay toll fighters ("py") instead of attacking them.
setVar $Move~Attack 3
setVar $Move~DensityOnlyScanner 0
setVar $PortCheck~DensityOnly 0

setVar $Move~Menu "WorldTrade"
gosub :Move~Menu
setMenuHelp "Move" "This menu lets you configure how the script will navigate the game, searching for port pairs."

# set PPT prefs
loadVar $PPT~Saved

if ($PPT~Saved)
  loadVar $PPT~DropFigs
  loadVar $PPT~PercTrade
else
  setVar $PPT~DropFigs 1
  setVar $PPT~PercTrade 20
  
  saveVar $PPT~DropFigs
  saveVar $PPT~PercTrade
  
  setVar $PPT~Saved 1
  saveVar $PPT~Saved
end

setVar $PPT~Menu "WorldTrade"
# Unattended runs keep the ship's fighters: dropping a toll fighter at every traded port drains its defence.
setVar $PPT~DropFigs 0
gosub :PPT~Menu

# set PPTCheck prefs
loadVar $PortCheck~Saved

if ($PortCheck~Saved)
  loadVar $PortCheck~Danger
  loadVar $PortCheck~FuelOrganics
  loadVar $PortCheck~PortType    
else
  setVar $PortCheck~Danger 0
  setVar $PortCheck~FuelOrganics 1
  setVar $PortCheck~PortType 1
  
  saveVar $PortCheck~Danger
  saveVar $PortCheck~FuelOrganics
  saveVar $PortCheck~PortType
  
  setVar $PortCheck~Saved 1
  saveVar $PortCheck~Saved
end

gosub :PortCheck~Menu
setMenuHelp "PPT" "This menu lets you configure how the script performs its port-pair-trading."

# open config menu
openMenu "WorldTrade"

:Menu_Go
setVar $WorldTrade~Quota 0
gosub :DetectScanner
setEventTrigger disconnect :disconnected "Connection lost"
gosub :WorldTrade~WorldTrade
halt

:DetectScanner
  # A Holo ship answers S with a Holo/Density choice; a Density-only ship scans at once, so the extra letter of
  # "sd"/"sh" becomes a separate command (D redisplays the sector, H opens mine control and stalls the script).
  setVar $hasHolo 0
  send "i"
  waitOn "<Info>"
  setTextLineTrigger scannerLine :scannerLine "LongRange Scan :"
  setTextTrigger infoDone :infoDone "Command [TL="
  pause
  :scannerLine
  getWordPos CURRENTLINE $holoPos "Holo"
  if ($holoPos > 0)
    setVar $hasHolo 1
  end
  pause
  :infoDone
  killTrigger scannerLine
  killTrigger infoDone
  if ($hasHolo = 1)
    setVar $Move~DensityOnlyScanner 0
    setVar $PortCheck~DensityOnly 0
  else
    setVar $Move~DensityOnlyScanner 1
    setVar $PortCheck~DensityOnly 1
  end
  return

:disconnected
  # disconnected.  Wait for the prompt then restart
  killAllTriggers
  
  waitFor "Command [TL="
  goto :Menu_Go
  

# includes:
include "..\includes\aliases\pink-scripts-scripts3-include\header.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\worldTrade.ts"
