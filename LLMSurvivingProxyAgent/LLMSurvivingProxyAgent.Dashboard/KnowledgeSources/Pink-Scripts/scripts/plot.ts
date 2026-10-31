#
# Test Plot Read
#
getinput  $currentsector "From Sector:"
getinput  $destination "To Sector:"

:WarpPathBuilder
send "^f" $currentsector "*" $destination "*q"
setTextlinetrigger BuildPath :BuildPath $currentsector&" > "
setTextLineTrigger NoRoute :NoRoute "No route within"
pause
:BuildPath
setVar $distance 1
setVar $routeline CURRENTLINE
striptext $routeline " >"
striptext $routeline "("
striptext $routeline ")"

:PathLoop
getWord $routeline $warppath[$distance] $distance
if ($warppath[$distance] = $destination)
	Goto :Display
end
if ($warppath[$distance] = "0")
	settextlinetrigger NextLine :NextLine " "
	pause
end
add $distance 1
goto :PathLoop
:NextLine
setVar $moredistance 1
setVar $routeline CURRENTLINE
striptext $routeline " >"
striptext $routeline ")"
striptext $routeline "("
:PathLoop2
getWord $routeline $warppath[$distance] $moredistance
if ($warppath[$distance] = $destination)
	Goto :Display
end
if ($warppath[$distance] = "0")
	settextlinetrigger NextLine :NextLine " "
	Goto :End
end
add $moredistance 1
add $distance 1
goto :Pathloop2
:NextLine2
setVar $moredistance 1
setVar $routeline CURRENTLINE
striptext $routeline " >"
striptext $routeline ")"
striptext $routeline "("
:PathLoop3
getWord $routeline $warppath[$distance] $moredistance
if ($warppath[$distance] = $destination)
	Goto :Display
end
if ($warppath[$distance] = "0")
	Goto :End
end
add $moredistance 1
add $distance 1
goto :Pathloop3

:Display
echo ANSI_11 "**PATH: "
setvar $displayloop 1
:DisplayLoop
ECHO ANSI_15 ">" ANSI_12  $warppath[$displayloop] 
if ($displayloop = $distance)
	Goto :DisplayDone
end
add $displayloop 1
goto :DisplayLoop
:DisplayDone
echo "**"

:noRoute

:END