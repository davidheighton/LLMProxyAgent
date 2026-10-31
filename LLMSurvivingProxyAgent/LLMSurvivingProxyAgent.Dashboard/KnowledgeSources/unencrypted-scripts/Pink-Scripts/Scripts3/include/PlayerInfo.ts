:InfoQuick
sys_check
  
send "/"
setVar $line ""
setTextLineTrigger line :line 
pause
:line
getWordPos CURRENTLINE $Pos #179
getWord CURRENTLINE $Test 1
  
if ($Pos = "0") and ($Test <> "Ship")
if ($line <> "")
return
end
setTextLineTrigger line :line 
pause
end
setVar $line CURRENTLINE
stripText $line ","
replaceText $line #179 " "
  
setVar $param 1
:Next
getWord $line $word $param
getWord $line $value ($param + 1)
  
if ($word = "0")
setTextLineTrigger line :line 
pause
end
  
if ($word = "Sect")
setVar $Sector $value
elseif ($word = "Turns")
setVar $Turns $value
elseif ($word = "Creds")
setVar $Credits $value
elseif ($word = "Figs")
setVar $Fighters $value
elseif ($word = "Shlds")
setVar $Shields $value
elseif ($word = "Hlds")
setVar $Holds $value
elseif ($word = "Ore")
setVar $Ore $value
elseif ($word = "Org")
setVar $Org $value
elseif ($word = "Equ")
setVar $Equip $value
elseif ($word = "Col")
setVar $Colonists $value
elseif ($word = "Phot")
setVar $Photons $value
elseif ($word = "Armd")
setVar $Mines $value
elseif ($word = "Lmpt")
setVar $Limpets $value
elseif ($word = "GTorp")
setVar $GenTorps $value
elseif ($word = "TWarp")
if ($value = "No")
setVar $TWarp "0"
else
setVar $TWarp $value
end
elseif ($word = "Clks")
setVar $Cloaks $value
elseif ($word = "Beacns")
setVar $Beacons $value
elseif ($word = "AtmDt")
setVar $Dets $value
elseif ($word = "Crbo")
setVar $Corbo $value
elseif ($word = "EPrb")
setVar $Probes $value
elseif ($word = "MDis")
setVar $Disruptors $value
elseif ($word = "PsPrb")
if ($value = "Yes")
setVar $PsiProbe "1"
else
setVar $PsiProbe "0"
end
elseif ($word = "PlScn")
if ($value = "Yes")
setVar $PlanetScanner "1"
else
setVar $PlanetScanner "0"
end
elseif ($word = "LRS")
if ($value = "None")
setVar $Scanner "0"
elseif ($value = "Holo")
setVar $Scanner "2"
else
setVar $Scanner "1"
end
elseif ($word = "Aln")
setVar $Align $value
elseif ($word = "Exp")
setVar $Experience $value
elseif ($word = "Corp")
setVar $Corp $value
elseif ($word = "Ship")
setVar $Ship $value
end
  
add $param 2
goto :Next
