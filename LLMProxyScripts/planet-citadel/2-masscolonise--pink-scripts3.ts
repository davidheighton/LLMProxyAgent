setVar $Header~Script "Mass Colonise"

# check location
cutText CURRENTLINE $location 1 12
if ($location <> "Command [TL=")
        clientMessage "This script must be run from the command menu"
        halt
end

reqRecording
logging off

gosub :Header~Pack2Header

loadVar $MassColoniseSaved

if ($MassColoniseSaved)
  loadVar $MassColonise_EWarp
else
  setVar $MassColonise_EWarp 0
  saveVar $MassColonise_EWarp

  setVar $MassColoniseSaved 1
  saveVar $MassColoniseSaved
end

# create setup menu
addMenu "" "MassColonise" "Mass Colonise Settings" "." "" "Main" FALSE
addMenu "MassColonise" "GO" "GO!" "G" :Menu_Go "" TRUE
addMenu "MassColonise" "EWarp" "Allow E-Warp colonising" "E" :Menu_EWarp "" FALSE

setMenuHelp "EWarp" "If this option is enabled, this script will perform Express-Warp colonising if it runs out of fuel or for some reason is unable to T-Warp to fedspace."

gosub :sub_SetMenu

# open config menu
openMenu "MassColonise"

:Menu_EWarp
if ($MassColonise_EWarp)
  setVar $MassColonise_EWarp 0
else
  setVar $MassColonise_EWarp 1
end

saveVar $MassColonise_EWarp
gosub :sub_SetMenu
openMenu "MassColonise"

:Menu_Go
setEventTrigger disconnect :disconnected "Connection lost"
setVar $MassColonise~EWarp $MassColonise_EWarp
gosub :MassColonise~MassColonise
halt

:disconnected
  # disconnected.  Wait for the prompt then restart
  killAllTriggers
  setEventTrigger disconnect :disconnected "Connection lost"
  
  waitFor "Command [TL="
  goto :Menu_Go

:sub_SetMenu
  if ($MassColonise_EWarp)
    setMenuValue "EWarp" "YES"
  else
    setMenuValue "EWarp" "NO"
  end
  
  return
  

# includes:
include "..\includes\aliases\pink-scripts-scripts3-include\header.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\massColonise.ts"

