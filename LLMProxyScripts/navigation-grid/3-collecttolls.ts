# CollectTolls v1.1
# by synack
# retrieves all of the tolls that your deployed figs have collected
# notes:
#	- works best when combined with my blasttolls.ts script if there are other ppl's figs sitting around
#	- doesn't move "safely" through the map (density scan, move, etc.) - just carry enough figs to make it not matter

:start
	setVar $count 1
	setVar $i 1
	setVar $sectors 0
	send "g"
	waitFor "Deployed  Fighter  Scan"

:watch
	killAllTriggers
	setTextLineTrigger toll_fig :toll_fig "Toll "
	setTextTrigger goto_sector :goto_sector "Command [TL="
	pause

:toll_fig
	getWord CURRENTLINE $amount 5
	getWord CURRENTLINE $sector 1
	stripText $amount ","
	if ($amount > 0)
		setVar $sectors[$count] $sector
		add $count 1
	end
	goto :watch

:goto_sector
	killAllTriggers
	if (($count = 1) and ($sector[1] = 0))
		goto :end
	end
	if ($i < $count)
		setTextTrigger no_twarp :no_twarp "Do you want to engage the TransWarp drive?"
		setTextTrigger express :express "Engage the Autopilot? "
		setTextTrigger already_there :were_there "You are already in that sector!"
		MergeText "Sector  : " $sectors[$i] $onejump
		setTextLineTrigger were_there :were_there $onejump
		send "m" $sectors[$i] "*"
		pause
	else
		goto :start
	end

:no_twarp
	send "n"

:express
	send "e"

:maintain_express
	killAllTriggers
	setTextTrigger keep_going :keepgoing "Stop in this sector "
	setTextTrigger were_there :were_there "Command [TL="
	pause

:keepgoing
	send "n"
	goto :maintain_express

:were_there
	killAllTriggers
	setTextLineTrigger count_figs :count_figs "Fighters: "
	send "d"
	pause

:count_figs
	killAllTriggers
	getWord CURRENTLINE $figs 2
	getWord CURRENTLINE $corp_personal 3
	stripText $figs ","
	stripText $corp_personal "("
	stripText $corp_personal ")"

:collect
	setTextTrigger fig_type :fig_type "Should they be "
	setTextTrigger corp_personal :corp_personal "Should these be "
	send "f" $figs "*"
	pause

:corp_personal
	if ($corp_personal = "yours")
		send "p"
	else
		send "c"
	end

:fig_type
	killAllTriggers
	send "t"
	add $i 1
	waitFor "Done. You have "
	goto :goto_sector

:end
	echo ANSI_15 "All done!"
