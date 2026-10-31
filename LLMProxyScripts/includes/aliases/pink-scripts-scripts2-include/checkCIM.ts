:CheckCIM
sys_check
  
send "^rq"
setVar $i 1
setVar $FoundChange 0
:redoPause
setTextTrigger pause2 :pause2 ": "
pause
:pause2
getWord CURRENTLINE $test 1
if ($test <> ":")
goto :redoPause
end
setTextLineTrigger savePort :savePort "%"
pause
:savePort
setVar $line CURRENTLINE
stripText $line "-"
stripText $line "%"
getWord $line $sector 1
getWord $line $product1 3
getWord $line $product2 5
getWord $line $product3 7
setVar $line $sector & " " & $product1 & " " & $product2 & " " & $product3
write $CIMLogFile $line
getDate $date
getTime $time
  
if ($Init = 1)
read "_" & $CIMLogFile $oldPort $i
getWord $oldPort $oldSector 1
getWord $oldPort $oldProduct1 2
getWord $oldPort $oldProduct2 3
getWord $oldPort $oldProduct3 4
setVar $line ""
    
setVar $repSector $sector
if ($oldSector <> $sector) and ($oldSector <> "EOF")
setVar $sound 1
if ($oldSector > $sector)
setVar $line "CIM: Port query opened to " & $sector
setVar $FoundChange 1
subtract $i 1
else
setVar $line "CIM: Port query closed to " & $oldSector
setVar $repSector $oldSector
setVar $FoundChange 1
add $i 1
end
      
goto :add
end
if ($product1 < $oldProduct1)
setVar $line "CIM: Fuel ore reduced from " & $oldProduct1 & " to " & $product1 & " in " & $sector
setVar $sound 1
end
if ($product2 < $oldProduct2)
setVar $line "CIM: Organics reduced from " & $oldProduct2 & " to " & $product2 & " in " & $sector
setVar $sound 1
end
if ($product3 < $oldProduct3)
setVar $line "CIM: Equipment reduced from " & $oldProduct3 & " to " & $product3 & " in " & $sector
setVar $sound 1
end
:add
    
if ($line <> "")
if (PORT.BUYFUEL[$repSector])
setVar $line $line & " (B"
else
setVar $line $line & " (S"
end
    
if (PORT.BUYORG[$repSector])
setVar $line $line & "B"
else
setVar $line $line & "S"
end
    
if (PORT.BUYEQUIP[$repSector])
setVar $line $line & "B)"
else
setVar $line $line & "S)"
end
    
if ($Broadcast = "1")
send "'" $line "*"
end
if ($LogFile <> "0")
write $LogFile $date & " " & $time & " - " & $line
end
setVar $FoundChange 1
end
    
add $i 1
end
  
killTrigger portsSaved
setTextLineTrigger savePort :savePort "%"
setTextTrigger portsSaved :portsSaved ": "
pause
:portsSaved
killTrigger checkFailed
killTrigger savePort
  
if ($sound = 1)
sound BASEUSE.wav
end
  
setVar $Init 1
setVar $sound 0
delete "_" & $CIMLogFile
rename $CIMLogFile "_" & $CIMLogFile
return
