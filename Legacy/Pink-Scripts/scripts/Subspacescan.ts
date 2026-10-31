setVar $bottomchan 0
setVar $topchan 60000
setVar $midchan 30000
setVar $upscan 0
setVar $downscan 60000
setVar $midupscan 30001
setVar $middownscan 30000
setVar $delaytime 1000


window SubspaceScanner 200 200 SubSpaceScanner ontop
:reset
	killalltriggers
	setTextLineTrigger rmsg :filterRmsg "R "
	setTextLineTrigger online :presskey "will be terminated"

:subspacescanloop

:midupscan
SEND "4" $midupscan "*"
if $midupscan = $topchan
	setvar $midupscan $midchan
end
waitFor "Settings command"
killTrigger delay
setDelayTrigger delay :middownscan $delaytime
pause

:middownscan
SEND "4" $middownscan "*"
if $middownscan = $bottomchan
	setVar $middownscan $midchan
end
waitFor "Settings command"
killtrigger delay
setDelayTrigger delay :upscan $delaytime
pause

:upscan
SEND "4" $upscan "*"
if $upscan = $topchan
	setVar $upscan $bottomchan
end
waitFor "Settings command"
killtrigger delay
setDelayTrigger delay :downscan $delaytime
pause

:downscan
SEND "4" $downscan "*"
if $downscan = $bottomchan
	setVar $downscan $topchan
end
waitFor "Settings command"
killtrigger delay
setDelayTrigger delay :addnumbers $delaytime
pause

:addnumbers
Add $upscan 1
Subtract $downscan 1
Add $midupscan 1
Subtract $middownscan 1

SetWindowContents SubSpaceScanner "Subspace Upscan: " & $upscan & "*Subspace Downscan:" & $downscan & "*Subspace MidUpscan: " & $midupscan & "*Subspace MidDownscan: " & $middownscan & "*"

GOTO :subspacescanloop

:filterRmsg
GOSUB :validate
setVar $targetline CURRENTLINE
write HITS.TXT $currentchan
write HITS.TXT $targetline
GOTO :reset


:validate
setVar $targetline CURRENTLINE
getWord CURRENTLINE $prompt 1
if ($prompt = "R")
	RETURN
else
	GOTO :reset
End


:presskey
SEND "#"
GOTO :reset


:END