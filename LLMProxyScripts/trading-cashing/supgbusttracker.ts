systemScript
setVar $bustlist GAMENAME & ".bst"
setVar $laststeal GAMENAME & ".lst"
gosub :get_last_Bust
fileExists $check $laststeal 
if ($check = "TRUE")
  read $laststeal $success_sector 1
end

echo ansi_12 "*Would you like to clear busts?*" 
getconsoleInput $clear singlekey
if ($clear = y)
 fileExists $check $bustlist 
  if ($check = "TRUE")
    delete $bustlist
  end
  echo ansi_12 "*Busts Cleared*"
end 
send "d"
window bust 200 100 "Bust Status" no

:top
setWindowContents bust "Current sector : " & $current_sector & "*Busted : " & BUSTSECTOR & "*Last Bust : " & $busted_sector & "*Last Steal : " & $success_sector
setVar $bust_count 1
settextLinetrigger bust :trackbust "Suddenly you're Busted!"
setTextLineTrigger warping :in_sector "Sector  :"
setTextLineTrigger success :last_steal "Success!"
pause

:trackbust
killtrigger warping
killtrigger success
waitfor "Command [TL="
getword CURRENTLINE $sector_mess 2
cutText $sector_mess $busted_sector 15 6
stripText $busted_sector "["
stripText $busted_sector "]"
setVar $current_sector $busted_sector
write $bustlist $busted_sector
goto :top

:in_sector
killtrigger bust
killtrigger success
getWord CURRENTLINE $current_sector 3

:chk_bust
fileExists $check $bustlist 
if ($check = "FALSE")
  setProgVar BUSTSECTOR "FALSE"
  goto :top
end
:bust_me
read $bustlist $bust $bust_count 
if ($bust <> "EOF")
   if ($bust = $current_sector)
     setProgVar BUSTSECTOR "TRUE"
     goto :top
    else
     add $bust_count 1
     goto :bust_me
    end
   
else
  setProgVar BUSTSECTOR "FALSE"
  goto :top
end

:last_steal
killtrigger warping
killtrigger bust
waitfor "Command [TL="
getword CURRENTLINE $sector_mess 2
cutText $sector_mess $success_sector 15 6
stripText $success_sector "["
stripText $success_sector "]"
setVar $current_sector $success_sector
fileExists $check $laststeal 
if ($check = "TRUE")
  delete $laststeal
end
write $laststeal $success_sector
goto :top

:get_last_Bust
setVar $busted_loop 1
fileExists $check $bustlist 
  if ($check = "TRUE")
:last_bust_loop    
	read $bustlist $stuff $busted_loop
	if ($stuff <> "EOF")
	    add $busted_loop 1
	    setVar $last_bust_chk $stuff
 	    goto :last_bust_loop
        else
	    setVar $busted_sector $last_bust_chk
	    return
	end
  end	    
  return