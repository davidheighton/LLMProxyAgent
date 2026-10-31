# Author           : SupG
# Description      : Reads CIM information and reports tunnel information
#                    Tunnel: group of sectors, with two warps out, linked together
#				     not necessarily closed off with a dead end.
# Trigger Point    : Sector command prompt

setVar $writefile GAMENAME & ".tun"
delete $writefile
write $writefile "Key:  + - fig in sector*      (XXX) - port type in sector"
write $writefile "----------------------------------------------------------**"


echo ANSI_6 "**-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 " SupGTunnelFinder   " ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-*"
echo ANSI_6 "-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 "  www.twtavern.com  " ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-**"

:info_gather
echo ANSI_13 "*Warp Info?"
echo ANSI_15 "*1 - Get From Game"
echo ANSI_15 "*2 - Get From File"
getconsoleinput $warpinf singlekey
isNumber $chk $warpinf
if ($chk = "FALSE")
	goto :info_gather
end
if ($warpinf < 1) or ($warpinf > 2)
	goto :info_gather
end
if ($warpinf = 2)
	getInput $filename "Filename? (export warp data from twx)" 0
end
:mindeep
getInput $mindeep "Minimum length of tunnel?" 0
isNumber $chk $mindeep
if ($chk = "FALSE")
	goto :mindeep
end
if ($mindeep < 1)
	goto :mindeep
end
#main function=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-==-=-=-=-=-=-=-=
if ($warpinf = 1)
	gosub :warpspec
else
	gosub :warpspecfile
end
gosub :fig_list
waitfor "(?="
echo ansi_15 "*Finding Tunnels : Status (| = 10%)*"
echo "1%        100%* "
gosub :calculate
echo ansi_15 "*Done, info in file : " $writefile "*"
halt
#-=-=-=-=-=-=-=-=-=-=-=-=--=-==--=-=-=-=-=-=-=-=-=-=-=-=-=-=-==-=-=-

#subroutines=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-==-=-
#warpspec update
:warpspec
send "^iq"
waitFor ":"

:spec
setTextLineTrigger spec :specs
pause

:specs
setVar $info CURRENTLINE
getWord $info $end_test 1
if ($end_test = 0)
	goto :done
end
if ($end_test = ":")
	goto :spec
end
getWord $info $warp1 2
getWord $info $warp2 3
getWord $info $warp3 4
if ($warp2 <> 0) and ($warp3 = 0)
	add $good 1
	setVar $twoWarps[$good] $end_test
	setVar $goodsectors[$end_test] 1 & " " & $warp1 & " " & $warp2
end
goto :spec

:done
return

:warpspecfile
setVar $speedy 1
setVar $filestat SECTORS
divide $filestat 10
setVar $updater $filestat
echo ansi_15 "*Reading File : Status (| = 10%)*"
echo "1%        100%* "
:readit
read $filename $info $speedy
if ($info <> "EOF")
    getWord $info $end_test 1
    if ($end_test > $updater)
		echo ansi_12 "|"
		add $updater $filestat
	end	
	getWord $info $warp1 2
	getWord $info $warp2 3
	getWord $info $warp3 4
	if ($warp2 <> 0) and ($warp3 = 0)
		add $good 1
		setVar $twoWarps[$good] $end_test
		setVar $goodsectors[$end_test] 1 & " " & $warp1 & " " & $warp2
	end
    add $speedy 1
	goto :readit
end
return

:fig_list
waitfor "Command [TL="
send "G"
:update
setVar $fig_sec 0
waitfor "==============="
:get_figs
getWord CURRENTLINE $sector 1
getWord CURRENTLINE $stopper 2
isNumber $secnum $sector
if ($secnum = "TRUE")
	setVar $figgies[$sector] 1
end
if ($stopper = "Total")
	goto :gotfigs
end
setTextLineTrigger grab :get_figs
pause

:gotfigs
return	

:calculate
setVar $pos 0
setVar $deep 1
setVar $1chked 0
setVar $off2nd 0
setVar $off1st 0
if ($counter < $good)
	add $counter 1
    setVar $secs $good
	divide $secs 10
    if ($status = $secs)
        setVar $status 0
		echo ansi_12 "|"
	end
    add $status 1
	setVar $goodone $goodsectors[$twoWarps[$counter]]
    setVar $orig $goodone
    setVar $prev $twoWarps[$counter]
	setVar $addsector $twoWarps[$counter]
	gosub :sector_info
	setVar $tunnel $addsector
:deep
    getWord $goodone $firstWarp 2
	getWord $goodsectors[$firstWarp] $firstchk 1
	getWord $goodone $secondWarp 3
	getWord $goodsectors[$secondWarp] $secondchk 1
    if ($firstchk = 1)
        add $deep 1
        setVar $addsector $firstWarp
		gosub :sector_info
		setVar $tunnel $addsector & " < " & $tunnel
        setVar $goodsectors[$prev] 0
		setVar $goodone $goodsectors[$firstWarp]
        setVar $goodsectors[$firstWarp] 0
        getWord $goodone $prev 2
		goto :deep
	elseif ($secondchk = 1) and ($goodone <> $orig)
        add $deep 1
        setVar $addsector $secondWarp
		gosub :sector_info
		setVar $tunnel $addsector & " < " & $tunnel
        setVar $goodone $goodsectors[$secondWarp]
        setVar $goodsectors[$prev] 0
		setVar $goodsectors[$secondWarp] 0
        getWord $goodone $prev 3
		goto :deep
	end
	setVar $goodone $goodsectors[$twoWarps[$counter]]
    setVar $orig $goodone
	setVar $prev $twoWarps[$counter]
:deep2
    getWord $goodone $firstWarp 2
	getWord $goodsectors[$firstWarp] $firstchk 1
	getWord $goodone $secondWarp 3
	getWord $goodsectors[$secondWarp] $secondchk 1
    if ($firstchk = 1) and ($goodone <> $orig)
        add $deep 1
        setVar $addsector $firstWarp
		gosub :sector_info
		setVar $tunnel $tunnel & " > " & $addsector
        setVar $goodone $goodsectors[$firstWarp]
        setVar $goodsectors[$prev] 0
		setVar $goodsectors[$firstWarp] 0
        getWord $goodone $prev 3
		goto :deep2
	elseif ($secondchk = 1)
        add $deep 1
        setVar $addsector $secondWarp
		gosub :sector_info
		setVar $tunnel $tunnel & " > " & $addsector
        setVar $goodone $goodsectors[$secondWarp]
        setVar $goodsectors[$prev] 0
		setVar $goodsectors[$secondWarp] 0
        getWord $goodone $prev 3
		goto :deep2
	end		
	if ($deep >= $mindeep)
        write $writefile $tunnel
	end
	goto :calculate
end

:sector_info
stripText $addsector "+"
getWord $addsector $addsector 1
getSector $addsector $addsecinf
if ($figgies[$addsector] = 1)
	setVar $addsector "+" & $addsector
end
if ($addsecinf.port.exists = 1)
	if ($addsecinf.port.class = 0)
    	setvar $porttype "(???)"
    elseif ($addsecinf.port.class = 1)
  		setvar $porttype "(BBS)"
    elseif ($addsecinf.port.class = 2)
    	setvar $porttype "(BSB)"
    elseif ($addsecinf.port.class = 3)
    	setvar $porttype "(SBB)"
    elseif ($addsecinf.port.class = 4)
        setvar $porttype "(SSB)"
    elseif ($addsecinf.port.class = 5)
        setvar $porttype "(SBS)"
    elseif ($addsecinf.port.class = 6)
        setvar $porttype "(BSS)"
    elseif ($addsecinf.port.class = 7)
        setvar $porttype "(SSS)"
    elseif ($addsecinf.port.class = 8)
        setvar $porttype "(BBB)"
    else
        setvar $porttype "(???)"
    end
	setVar $addsector $addsector & " " & $porttype
end
return

