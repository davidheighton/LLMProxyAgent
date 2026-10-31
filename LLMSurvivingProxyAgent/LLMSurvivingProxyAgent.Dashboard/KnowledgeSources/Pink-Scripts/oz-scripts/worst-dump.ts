# parm1 is dump type parm2 is cycle length
:start
send "'OZ-BOT: Activating OZ Buy Down - Worst Price*"
waitFor "(?="
cutText CURRENTLINE $location 1 7
striptext $location " "
if ($location = "Citadel")
	goto :get_info
else
	echo ANSI_12 "**NEED TO BE AT CIT PROMPT!!**"
	send "'OZ-BOT: Wrong Prompt..*"
	send "'OZ-BOT: Done Run*"
	send "'OZ-BOT: OZ Buy Down - Worst Price Powering Down...*"
	halt
end

:get_info
setVar $count 0
setVar $percent 45
setVar $avoids 0
# send "cn7qq"
send "i"
	setTextTrigger holds :getHolds "Total Holds    :"
	pause
	:getHolds
	getWord CURRENTLINE $numHolds 4
send "q"
:getplanetnum
send "D"
setTextLineTrigger pnum :pnum "Planet #"
pause

:pnum
getWord CURRENTLINE $planet 2
stripText $planet "#"
send "tnl1*tnl2*tnl3*snl1*"
send "q"
send "d"
waitFor "Sector  :"
getWord CURRENTLINE $dumpsec 3
getSector $dumpsec $secinfo
send "c"
:avoid
add $avoids 1
if ($avoids > $secinfo.warps)
	send "q"
	goto :next
end
send "v"
send $secinfo.warp[$avoids] "*"
goto :avoid

:next

:sub_Trade
if ($parm2 <> 0) and ($count = $parm2)
	goto :done_buy
end
killTrigger 0
killTrigger 1
killTrigger 2
killTrigger 3
killTrigger 4
send "pt"
:letssee
setVar $buyPerc 100
add $buyPerc $percent
waitFor "do you want to buy"


:getTradeType
getWord CURRENTLINE $tradeType 5
getText CURRENTLINE $sellHolds "[" "]"

	if ($tradeType = "Fuel") and ($parm1 = 1)
		send "*"
		goto :reverse
	elseif ($tradeType = "Organics") and ($parm1 = 2)
		send "*" 	
		goto :reverse
	elseif ($tradeType = "Equipment") and ($parm1 = 3)
		send "*" 	
		goto :reverse
	elseif ($tradeType = "Fuel") and ($parm1 = 2) or ($parm1 = 3) 
		send "0*"
		goto :letssee
	elseif ($tradeType = "Organics") and ($parm1 = 3)
		send "0*"
		goto :letssee
	end
	
:reverse
if ($sellHolds < $numHolds )
	goto :done_Buy
end		
# haggle it!
:haggle
setTextTrigger 3 :dump "and 0 empty cargo holds"
setTextTrigger 4 :sub_Trade "We're not interested."
killTrigger 7
waitFor "Your offer"
        

:Haggle_Buy
getWord CURRENTLINE $startPrice 3
stripText $startPrice ","
stripText $startPrice "["
stripText $startPrice "]"

:sub_Buy
setVar $price $startPrice
multiply $price $buyPerc
divide $price 100
send $price "*"
subtract $buyPerc 1
killtrigger 3
killTrigger 4
goto :haggle

:dump
killTrigger slow
add $count 1
send "l" $planet "*tnl" $parm1 "*q"
waitFor "Command [TL"
goto :sub_Trade

:done_Buy	
send "0*0*0*"
send "cv0*yyq"
# send "cn7qq"
send "'OZ-BOT: Done Run*"
halt	