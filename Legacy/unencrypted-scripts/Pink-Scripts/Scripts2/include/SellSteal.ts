:SellSteal
sys_check
  
if ($ShipHolds[$sector] = 0)
  
send "i"
setTextLineTrigger GetEquip :GetEquip "Total Holds    : "
pause
:GetEquip
getWord CURRENTLINE $ShipHolds[$sector] 4
setVar $line CURRENTLINE
replaceText $line "=" " "
getWord $line $word 6
if ($word = "Equipment")
getWord $line $ShipEquip[$sector] 7
else
setVar $ShipEquip[$sector] 0
end
    
send "cr*q"
  
waitFor "Commerce report for"
setTextLineTrigger GetPort :GetPort "Equipment  Buying"
pause
:GetPort
getWord CURRENTLINE $EquipBuy 3
getWord CURRENTLINE $EquipPerc 4
stripText $EquipPerc "%"
setVar $x 10000
if ($EquipPerc = 0)
setVar $PortEquip[$sector] ($ShipHolds[$sector] + 10)
else
divide $x $EquipPerc
multiply $x $EquipBuy
divide $x 100
subtract $x 1
subtract $x $EquipBuy
setVar $PortEquip[$sector] $x
end
end
  
if ($ShipEquip[$sector] > 0)
setVar $buyProd "None"
    
if ($Haggle~HaggleFactor = 0) and ($PortClass <> "0")
setVar $send "p t **"
      
if ($PortClass = 2) or ($PortClass = 3)
setVar $send $send & "0*"
elseif ($PortClass = 4)
setVar $send $send & "0*0*"
end
      
send $send
else
:RetrySell
send "p t"
gosub :Haggle~Haggle
if ($Haggle~abort = 1)
goto :RetrySell
end
end
    
add $PortEquip[$sector] $ShipEquip[$sector]
setVar $ShipEquip[$sector] 0
end
  
setVar $Holds[$sector] $experience
divide $Holds[$sector] $stealFactor
subtract $Holds[$sector] 1
  
if ($Holds[$sector] > $ShipHolds[$sector])
setVar $Holds[$sector] $ShipHolds[$sector]
end
if ($PortEquip[$sector] < $Holds[$sector])
setVar $Upgrade $Holds[$sector]
subtract $Upgrade $PortEquip[$sector]
add $PortEquip[$sector] $Upgrade
add $Upgrade 5
divide $Upgrade 10
    
if ($Upgrade > "0")
send "o 3" $Upgrade "* *"
end
end
  
:Steal
send "p r* st3" $Holds[$sector] "*"
waitOn "Enter your choice [T] ?"  
setTextLineTrigger getTurns :getTurns "One turn deducted,"
setTextLineTrigger Success :Success "Success!"
setTextLineTrigger Busted :Busted "Busted!"
setTextLineTrigger Failure :Failure "There aren't that many holds"
pause
  
:getTurns
getWord CURRENTLINE $Turns 4
pause
  
:Success
killTrigger getTurns
killTrigger Busted
killTrigger Failure
subtract $PortEquip[$sector] $Holds[$sector]
setVar $ShipEquip[$sector] $Holds[$sector]
  
setTextLineTrigger GetExp :GetExp "and you receive"
setTextTrigger NoExp :NoExp "Command [TL="
pause
  
:GetExp
getWord CURRENTLINE $ExpGain 4
add $experience $ExpGain
  
:NoExp
killTrigger GetExp
killTrigger NoExp
  
setVar $success 1
return
  
:Busted
killTrigger getTurns
killTrigger Success
killTrigger Failure
  
setTextLineTrigger GetLost :GetLost "He arrives shortly"
pause
:GetLost
getWord CURRENTLINE $holdsLost 7
if ($holdsLost > 0)
end
  
setVar $ShipHolds[$sector] 0
  
setVar $success 0
return
  
:Failure
killTrigger getTurns
killTrigger Success
killTrigger Busted
  
send "o 31* *"
goto :SellSteal
  
include "include\haggle"
