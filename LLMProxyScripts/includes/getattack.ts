  
:GetAttack
sys_check
  
setVar $i 0
setVar $Attack "a"
:Next
if ($i < $GetTarget~Index)
setVar $Attack $Attack & "n"
add $i 1
goto :Next
end
return
