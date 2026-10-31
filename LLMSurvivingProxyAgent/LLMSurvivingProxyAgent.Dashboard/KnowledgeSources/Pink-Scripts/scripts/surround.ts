# Are we at the correct prompt?
cutText CURRENTLINE $location 1 7
if ($location <> "Command")
clientMessage "This script must be run from the game command menu"
halt
end


# ASK: Number of fighters to leave in each sector
:ASKFIGHTERS
getInput $numFighters "*How many fighters to drop per sector? "
isNumber $result $numFighters
if ($results = "TRUE")
Goto :ASKFIGHTERS
end


# ASK: (T)oll (D)efensive (O)ffensive
:ASKSET
if ($numFighters = 0)
Goto :ASKCURRENT
end
Echo "*Set them as (T)oll (D)efensive (O)ffensive :"
getConsoleInput $setFighters SINGLEKEY
if ($setFighters <> "T") and ($setFighters <> "D") and ($setFighters <> "O")
Goto :ASKSET 
end


# ASK: Include current sector?
:ASKCURRENT
Echo "*Drop in current sector also (Y/N):"
getConsoleInput $dropCurrent SINGLEKEY
if ($dropCurrent <> "Y") and ($dropCurrent <> "N")
Goto :ASKCURRENT
end


#determine current sector
send "*"
setTextLineTrigger GetSector :GetSector "Sector :"
pause
:GetSector
getWord CURRENTLINE $CurrentSector 3


#determine warps out of sector
setTextLineTrigger GetWarps :GetWarps "Warps to Sector(s) :"
send "*"
pause
:GetWarps
setVar $line CURRENTLINE
stripText $line "Warps to Sector(s) :"
stripText $line "("
stripText $line ")"
stripText $line "-"
setVar $numWarps 0
:FINDWARPSLOOP
if ($numWarps < 7)
Add $numWarps 1 
getWord $line $Warp[$numWarps] $numWarps
Goto :FINDWARPSLOOP
end


#move to each warp 
setVar $i 1
:MOVEAGAIN
if ($i <= 7)
if ($Warp[$i] <> 0)
if ($numFighters > 0)
send $Warp[$i] "*f" $numFighters "*c" $setFighters $CurrentSector "*"
else
send $Warp[$i] "*f0*" $CurrentSector "*"
end
end
Add $i 1
Goto :MOVEAGAIN
end
if ($dropCurrent = "Y")
if ($numFighters > 0)
send "f" $numFighters "*c" $setFighters 
else
send "f0*"
endif
end 

