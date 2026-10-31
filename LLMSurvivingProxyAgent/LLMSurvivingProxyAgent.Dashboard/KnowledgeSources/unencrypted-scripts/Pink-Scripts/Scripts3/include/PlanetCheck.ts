:PlanetCheck
sys_check
  
setVar $i 1
setVar $ignoreCount 0
:i
getWord $IgnoreList $ignore[$i] $i
if ($ignore[$i] <> 0)
add $i 1
add $ignoreCount 1
goto :i
end  
setVar $IgnoreList ""
  
setVar $found 0
send "l"
  
setTextLineTrigger noPlanet :noPlanet "There isn't a planet in this sector."
setTextLineTrigger multiplePlanets :multiplePlanets "Registry# and Planet Name"
setTextLineTrigger singlePlanet :singlePlanet "Landing sequence engaged..."
pause
  
:noPlanet
killTrigger multiplePlanets
killTrigger singlePlanet
return
  
:multiplePlanets
killTrigger singlePlanet
killTrigger noPlanet
setVar $LastID 0
:nextPlanet
setTextTrigger planetsChecked :PlanetsChecked "Land on which planet <Q to abort>"
setTextLineTrigger getID :getID "<"
pause
  
:getID
getWord CURRENTLINE $word 1
if ($word = "Owned")
setTextLineTrigger getID :getID "<"
pause
end
  
killTrigger planetsChecked
setVar $Line CURRENTLINE
stripText $Line "<"
stripText $Line ">"
getWord $Line $ID 1
if ($ID = "Land")
goto :PlanetsChecked
end
  
gosub :sub_CheckIgnore
  
if ($ID > $LastID) and ($ignore = 0)
send $ID "*"
setVar $LastID $ID
gosub :sub_Check
    
if ($found <> 0)  
return
end
    
send "ql"
waitFor "Registry# and Planet Name"
end
goto :nextPlanet
  
:PlanetsChecked
killTrigger getID
send "q*"
return
  
:singlePlanet
killTrigger multiplePlanets
killTrigger noPlanet
gosub :sub_Check
if ($found = 0)
send "q"
end
return
  
:sub_Check
setTextLineTrigger Check_GetPlanet :Check_GetPlanet "Planet #"
pause
:Check_GetPlanet
getWord CURRENTLINE $Check_Planet 2
stripText $Check_Planet "#"
    
setVar $ID $Check_Planet
gosub :sub_CheckIgnore
    
if ($ignore = 0)
gosub $planetCheckSub
      
if ($found = 1)
setVar $found $Check_Planet
end
end
    
return
:sub_CheckIgnore
setVar $j 1
setVar $ignore 0
:j
if ($j <= $ignoreCount)
if ($ignore[$j] = $ID)
setVar $ignore 1
else
add $j 1
goto :j
end
end
return
