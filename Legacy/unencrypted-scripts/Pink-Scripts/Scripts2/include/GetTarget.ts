:GetTarget
sys_check
  
send "d"
waitFor "Sector  : "
setVar $Index 0
setVar $Found 0
setTextLineTrigger AddIndex :AddIndex " ftrs,"
setTextLineTrigger BeginAttack :BeginAttack "Warps to Sector(s)"
pause
:AddIndex
getWord CURRENTLINE $Word 1
if ($Word = "Federals:")
setVar $Pos "federals"
elseif ($Word = "Traders") or ($Word = "Aliens")
setVar $Pos "traders"
elseif ($Word = "Ships")
setVar $Pos "ships"
end
if ($Pos = "federals") or ($Pos = "ships")
add $Index 1
elseif ($Pos = "traders")
getWordPos CURRENTLINE $isTarget $Target
if ($isTarget <> 0)
setVar $Pos "none"
setVar $Found 1
getWordPos CURRENTLINE $Start ", w/ "
getWordPos CURRENTLINE $Length " ftrs,"
add $Start 5
subtract $Length $Start
cutText CURRENTLINE $Figs $Start $Length
stripText $Figs ","
setTextLineTrigger getShip :getShip " in "
pause
:getShip
getWordPos CURRENTLINE $Start " ("
getWordPos CURRENTLINE $Length ")"
add $Start 2
subtract $Length $Start
cutText CURRENTLINE $Ship $Start $Length
else
add $Index 1
end
end
  
setTextLineTrigger AddIndex :AddIndex " ftrs,"
pause
:BeginAttack
killTrigger AddIndex
if ($Found = 0)
setVar $Index ""
end
  
return
