:CacheFigs
sys_check
setArray $Figs SECTORS
send "g"
  
setTextLineTrigger FigListStart :FigListStart "========================"
pause
  
:FigListStart
setTextLineTrigger FigListLine :FigListLine
pause
  
:FigListLine
if (CURRENTLINE = "")
return
end
  
getWord CURRENTLINE $Fig 1
getWord CURRENTLINE $Test 2
if ($Test <> "Total")
setVar $Figs[$Fig] 1
end
goto :FigListStart
:CacheFigsFromFile
sys_check
  
if ($Fig <> "0")
setVar $i 1
:ClearNext
if ($i <= $Fig)
setVar $Figs[$i] 0
add $i 1
goto :ClearNext
end
end
  
setVar $FigIndex 1
  
:NextFig
read $FigFile $Fig $FigIndex
  
if ($Fig = EOF)
return
end
  
setVar $Figs[$Fig] 1
add $FigIndex 1
goto :NextFig
  
  
