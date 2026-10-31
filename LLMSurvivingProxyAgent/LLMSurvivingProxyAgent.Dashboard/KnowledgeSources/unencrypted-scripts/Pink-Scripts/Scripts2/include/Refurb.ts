:Refurb
sys_check
  
setVar $Failed "0"
if ($Method = 0)
setVar $Method E
end
    
setVar $Warp~Dest $RefurbPort
setVar $Warp~Method $Method
gosub :Warp~Warp
  
if (PORT.CLASS[$RefurbPort] = 9)
send "ps  gygqs  p"
else
send "pty"
end
  
waitOn "Commerce report for:"
setTextLineTrigger Cargo :Cargo "A  Cargo holds  "
setTextLineTrigger Fighters :Fighters "B  Fighters  "
setTextLineTrigger Shield :Shield "C  Shield Points  "
pause
  
:Cargo
getWord CURRENTLINE $CanBuyCargo 10
pause
  
:Fighters
getWord CURRENTLINE $FigPrice 4
getWord CURRENTLINE $CanBuyFigs 8
pause
  
:Shield
getWord CURRENTLINE $ShieldPrice 5
getWord CURRENTLINE $CanBuyShield 9
  
if ($CanBuyCargo < $HoldsLost)
setVar $Failed "1"
goto :ExitPort
end
  
send "a" $CanBuyCargo "*y"  
waitOn "Installing your next Cargo"
setTextLineTrigger Credits :Credits "You have "
pause
  
:Credits
getWord CURRENTLINE $Credits 3
stripText $Credits ","
  
if ($BuyFigs = "1")
setVar $x ($Credits - $CreditLimit)
    
if ($x > 0)
divide $x ($FigPrice * 2)
subtract $x 1
      
if ($x > 0)
send "b" $x "*"
subtract $Credits ($x * $FigPrice)
end
end
end
  
if ($BuyShield = "1")
setVar $x ($Credits - $CreditLimit)
    
if ($x > 0)
divide $x $ShieldPrice
subtract $x 1
      
if ($x > 0)
send "c" $x "*"
end
end
end
  
:ExitPort
if (PORT.CLASS[$RefurbPort] = 9)
send "qqq"
else
send "q"
end
  
return
  
:Menu
addMenu $Menu "Refurb" "Refurb Settings" "R" "" "Refurb" FALSE
addMenu "Refurb" "RefurbPort" "Refurb port (class 0/9)" "P" :Menu_RefurbPort "" FALSE
addMenu "Refurb" "BuyFigs" "Buy fighters" "F" :Menu_BuyFigs "" FALSE
addMenu "Refurb" "BuyShield" "Buy shield" "S" :Menu_BuyShield "" FALSE
addMenu "Refurb" "CreditLimit" "Fig/shield credit limit" "C" :Menu_CreditLimit "" FALSE
  
setMenuHelp "RefurbPort" "This option sets the port that the script will use for refurbs.  You may enter the sector number of any class 0/9 port that is not fortified by another player."
setMenuHelp "BuyFigs" "If this option is enabled, the script will buy fighters when it refurbs."
setMenuHelp "BuyShield" "If this option is enabled, the script will buy shield when it refurbs."
setMenuHelp "CreditLimit" "This option determines the amount of credits the script will always try to keep onhand when it refurbs.  The script will not buy fighters or shield in such a way that credits fall below this level.  At least 200k credits is strongly recommended."
  
gosub :sub_SetMenu
return
  
:Menu_RefurbPort
getInput $value "Enter class 0/9 port to refurb at"  
isNumber $test $value
if ($test = 0)
echo ANSI_15 "**Value must be a number*"
goto :Menu_RefurbPort
end
if ($value > SECTORS) or ($value < 1)
echo ANSI_15 "**Bad sector number*"
goto :Menu_RefurbPort
end
setVar $RefurbPort $value
saveVar $RefurbPort
gosub :sub_SetMenu
openMenu "Refurb"
  
:Menu_BuyFigs
if ($BuyFigs)
setVar $BuyFigs 0
else
setVar $BuyFigs 1
end
saveVar $BuyFigs
gosub :sub_SetMenu
openMenu "Refurb"
 
:Menu_BuyShield
if ($BuyShield)
setVar $BuyShield 0
else
setVar $BuyShield 1
end
saveVar $BuyShield
gosub :sub_SetMenu
openMenu "Refurb"
 
:Menu_CreditLimit
getInput $value "Enter credit limit when buying figs/shield"  
isNumber $test $value
if ($test = 0)
echo ANSI_15 "**Value must be a number*"
goto :Menu_CreditLimit
end
setVar $CreditLimit $value
saveVar $CreditLimit
gosub :sub_SetMenu
openMenu "Refurb"
  
:sub_SetMenu
setMenuValue "RefurbPort" $RefurbPort
setMenuValue "CreditLimit" $CreditLimit
  
if ($BuyFigs)
setMenuValue "BuyFigs" "ON"
else
setMenuValue "BuyFigs" "OFF"
end
  
if ($BuyShield)
setMenuValue "BuyShield" "ON"
else
setMenuValue "BuyShield" "OFF"
end
  
return  
  
include "include\warp"
