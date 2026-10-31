:StealDump
sys_check
  
setVar $Holds[$Sector] $experience
divide $Holds[$Sector] $stealFactor
subtract $Holds[$Sector] 1
if ($MaxEquip[$sector] = 0) or ($ReInit[$sector])
gosub :sub_SDInit
setVar $PortEquip[$sector] $SDInit_PortEquip
setVar $MaxEquip[$sector] $SDInit_MaxEquip
setVar $ShipHolds[$sector] $SDInit_Holds
    
if ($Holds[$Sector] > $ShipHolds[$Sector])
setVar $Holds[$Sector] $ShipHolds[$Sector]
end
end
if ($Holds[$Sector] > $ShipHolds[$Sector])
setVar $Holds[$Sector] $ShipHolds[$Sector]
end
  
if ($MaxEquip[$Sector] < $SDInit_Holds[$Sector])
setVar $success 2
return
end
  
if (($PortEquip[$sector] - 2) < $Holds[$Sector])
gosub :sub_SDSell
setVar $PortEquip[$sector] $MaxEquip[$sector]
end
send "pr*st3" $Holds[$sector] "*"
  
setTextLineTrigger GetTurns :GetTurns "One turn deducted,"
setTextLineTrigger NoTurns :NoTurns "After docking with the port"
pause
:GetTurns
killTrigger NoTurns
getWord CURRENTLINE $turns 4
:NoTurns
killTrigger GetTurns
  
setTextLineTrigger getEquipLeft :getEquipLeft "Equipment  Buying"
setTextLineTrigger fakeBust :Busted "Suddenly you're Busted!"
pause
:getEquipLeft
killTrigger fakeBust
getWord CURRENTLINE $PortEquip[$sector] 4
  
setTextLineTrigger Success :Success "Success!"
setTextLineTrigger Busted :Busted "Busted!"
setTextLineTrigger Failure :Failure "There aren't that many holds"
pause
  
:Failure
killTrigger Busted
killTrigger Success
send "o33*q"
goto :StealDump
  
:Success
killTrigger Busted
killTrigger Failure
setTextLineTrigger GetExp :GetExp "and you receive"
setTextTrigger NoExp :NoExp "Command [TL="
pause
  
:GetExp
getWord CURRENTLINE $expGain 4
add $experience $expGain
  
:NoExp
killTrigger GetExp
killTrigger NoExp
  
if ($planet <> "0")
send "l" $planet "*"
else
gosub :Land~Land
end
send "tnl3*q"
setVar $success 1
subtract $PortEquip[$sector] $Holds[$sector]
return
  
:Busted
killTrigger Success
killTrigger Failure
killTrigger getEquipLeft
setTextLineTrigger GetLost :GetLost "He arrives shortly"
pause
:GetLost
getWord CURRENTLINE $holdsLost 7
if ($holdsLost > 0)
end
  
setVar $MaxEquip[$sector] 0
  
setVar $success 0
return
:sub_SDInit
send "i"
setTextLineTrigger SDInit_GetHolds :SDInit_GetHolds "Total Holds    :"
pause
:SDInit_GetHolds
getWord CURRENTLINE $SDInit_Holds 4
setVar $SDInit_line CURRENTLINE
replaceText $SDInit_line "=" " "
getWord $SDInit_line $SDInit_Cargo 6
if ($SDInit_Cargo = "Equipment")
getWord $SDInit_line $SDInit_ShipEquip 7
else
setVar $SDInit_ShipEquip 0
end
if ($planet <> "0")
send "l" $planet "*"
else
gosub :Land~Land
end
setTextLineTrigger SDInit_getPlanetEquip :SDInit_getPlanetEquip "Equipment   "
pause
:SDInit_getPlanetEquip
getWord CURRENTLINE $SDInit_PlanetEquip 5
stripText $SDInit_PlanetEquip ","
  
if ($SDInit_ShipEquip > 0)
send "tnl3*"
add $SDInit_PlanetEquip $SDInit_ShipEquip
end
send "qcr*q"
  
waitFor "Commerce report for"
setTextLineTrigger SDInit_GetEquip :SDInit_GetEquip "Equipment  Buying"
pause
:SDInit_GetEquip
getWord CURRENTLINE $SDInit_EquipBuy 3
getWord CURRENTLINE $SDInit_EquipPerc 4
stripText $SDInit_EquipPerc "%"
  
if ($SDInit_EquipPerc = 0)
setVar $SDInit_PortEquip 1000
setVar $SDInit_MaxEquip 2000
setVar $ReInit[$sector] 1
return
else
setVar $ReInit[$sector] 0
end
  
setVar $x 10000
divide $x $SDInit_EquipPerc
multiply $x $SDInit_EquipBuy
divide $x 100
subtract $x 15
subtract $x $SDInit_EquipBuy
setVar $SDInit_PortEquip $x
  
setVar $SDInit_MaxEquip $SDInit_PortEquip
add $SDInit_MaxEquip $SDInit_PlanetEquip
  
return
:sub_SDSell
send "pn"
  
if ($planet <> "0")
send $planet "*"
goto :PlanetSell
end
  
waitFor "Registry# and "
setTextLineTrigger GetID :GetID "<"
pause
  
:GetID
setVar $line CURRENTLINE
stripText $line "<"
stripText $line ">"
getWord $line $ID 1
send $ID "*"
:PlanetSell  
setTextTrigger Fuel :NoSell "How many units of Fuel Ore do you want"
setTextTrigger Organics :NoSell "How many units of Organics do you want"
setTextTrigger Equipment :Sell "How many units of Equipment do you want"
pause
  
:NoSell
send "0*"
pause
  
:Sell
killTrigger Fuel
killTrigger Organics
gosub :sub_SDHaggle
  
if ($SDHaggle_abort = 1)
goto :sub_SDSell
end
  
return
:sub_SDHaggle
:Sell
send "*"
setVar $Abort 0
setVar $firstOffer 0
setVar $offerPerc 100
add $offerPerc $HaggleFactor
setTextLinetrigger SellDone :SellDone "You have "
setTextLineTrigger Abort :Abort "We're not interested"
:SellReset
killtrigger Offer
killtrigger FinalOffer
setTextLinetrigger Offer :SellOffer "We'll buy them for"
setTextLinetrigger FinalOffer :SellOffer "Our final offer"
pause
  
:SellOffer
if ($firstOffer = 0)
getWord CURRENTLINE $firstOffer 5
stripText $firstOffer ","
end
  
setVar $offer $firstOffer
multiply $offer $offerPerc
divide $offer 100
send $offer "*"
subtract $offerPerc 1
goto :SellReset
  
:SellDone
getWord CURRENTLINE $credits 3
killtrigger Offer
killtrigger FinalOffer
killTrigger Abort
return
  
:Abort
killtrigger Offer
killtrigger FinalOffer
killTrigger SellDone
setVar $Abort 1
return
  
  
include "include\land"
