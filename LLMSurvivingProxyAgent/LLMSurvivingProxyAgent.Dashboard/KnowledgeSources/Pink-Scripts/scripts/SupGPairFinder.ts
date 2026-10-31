# Author           : SupG
# Description      : Reads CIM information and reports Port Pair Information
#                    to a file.
# Trigger Point    : Sector command prompt


#initialize script=-==--=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=--=-=-=-=
setVar $writefile GAMENAME & ".par"
delete $writefile
write $writefile "Type Perc  Fig Sec     Value"
write $writefile "------------------------------"
setVar $looper 0
setVar $leasttrade 0
setVar $goodie 0
#=-=-=-=-=-=-=-=-=-=-=-=-=-==-=-=-=-=-=-=-=-=-=-=-=-=-=-=-==-=-=-=-=

#gather information=-=-=-=-=-=--=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-

echo ANSI_6 "**-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 "SupGPPTFinder Ver. 2" ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-*"
echo ANSI_6 "-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 "  www.twtavern.com  " ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-**"

echo ANSI_15 "*Account for One Ways (Y/N)? (May error out with insufficient mapping data)"
getconsoleinput $onewaychk singlekey
uppercase $onewaychk
if ($onewaychk <> "Y")
	setVar $onewaychk "N"
end	
:info_gather
echo ANSI_13 "*Trade What?"
echo ANSI_15 "*1 - Organics & Equipment"
echo ANSI_15 "*2 - Fuel & Equipment"
echo ANSI_15 "*3 - Fuel & Organics"
echo ANSI_15 "*4 - SSM Pairs"
echo ANSI_15 "*5 - All Pairs"
getconsoleinput $sellboth singlekey
isNumber $chk $sellboth
if ($chk = "FALSE")
	goto :info_gather
end
if ($sellboth < 1) or ($sellboth > 5)
	goto :info_gather
end
if ($sellboth < 4)
:least
	getInput $leasttrade "Least amount on ports (in percent)?" 100
	isNumber $chk $leasttrade
	if ($chk = "FALSE")
		goto :lease
	end
	if ($leasttrade < 0) or ($leasttrade > 100)
		clientmessage "Invalid percentage"
		goto :least
	end
end
#=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=

#main function=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-==-=-=-=-=-=-=-=
:main
gosub :run_cim
gosub :fig_list
waitfor "(?="
echo ansi_15 "*Finding Port Pairs : Status (| = 10%)*"
echo "1%        100%* "
gosub :calculate
echo ansi_15 "*Done, info in file : " $writefile "*"
halt
#-=-=-=-=-=-=-=-=-=-=-=-=--=-==--=-=-=-=-=-=-=-=-=-=-=-=-=-=-==-=-=-

#subroutines=--=-=-=-=-=-=-=-=-=--=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
#cim sub
:run_cim
send "^rq"
waitfor ":"

:cim
setTextLineTrigger cim :cimmer
pause

:cimmer
setVar $info CURRENTLINE
getWord $info $end_test 1
if ($end_test = "0")
	goto :done
end
if ($end_test = ":")
	goto :cim
end
stripText $info "-"
stripText $info "%"

getWord $info $fuel_perc 3
getWord $info $fuel_amnt 2
getWord $info $orgs_perc 5
getWord $info $orgs_amnt 4
getWord $info $equi_perc 7
getWord $info $equi_amnt 6
getSector $end_test $test
setVar $class $test.port.class
gosub :filter
if ($nogood = 1)
	setVar $nogood 0
	goto :cim
end
setVar $bothsell 0
if ($sellboth = 1) or ($sellboth = 5)
	if ($orgs_perc >= $leasttrade) and ($equi_perc >= $leasttrade)
        add $bothsell 1
        if ($bothsell < 2)
			add $goodie 1
		end
		setVar $sector[$end_test] 1 & " (" & $fuel_amnt & "[" & $fuel_perc & "%]" & " / " & $orgs_amnt & "[" & $orgs_perc & "%]" & " / " & $equi_amnt & "[" & $equi_perc & "%]" & ") "
    		setVar $goodsector[$goodie] $end_test    	
	end
end
if ($sellboth = 2) or ($sellboth = 5)
	if ($fuel_perc >= $leasttrade) and ($equi_perc >= $leasttrade)
        add $bothsell 1
        if ($bothsell < 2)
			add $goodie 1
		end
		setVar $sector[$end_test] 1 & " (" & $fuel_amnt & "[" & $fuel_perc & "%]" & " / " & $orgs_amnt & "[" & $orgs_perc & "%]" & " / " & $equi_amnt & "[" & $equi_perc & "%]" & ") "
    		setVar $goodsector[$goodie] $end_test
	end
end
if ($sellboth = 3) or ($sellboth = 5)
	if ($orgs_perc >= $leasttrade) and ($fuel_perc >= $leasttrade)
        add $bothsell 1
        if ($bothsell < 2)
			add $goodie 1
		end
		setVar $sector[$end_test] 1 & " (" & $fuel_amnt & "[" & $fuel_perc & "%]" & " / " & $orgs_amnt & "[" & $orgs_perc & "%]" & " / " & $equi_amnt & "[" & $equi_perc & "%]" & ") "
		setVar $goodsector[$goodie] $end_test
	end
end
if ($sellboth = 4) or ($sellboth = 5)
   if ($test.port.buy_ore = "YES")
        add $bothsell 1
        if ($bothsell < 2)
			add $goodie 1
		end 
		setVar $sector[$end_test] 1 & " (" & $fuel_amnt & "[" & $fuel_perc & "%]" & " / " & $orgs_amnt & "[" & $orgs_perc & "%]" & " / " & $equi_amnt & "[" & $equi_perc & "%]" & ") "
    		setVar $goodsector[$goodie] $end_test
	end
end
goto :cim
:done
return

#calculate sub
:calculate
setVar $adjloop 0
if ($looper < $goodie)
	add $looper 1
	setVar $secs $goodie
	divide $secs 10
	if ($status = $secs)
        	setVar $status 0
		echo ansi_12 "|"
	end
    	add $status 1
	getsector $goodsector[$looper] $sec_info
:adjloop
	if ($adjloop < 7)
       		add $adjloop 1
		setVar $adjsector $sec_info.warp[$adjloop]
		if ($adjsector = 0)
        		setVar $sector[$goodsector[$looper]] 0
			goto :calculate
		end
        	getSector $adjsector $adjsecinfo
        	setVar $adjportclass $adjsecinfo.port.class
       		setVar $portclass $sec_info.port.class
        	getWord $sector[$adjsector] $test 1
		if ($test = 0)
                	setVar $sector[$adjsector] 0
			goto :adjloop
        	end
        	if ($onewaychk = "Y")
			getDistance $other $adjsector $goodsector[$looper]
    			if ($other > 1)
				goto :adjloop
			end		
       		end
	    	if ($sellboth = 1) or ($sellboth = 5)
           		setVar $message "O-E"
           		if ($portclass = 1) and ($adjportclass = 2) 
				gosub :write
			  	goto :1out
			end
			if ($portclass = 1) and ($adjportclass = 4) 
				gosub :write
				goto :1out
			end
			if ($portclass = 5) and ($adjportclass = 4) 
				gosub :write
				goto :1out
			end
			if ($portclass = 5) and ($adjportclass = 2) 
				gosub :write
				goto :1out
			end
			if ($portclass = 2) and ($adjportclass = 1) 
				gosub :write
				goto :1out
			end
			if ($portclass = 2) and ($adjportclass = 5) 
				gosub :write
				goto :1out
			end
			if ($portclass = 4) and ($adjportclass = 5) 
				gosub :write
				goto :1out
			end
			if ($portclass = 4) and ($adjportclass = 1) 
				gosub :write
				goto :1out
			end
		end
        :1out
		if ($sellboth = 2) or ($sellboth = 5)
			setVar $message "F-E"
           		if ($portclass = 1) and ($adjportclass = 3) 
				gosub :write
				goto :2out
			end
			if ($portclass = 1) and ($adjportclass = 4) 
				gosub :write
				goto :2out
			end
			if ($portclass = 6) and ($adjportclass = 3) 
				gosub :write
				goto :2out
			end
			if ($portclass = 6) and ($adjportclass = 4) 
				gosub :write
				goto :2out
			end
			if ($portclass = 3) and ($adjportclass = 1) 
				gosub :write
				goto :2out
			end
			if ($portclass = 3) and ($adjportclass = 6) 
				gosub :write
				goto :2out
			end
			if ($portclass = 4) and ($adjportclass = 1) 
				gosub :write
				goto :2out
			end
			if ($portclass = 4) and ($adjportclass = 6) 
				gosub :write
				goto :2out
			end
		end
        :2out
		if ($sellboth = 3) or ($sellboth = 5)
			setVar $message "F-O"
           		if ($portclass = 2) and ($adjportclass = 3) 
				gosub :write
				goto :3out
			end
			if ($portclass = 3) and ($adjportclass = 2) 
				gosub :write
				goto :3out
			end
			if ($portclass = 2) and ($adjportclass = 5) 
				gosub :write
				goto :3out
			end
			if ($portclass = 5) and ($adjportclass = 2) 
				gosub :write
				goto :3out
			end
			if ($portclass = 3) and ($adjportclass = 6) 
				gosub :write
				goto :3out
			end
			if ($portclass = 6) and ($adjportclass = 3) 
				gosub :write
				goto :3out
			end
			if ($portclass = 5) and ($adjportclass = 6) 
				gosub :write
				goto :3out
			end
			if ($portclass = 6) and ($adjportclass = 5) 
				gosub :write
				goto :3out
			end
		end
	:3out
		if ($sellboth = 4) or ($sellboth = 5)	
			setVar $message "SSM"
			if ($sec_info.port.buy_equip = "YES") and ($adjsecinfo.port.buy_equip = "YES")
				gosub :write
			end
		end
	goto :adjloop
	end
setVar $sector[$goodsector[$looper]] 0
goto :calculate
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

#filter sub
:filter
if ($class = 0) or ($class = 7)
	setVar $nogood 1
end
return

#write to file
:write
if ($figgies[$goodsector[$looper]] = 1)
	setVar $jumpfig1 "<#>"
else
	setVar $jumpfig1 "< >"
end
if ($figgies[$adjsector] = 1)
	setVar $jumpfig2 "<#>"
else
	setVar $jumpfig2 "< >"
end
setVar $pp $goodsector[$looper]
cutText $sector[$pp] $firstPort 2 9999
cutText $sector[$adjsector] $secondPort 2 9999
if ($message = "SSM")
	setVar $perc "SSM "
else
    if ($leastTrade = 100)
		setVar $perc "100%"
	elseif ($leastTrade < 10)
    	setVar $perc " " & $leastTrade & "%+"
	else
		setVar $perc $leastTrade & "%+"
	end
end
write $writefile $message & " |" & $perc & "| " & $jumpfig1 & " " & $goodsector[$looper] & $firstPort &" >>*           " & $jumpfig2 & " " & $adjsector & $secondPort
return
