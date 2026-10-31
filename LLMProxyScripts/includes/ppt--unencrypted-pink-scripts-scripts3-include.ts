:PPT
sys_check
gosub :PlayerInfo~InfoQuick
  
setVar $ore $PlayerInfo~Ore
setVar $org $PlayerInfo~Org
setVar $equip $PlayerInfo~Equip
setVar $holds $PlayerInfo~Holds
setVar $displayOFF "0"
setVar $Aborted "0"
  
if ($Ore > 0)
setVar $OnHand "Fuel"
elseif ($Org > 0)
setVar $OnHand "Organics"
elseif ($Equip > 0)
setVar $OnHand "Equipment"
else
setVar $OnHand "None"
end
  
setVar $OneWay 0
  
send "cr*r" $SectorB "*q"
waitFor "Commerce report for"  
setTextLineTrigger GetSellProductA :GetSellProductA $ProdA
setTextLineTrigger GetBuyProductA :GetBuyProductA $ProdB
setTextTrigger GotProductA :GotProductA "Computer command"
pause
:GetSellProductA
setVar $line CURRENTLINE
stripText $line "Ore"
getWord $line $SellAmountA 3
pause
:GetBuyProductA
setVar $line CURRENTLINE
stripText $line "Ore"
getWord $line $BuyAmountA 3
pause
:GotProductA
  
waitOn "Commerce report for"  
setTextLineTrigger GetSellProductB :GetSellProductB $ProdB
setTextLineTrigger GetBuyProductB :GetBuyProductB $ProdA
setTextTrigger GotProductB :GotProductB "Computer command"
pause
:GetSellProductB
setVar $line CURRENTLINE
stripText $line "Ore"
getWord $line $SellAmountB 3
pause
:GetBuyProductB
setVar $line CURRENTLINE
stripText $line "Ore"
getWord $line $BuyAmountB 3
pause
:GotProductB
  
setVar $Trade 100
setVar $X 100
multiply $X $Holds
subtract $Trade $PercTrade
multiply $SellAmountA $Trade
multiply $SellAmountB $Trade
multiply $BuyAmountA $Trade
multiply $BuyAmountB $Trade
divide $SellAmountA $X
divide $SellAmountB $X
divide $BuyAmountA $X
divide $BuyAmountB $X
  
if ($SellAmountA <= 1) or ($SellAmountB <= 1) or ($BuyAmountA <= 1) or ($BuyAmountB <= 1)
setVar $Sector $SectorA
setVar $Aborted "1"
return
end
  
if ($SectorA <> STARDOCK) and ($SectorA > 10)
if ($DropFigs = 1)
send "f1*ct"
end
    
send "jy"
end
  
  
setVar $FirstRun 1
  
:PortA
  
send "pt"
if ($Haggle~HaggleFactor = 0)
if ($OnHand <> "None")
send "**"
end
if ($SellAmountA <= 0) or ($BuyAmountB <= 0)
if (PORT.CLASS[$SectorA] < 8)
send "0*"
end
if (PORT.CLASS[$SectorA] > 3)
send "0*"
end
else
if (PORT.BUYFUEL[$SectorA] = 0)
if ($ProdA = "Fuel")
send "**"
else
send "0*"
end
end
if (PORT.BUYORG[$SectorA] = 0)
if ($ProdA = "Organics")
send "**"
else
send "0*"
end
end
if (PORT.BUYEQUIP[$SectorA] = 0)
if ($ProdA = "Equipment")
send "**"
else
send "0*"
end
end
      
setVar $OnHand $ProdA
end
    
else
    
if ($SellAmountA <= 0) or ($BuyAmountB <= 0)
setVar $Haggle~BuyProd "none"
else
setVar $Haggle~BuyProd $ProdA
end
setVar $Haggle~Sector $SectorA
gosub :Haggle~Haggle
setVar $Credits $Haggle~Credits
if ($Haggle~Abort = 1)
goto :PortA
end
end
subtract $BuyAmountA 1
subtract $SellAmountA 1
  
if ($SellAmountA <= "-1") or ($BuyAmountB <= "-1")
setVar $Sector $SectorA
if ($Haggle~HaggleFactor = 0) and ($DisplayOFF)
send "cn 9 qq"
end
return
end
  
  
if ($SectorB < 600) or (SECTORS > 5000)
send $SectorB "*"
else
send $SectorB
end
  
if ($FirstRun = 1)
setVar $FirstRun 0
    
if ($Haggle~HaggleFactor = 0)
setVar $DisplayOFF 1
send "cn 9 qq"
end
if ($SectorB <> STARDOCK) and ($SectorB > 10) and ($DropFigs = 1)
send "f1*ct"
end
    
waitOn "Warping to Sector " & $SectorB
waitOn "Command [TL="
  
getSector $SectorB $SectorB
setVar $X 1
:X
if ($SectorB.warp[$X] = $SectorA)
goto :PortB
end
if ($SectorB.warp[$X] = 0)
  
if ($Haggle~HaggleFactor = 0) and ($DisplayOFF)
send "cn 9 qq"
end
setVar $OneWay 1
setVar $Sector $SectorB
      
if ($SectorB > 10) and ($SectorB <> STARDOCK)
send "jy"
end
return
end
add $X 1
goto :X
end
  
:PortB
send "pt"
if ($Haggle~HaggleFactor = 0)
if ($OnHand <> "None")
send "**"
end
if ($SellAmountB <= 0) or ($BuyAmountA <= 0)
if (PORT.CLASS[$SectorB] < 8)
send "0*"
end
if (PORT.CLASS[$SectorB] > 3)
send "0*"
end
else
if (PORT.BUYFUEL[$SectorB] = 0)
if ($ProdB = "Fuel")
send "**"
else
send "0*"
end
end
if (PORT.BUYORG[$SectorB] = 0)
if ($ProdB = "Organics")
send "**"
else
send "0*"
end
end
if (PORT.BUYEQUIP[$SectorB] = 0)
if ($ProdB = "Equipment")
send "**"
else
send "0*"
end
end
      
setVar $OnHand $ProdB
end
    
else
  
if ($SellAmountB <= 0) or ($BuyAmountA <= 0)
setVar $Haggle~BuyProd "none"
else
setVar $Haggle~BuyProd $ProdB
end
setVar $Haggle~Sector $SectorB
gosub :Haggle~Haggle
setVar $Credits $Haggle~Credits
if ($Haggle~Abort = 1)
goto :PortB
end
end
  
subtract $BuyAmountB 1
subtract $SellAmountB 1
  
if ($SellAmountB <= "-1") or ($BuyAmountA <= "-1")
if ($Haggle~HaggleFactor = 0) and ($DisplayOFF)
send "cn 9 qq"
end
setVar $Sector $SectorB
return
end
  
if ($SectorA < 600) or (SECTORS > 5000)
send $SectorA "*"
else
send $SectorA
end
  
  
goto :PortA
:Menu
addMenu $Menu "PPT" "PPT Settings" "P" "" "PPT" FALSE
addMenu "PPT" "DropFigs" "Drop figs under ports" "F" :Menu_DropFigs "" FALSE
addMenu "PPT" "PercTrade" "Trade to % of max" "T" :Menu_PercTrade "" FALSE
addMenu "PPT" "Haggle" "Haggling" "H" :Menu_Haggle "" FALSE
  
setMenuHelp "Dropfigs" "If this option is enabled, the script will drop a toll fighter under every port it trades at."
setMenuHelp "PercTrade" "This option adjusts how far the script will trade a port down before it determines it to no longer be profitable."
setMenuHelp "Haggle" "If this option is enabled, the script will haggle every trade for best price."
  
gosub :sub_SetMenu
return
  
:Menu_DropFigs
if ($DropFigs)
setVar $DropFigs 0
else
setVar $DropFigs 1
end
saveVar $DropFigs
gosub :sub_SetMenu
openMenu "PPT"
  
:Menu_PercTrade
getInput $value "Enter % of max to trade to"  
isNumber $test $value
if ($test = 0)
echo ANSI_15 "**Value must be a number*"
goto :Menu_PercTrade
end
if ($value > 100)
echo ANSI_15 "**Bad percentage*"
goto :Menu_PercTrade
end
setVar $PercTrade $value
saveVar $PercTrade
gosub :sub_SetMenu
openMenu "PPT"
  
:Menu_Haggle
if ($Haggle~HaggleFactor = 0)
setVar $Haggle~HaggleFactor 7
else
setVar $Haggle~HaggleFactor 0
end
saveVar $Haggle~HaggleFactor
gosub :sub_SetMenu
openMenu "PPT"
  
:sub_SetMenu
if ($DropFigs)
setMenuValue "DropFigs" "ON"
else
setMenuValue "DropFigs" "OFF"
end
  
if ($Haggle~HaggleFactor > 0)
setMenuValue "Haggle" "ON"
else
setMenuValue "Haggle" "OFF"
end
  
setMenuValue "PercTrade" $PercTrade
return  
include "..\includes\aliases\pink-scripts-scripts3-include\haggle.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\playerInfo.ts"
