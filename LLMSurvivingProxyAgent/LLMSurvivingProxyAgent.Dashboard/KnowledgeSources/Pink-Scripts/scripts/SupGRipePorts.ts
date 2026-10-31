# TWX SupG Script  : SupGRipePorts
# Author           : SupG
# Description      : Script reads CIM data and reports via subspace or corporate
#                    memo, ports that are ripe for buydown/ptrading when upgraded.
#                    Returns ports selling more than 30000 equip and/or orgs
#		     Returns ports buying more than 20000 equip and/or orgs
# Trigger Point    : Command prompt
# Other		   : This script can be improved upon, it was written rather quickly


:porttype
echo ANSI_15 "*Send port info to SubSpace or Corporate Memo? [s/c]*"
getconsoleinput $reporttype singlekey
uppercase $reporttype
if ($reporttype <> "S") and ($reporttype <> "C")
	goto :porttype
end 
if ($reporttype = "C")
	cutText CURRENTLINE $location 1 12
	if ($location <> "Command [TL=")
        	clientMessage "This script must be run from the command menu"
        	halt
	end
end

setVar $ripebuyports 0
setVar $ripesellports 0
send "^rq"
gosub :get_cim


if ($ripebuyports > 0)
	if ($reporttype = "S")
		send "'*"
	else
		send "tt"
	end
	setVar $uloop 0
:uloop
	if ($uloop < $ripebuyports)
		add $uloop 1
		send "Port " $ripe_buy[$uloop] " is ready for buydown.*"
		goto :uloop
	end
	send "*"
end
if ($reporttype = "C")
	send "q"
end
if ($ripesellports > 0)
	setVar $mloop 0
	if ($reporttype = "S")
		send "'*"
	else
		send "tt"
	end
:mloop
	if ($mloop < $ripesellports)
		add $mloop 1
		send "Port " $ripe_sell[$mloop] " is buying*"
		goto :mloop
	end
	send "*"
end
if ($reporttype = "C")
	send "q"
end
halt

:get_cim
	 setVar $colon 0	 
	 setVar $used_port_count 0
 	 setVar $Cim_file_place 1
	 waitfor ":"
:cim
	 setVar $spanky 0 
	 setTextLineTrigger cim :cimmer
	 pause
:cimmer
 	setVar $info CURRENTLINE
	getWord $info $end_test 1
        if ($end_test = "0")
		goto :done
	end
	if ($end_test = ":")
		add $colon 1
		if ($colon > 1)
			goto :done
		else
			goto :cim
		end
	end
	stripText $info "-"
	stripText $info "%"
	getWord $info $fuel 2
	getWord $info $orgs 4
	getWord $info $equi 6
	getSector $end_test $sec_info
	if ($fuel > 30000) or ($orgs > 30000) and ($equi > 30000) and ($sec_info.port.buy_equip = "NO")
		add $ripebuyports 1
		setVar $ripe_buy[$ripebuyports] $end_test
	end
	if ($orgs > 20000) and ($sec_info.port.buy_orgs = "YES") or ($equi > 20000) and ($sec_info.port.buy_equip = "YES")
		add $ripesellports 1
		setVar $ripe_sell[$ripesellports] $end_test
	end
	goto :cim
:done
return