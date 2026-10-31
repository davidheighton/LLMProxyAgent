# get the sector we're in
send "d" 
setTextLineTrigger GetSector :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $currentsector 3


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

echo "*[ Warps : " $line " ]*"
#move to each warp 
setVar $i 1
:MOVEAGAIN
if ($i <= 7)
if ($Warp[$i] <> 0)
if (1 > 0)
send $Warp[$i] "*f1*ct" $CurrentSector "*"
else
send $Warp[$i] "*f0*" $CurrentSector "*"
end
end
Add $i 1
Goto :MOVEAGAIN
end
#drop current line
if ("Y" = "Y")
if (1 > 0)
send "f1*ct"
else
send "f0*"
endif
end 