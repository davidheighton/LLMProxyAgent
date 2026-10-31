:StripCorp
sys_check
  
getLength $Player $Len
  
if ($Len < 3)
return
end
  
cutText $Player $CorpData $Len 1
  
if ($CorpData = "]")
subtract $Len 3
cutText $Player $CorpData $Len 99
getWord $CorpData $CorpData 1
StripText $Player " " & $CorpData
StripText $CorpData "["
StripText $CorpData "]"
end
  
return
