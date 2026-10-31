:Rob
sys_check
  
send "pr*r"
  
setTextLineTrigger Fakebust :Bust "Suddenly you're Busted!"
setTextLineTrigger GetCash :GetCash "The Trade Journals estimate this port"
pause
  
:Bust
killTrigger GetCash
killTrigger Success
setTextLineTrigger GetLost :GetLost "He arrives shortly"
pause
:GetLost
getWord CURRENTLINE $holdsLost 7
if ($holdsLost > 0)
end
setVar $success 0
return
  
:GetCash
killTrigger Fakebust
getWord CURRENTLINE $PortCash 11
stripText $PortCash ","
multiply $PortCash 10
divide $PortCash 9
subtract $PortCash 1
  
if ($PortCash <= 0)
setVar $success 2
send "0*"
return
end
  
setVar $RobCash $experience
multiply $RobCash $robFactor
  
if ($RobCash > $PortCash)
setVar $RobCash $PortCash
end
  
send $RobCash "*"
  
setTextLineTrigger Bust :Bust "Suddenly you're Busted!"
setTextLineTrigger Success :Success "Success!"
pause
  
:Success
killTrigger Bust
  
setTextLineTrigger GetExp :GetExp "and you receive"
setTextTrigger NoExp :NoExp "Command [TL="
pause
  
:GetExp
getWord CURRENTLINE $expGain 4
add $experience $expGain
  
:NoExp
killTrigger GetExp
killTrigger NoExp
setVar $success 1
return
