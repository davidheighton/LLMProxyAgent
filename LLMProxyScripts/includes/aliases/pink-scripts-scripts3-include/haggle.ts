:Haggle
sys_check
  
setVar $abort 0
  
if ($BuyFactor[$Sector] = 0)
setVar $BuyFactor[$Sector] ($HaggleFactor * 10)
end
if ($SellFactor[$Sector] = 0)
setVar $SellFactor[$Sector] ($HaggleFactor * 10)
end
  
waitOn "Docking..."
setTextLineTrigger sell :sell "We are buying up to "
setTextLineTrigger buy :buy "We are selling up to "
setTextTrigger done :done "Command [TL="
pause
:sell
killTrigger done
send "*"
setVar $awaitingQuote 0
setVar $firstOffer 0
setVar $offerPerc (1000 + $SellFactor[$Sector])
:SellReset
killTrigger line
killTrigger offer
setTextLineTrigger line :sellLine
setTextTrigger offer :sellOffer "Your offer ["
pause
  
:sellLine
getWord CURRENTLINE $test 1
# One offer per server reply: echoed numbers and repeated offer prompts are not replies.
getWord CURRENTLINE $replyWord2 2
isNumber $echoOnly $test
getWordPos CURRENTLINE $offerPromptPos "Your offer"
if ((($echoOnly = 0) or ($replyWord2 <> 0)) and ($offerPromptPos = 0))
setVar $awaitingQuote 0
end
  
if ($test = 0)
setVar $lastLineBlank 1
else
setVar $lastLinkBlank 0
  
if (CURRENTLINE = "We're not interested.")
goto :abort
else
cutText CURRENTLINE $test 1 12
    
if ($test = "Command [TL=")
goto :done
else
cutText CURRENTLINE $test 1 9
        
if ($test = "You have ")
goto :sellDone
end  
end
end
end  
  
setTextLineTrigger line :sellLine
pause 
  
:sellOffer
# Answer each quote once; the same prompt is also reported when its line completes.
if ($awaitingQuote = 1)
setTextTrigger offer :sellOffer "Your offer ["
pause
end
if ($firstOffer = 0)
getWord CURRENTLINE $firstOffer 3
stripText $firstOffer "["
stripText $firstOffer "]"
stripText $firstOffer ","
end
  
setVar $offer $firstOffer
multiply $offer $offerPerc
divide $offer 1000
send $offer "*"
setVar $awaitingQuote 1
if ($SellFactor[$Sector] < 0)
if ($SellDone[$Sector])
add $offerPerc 5
add $SellFactor[$Sector] 5
else
add $offerPerc 10
add $SellFactor[$Sector] 10
end
else
if ($SellDone[$Sector])
subtract $offerPerc 3
subtract $SellFactor[$Sector] 3
else
subtract $offerPerc 10
subtract $SellFactor[$Sector] 10
end
end
goto :sellReset
  
:sellDone
getWord CURRENTLINE $test 3
if ($test <> "been")
setVar $Credits $test
stripText $Credits ","
end
killTrigger offer
killTrigger line
setVar $SellDone[$Sector] 1
if ($SellFactor[$Sector] < 0)
subtract $SellFactor[$sector] 4
else
add $SellFactor[$Sector] 6
end
setTextTrigger done :done "Command [TL="
pause
  
:buy
killTrigger done
  
waitOn "do you want to buy ["
getWord CURRENTLINE $Product 5
if ($Product <> $buyProd)
send "0*"
setTextLineTrigger Buy :Buy "We are selling up to "
setTextTrigger done :done "Command [TL="
pause
end
  
# The default quantity is credit-limited at the listed price, but the negotiated quote can exceed the credits.
# After an unaffordable quote this port's purchases are capped (see :buyOffer) instead of re-offering forever.
getText CURRENTLINE $defaultQty "[" "]"
stripText $defaultQty ","
isNumber $isQty $defaultQty
if ($isQty = 0)
setVar $defaultQty 0
end
setVar $tooPoor 0
setVar $awaitingQuote 0
if ($Quantity > 0)
send $Quantity "*"
elseif (($BuyQtyLimit[$Sector] > 0) and ($BuyQtyLimit[$Sector] < $defaultQty))
send $BuyQtyLimit[$Sector] "*"
else
send "*"
end
    
setVar $firstOffer 0
setVar $offerPerc (1000 - $BuyFactor[$Sector])
:buyReset
killTrigger line
killTrigger offer
setTextLineTrigger line :buyLine
setTextTrigger offer :BuyOffer "Your offer ["
pause
  
:buyLine
getWord CURRENTLINE $test 1
# One offer per server reply: echoed numbers and repeated offer prompts are not replies.
getWord CURRENTLINE $replyWord2 2
isNumber $echoOnly $test
getWordPos CURRENTLINE $offerPromptPos "Your offer"
if ((($echoOnly = 0) or ($replyWord2 <> 0)) and ($offerPromptPos = 0))
setVar $awaitingQuote 0
end
  
if ($test = 0)
setVar $lastLineBlank 1
else
setVar $lastLinkBlank 0
  
getWordPos CURRENTLINE $poorPos "You only have"
if ($poorPos > 0)
getWord CURRENTLINE $haveCredits 4
stripText $haveCredits ","
setVar $tooPoor 1
end
  
if (CURRENTLINE = "We're not interested.")
goto :Abort
else
cutText CURRENTLINE $test 1 12
    
if ($test = "Command [TL=")
goto :Done
else
cutText CURRENTLINE $test 1 9
        
if ($test = "You have ")
goto :BuyDone
end  
end
end
end  
  
setTextLineTrigger line :buyLine
pause
  
:buyOffer
if ($lastLinkBlank)
setTextTrigger offer :buyOffer "Your offer ["
pause
end
  
# Answer each quote once: the same prompt is also reported when its line completes, and a second answer arrived
# at the Command prompt as a sector number.
if ($awaitingQuote = 1)
setTextTrigger offer :buyOffer "Your offer ["
pause
end
  
if ($tooPoor = 1)
setVar $tooPoor 0
setVar $awaitingQuote 0
setVar $limit 0
if (($defaultQty > 0) and ($firstOffer > 0))
setVar $limit $defaultQty
multiply $limit $haveCredits
multiply $limit 90
divide $limit $firstOffer
divide $limit 100
end
if ($limit < 1)
setVar $limit 1
end
setVar $BuyQtyLimit[$Sector] $limit
# An offer of 0 declines the purchase and returns to the port menu.
send "0*"
setVar $awaitingQuote 1
goto :BuyReset
end
  
if ($firstOffer = 0)
getWord CURRENTLINE $firstOffer 3
stripText $firstOffer "["
stripText $firstOffer "]"
stripText $firstOffer ","
end
  
setVar $offer $firstOffer
multiply $offer $offerPerc
divide $offer 1000
send $offer "*"
setVar $awaitingQuote 1
if ($BuyFactor[$Sector] < 0)
if ($BuyDone[$Sector])
subtract $offerPerc 5
add $BuyFactor[$Sector] 5
else
subtract $offerPerc 10
add $BuyFactor[$Sector] 10
end
else
if ($BuyDone[$Sector])
add $offerPerc 3
subtract $BuyFactor[$Sector] 3
else
add $offerPerc 10
subtract $BuyFactor[$Sector] 10
end
end
goto :BuyReset
  
:buyDone
getWord CURRENTLINE $test 3
if ($test <> "been")
setVar $Credits $test
stripText $Credits ","
end
killTrigger offer
killTrigger line
setVar $BuyDone[$Sector] 1
  
if ($BuyFactor[$Sector] < 0)
subtract $BuyFactor[$sector] 4
else
add $BuyFactor[$Sector] 6
end
  
setTextLineTrigger buy :buy "We are selling up to "
setTextTrigger done :done "Command [TL="
pause
  
:abort
setVar $abort 1
killTrigger buy
killTrigger sell
killTrigger done
killTrigger line
killTrigger offer
setTextLineTrigger buy :buy "We are selling up to "
setTextLineTrigger sell :sell "We are buying up to "
setTextTrigger done :done "Command [TL="
pause
:Done
killTrigger abort
killTrigger sell
killTrigger buy
setVar $Quantity 0
return
