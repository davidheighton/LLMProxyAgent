# Script Name      : SupGCimHunter
# Author           : SupG
# Description      : Attempts to hunt down traders by reporting over Subspace radio,
#                    changes in port reports.  Best used with a partner, one person
#		     running the script, and the other tracking down the target.
# Trigger Point    : Sector command prompt
# Warnings         : This script spends the vast majority of it's runtime in interregation
#                    mode, you will not receive game messages at that time.  
# Other            : You are free to modify this script to your liking, but please give me
#                    credit for any of my code that you use.


setVar $cim_file GAMENAME & ".cim"
setVar $cim_temp "cim.tmp"
setVar $log_file GAMENAME & ".hnt"
fileExists $file_check $cim_temp
if ($file_check = "TRUE")
	delete $cim_temp
end
fileExists $file_check $cim_file
if ($file_check = "FALSE")
	setVar $new_file 1
	goto :get_cim
end

:wait
:get_cim
	 setVar $colon 0
	 
	 setVar $used_port_count 0
 	 setVar $Cim_file_place 1
	 send "^R"
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
	if ($new_file = 1)
	       	stripText $info "-"
		write $cim_file $info
 		goto :cim
	else
		stripText $info "-"
		write $cim_temp $info
:read_file
		read $cim_file $cim_info $cim_file_place
		setVar $sector_info 2
		if ($cim_info = "EOF")
			setVar $cim_info_sector 50000
		else
			getWord $cim_info $cim_info_sector 1
		end
		getWord $info $info_sector 1
		if ($cim_info_sector = $info_sector)
			if ($info_sector = 0)
				goto :done
			end
			getWord $cim_info $cim_info_fuel 3
			getWord $cim_info $cim_info_orgs 5
			getWord $cim_info $cim_info_equi 7
			getWord $info $info_fuel 3
			getWord $info $info_orgs 5
			getWord $info $info_equi 7
			stripText $info_fuel "%"
			stripText $info_orgs "%"
			stripText $info_equi "%"
			stripText $cim_info_fuel "%"
			stripText $cim_info_orgs "%"
			stripText $cim_info_equi "%"
			if ($cim_info_fuel > $info_fuel) or ($cim_info_orgs > $info_orgs) or ($cim_info_equi > $info_equi)
				add $used_port_count 1
				if ($cim_info_fuel > $info_fuel)
					setVar $fueldifference $cim_info_fuel
					subtract $fueldifference $info_fuel
					add $spanky 1
				end
				if ($cim_info_orgs > $info_orgs)
					setVar $orgsdifference  $cim_info_orgs
					subtract $orgsdifference $info_orgs
					add $spanky 2
				end
				if ($cim_info_equi > $info_equi)
					setVar $equidifference $cim_info_equi
					subtract $equidifference $info_equi
					add $spanky 4
				end
				if ($spanky = 1)
					setVar $change_port[$used_port_count] "Sector " & $info_sector & " down " & $fueldifference & "% in fuel."
				elseif ($spanky = 2)
					setVar $change_port[$used_port_count] "Sector " & $info_sector & " down " & $orgsdifference & "% in orgs."
				elseif ($spanky = 4)
					setVar $change_port[$used_port_count] "Sector " & $info_sector & " down " & $equidifference & "% in equipment."
				elseif ($spanky = 3)
					setVar $change_port[$used_port_count] "Sector " & $info_sector & " down " & $fueldifference & "% in fuel and " & $orgsdifference & "% in orgs."
				elseif ($spanky = 5)
					setVar $change_port[$used_port_count] "Sector " & $info_sector & " down " & $fueldifference & "% in fuel and " & $equidifference & "% in equipment."
				elseif ($spanky = 6)
					setVar $change_port[$used_port_count] "Sector " & $info_sector & " down " & $orgsdifference & "% in orgs and " & $equidifference & "% in equipment."
				else
					setVar $change_port[$used_port_count] "Sector " & $info_sector & " down " & $fueldifference & "% in fuel, "  & $orgsdifference & "% in orgs, " & $equidifference & "% in equipment."
				end
			end
			setVar $last_added $info_sector
			add $cim_file_place 1
			goto :cim
		elseif ($cim_info_sector > $info_sector)
			setVar $last_added $info_sector
			add $cim_file_place 1
			goto :cim
		elseif ($cim_info_sector < $info_sector)
			add $used_port_count 1
			add $cim_file_place 1
			setVar $change_port[$used_port_count] "Sector " & $cim_info_sector & " is no longer responding."
			goto :read_file
		end
				
				
			
	
:done
 	killtrigger done
 	killtrigger cim
 	if ($new_file = 1)
		setVar $new_file 0
        else
		delete $cim_file
		rename $cim_temp $cim_file
	end
	send "q"
 	setVar $print_loop 0
	if ($used_port_count > 0)
		waitfor "(?="
		send "'*"
:print_results
		if ($print_loop	< $used_port_count)
			add $print_loop 1
			send $change_port[$print_loop] "*"
			write $log_file $change_port[$print_loop]
			goto :print_results
		else
			send "*"
		end
	end
	waitfor "(?="		
	goto :wait
	