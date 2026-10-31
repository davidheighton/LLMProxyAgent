:Land
sys_check
  
send "l"
  
setTextLineTrigger Land_SlowLanding :Land_SlowLanding "Registry#"
setTextLineTrigger Land_Landed :Land_Landing "Landing sequence engaged..."
pause
  
:Land_SlowLanding
setTextLineTrigger Land_GetID :Land_GetID "<"
pause
  
:Land_GetID
setVar $Land_line CURRENTLINE
stripText $Land_line "<"
stripText $Land_line ">"
getWord $Land_line $Land_PID 1
send $Land_PID "*"
  
:Land_Landing
killTrigger Land_SlowLanding
killTrigger Land_Landed
  
return
