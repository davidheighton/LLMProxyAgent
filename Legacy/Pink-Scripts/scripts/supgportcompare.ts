setVar $counter 1    
setVar $newctr 1    
setVar $comparing 0  


ECHO ANSI_15 "*        -=" ANSI_11 " SupG's Port Report Comparison Script " ANSI_15 "=-"
ECHO ANSI_15 "*                    -=" ANSI_11 " Version 3.0 " ANSI_15 "=-"
#=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-

:Add_or_Run
setVar $selection ""
setVar $game_loop 1
setVar $name_loop 0
ECHO ANSI_15 "*-=" ANSI_11 " Main Menu " ANSI_15 "=-" 
echo "*" ANSI_15 "What to do?" "**"
echo ANSI_11 "1 " ANSI_8 " -" ANSI_10 " Run Port Compare*"
echo ANSI_11 "2 " ANSI_8 " -" ANSI_10 " Add A Game and Make New Permanent File*"
echo ANSI_11 "3 " ANSI_8 " -" ANSI_10 " Add A Game with an Existing Permanent File*"
echo ANSI_11 "4 " ANSI_8 " -" ANSI_10 " Remove A Game*"
echo ANSI_11 "5 " ANSI_8 " -" ANSI_10 " Exit*"
echo ANSI_11 "? " ANSI_8 " -" ANSI_14 " Help*"
getConsoleInput $Selection SINGLEKEY

if ($Selection = 1)
  goto :gamelist
elseif ($Selection = 2)
  goto :add_game_new
elseif ($Selection = 3)
  goto :add_game_existing
elseif ($Selection = 4)
  setVar $rem 1
  goto :gamelist
elseif ($Selection = 5)
  halt
elseif ($Selection = "?")
  goto :help_Main
else
  goto :Add_or_Run
end

:gamelist
 fileexists $prtfile_exist "prtcmpr.cfg" 
  if ($prtfile_exist = "FALSE")
    goto :no_cfg_file
  end
 read prtcmpr.cfg $gname $game_loop
  if ($gname = "EOF")
     if ($game_loop = 1)
:no_cfg_file       
       echo ansi_3 "**You don't have any games stored, you have to add one."
       goto :Add_or_Run
     else
       if ($rem = 1)
         setVar $rem 0 
         goto :remove_game
       else
          goto :choose_game
       end
     end
  else
     setVar $game_name[$name_loop] $gname
     add $name_loop 1
     add $game_loop 1
     goto :gamelist
  end

:Choose_Game
ECHO ANSI_15 "*-=" ANSI_11 " Choose Game " ANSI_15 "=-"
gosub :Game_menu
getConsoleInput $Game_Selection
if ($Game_Selection < $choose_loop)
  setVar  $prmport $game_name[$Game_Selection]
  echo "*" $prmport
  goto :get_new_prt
elseif ($Game_Selection = $choose_loop)
  goto :add_or_run
else
  goto :Choose_Game
end

:Remove_game
setVar $remove 1
ECHO ANSI_15 "*-=" ANSI_11 " Remove Game " ANSI_15 "=-"
gosub :Game_menu
getConsoleInput $Remove_Selection
if ($Remove_Selection < $choose_loop)
    setVar $search_game $game_name[$Remove_Selection]
    setVar $newcount 1
    setVar $file_looper 1
:Write_New_File
  read prtcmpr.cfg $Old_File $file_looper
  striptext $old_file " " 
  striptext $search_game " "  
  if ($Old_File <> $search_game) and ($Old_file <> "EOF")
     setVar $New_File[$newcount] $Old_File
     add $newcount 1
     add $file_looper 1
     goto :Write_New_File
  elseif ($Old_File = "EOF")
     setVar $remove_file $search_game
     delete $remove_file
     setVar $new_conf_loop 1
     echo "*"  $file_looper
     delete prtcmpr.cfg
     goto :Write_New_Conf
  else
     add $file_looper 1
     goto :Write_New_File   
  end
elseif ($Remove_Selection = $choose_loop)
  goto :Add_or_Run
else
  goto :Remove_Game
end

:Write_New_Conf
if ($new_conf_loop < $newcount)
   write prtcmpr.cfg $New_File[$new_conf_loop]
   add $new_conf_loop 1
   goto :Write_New_Conf
 else
   goto :Add_or_Run
 end

:add_game_existing
 ECHO ANSI_15 "*-=" ANSI_11 " Add Game " ANSI_15 "=-"
 getInput $added_filename "What is the permanent file's name?" 0
 fileexists $newprm_exist $added_filename
 if ($newprm_exist = "TRUE")
  write prtcmpr.cfg $added_filename
 else
  echo "*File does not exist, check spelling and try again. You must include the file*extention.*"
 end
 goto :Add_or_Run

:add_game_new
 ECHO ANSI_15 "*-=" ANSI_11 " Add Game " ANSI_15 "=-"
 getInput $new_game_name "What do you want to call this game file? (Do NOT include file extention)" 0
 mergeText $new_game_name ".prm" $game_name_prm
 write prtcmpr.cfg $game_name_prm
 setVar $newprm 1
 gosub :get_new_port_report
 goto :Add_or_Run

:get_new_prt
 ECHO ANSI_15 "*-=" ANSI_11 " Choose New Report " ANSI_15 "=-"
 echo "*" ANSI_15 "What to do?" "**"
 echo ANSI_11 "1 " ANSI_8 " -" ANSI_10 " Get A New Report*"
 echo ANSI_11 "2 " ANSI_8 " -" ANSI_10 " Run From A File*"
 echo ANSI_11 "3 " ANSI_8 " -" ANSI_10 " Back*"
 echo ANSI_11 "? " ANSI_8 " -" ANSI_14 " Help*"
 getConsoleInput $Newprt SINGLEKEY

if ($Newprt = 1)
  gosub :get_new_port_report
  goto :run_compare
elseif ($Newprt = 2)
:chk_file  
  getInput $newportlog "What is the name of the new port report to compare with the permanent file?"
  fileexists $file_test $newportlog
  if ($file_test = "TRUE")
   goto :run_compare
  else
   echo "*File not available, check spelling and try again.*"
   goto :chk_file
  end
elseif ($Newprt = 3)
  goto :Add_or_Run
elseif ($Newprt = "?")
  goto :help_newrpt
else
  goto :get_new_prt
end

:help_newrpt
ECHO ANSI_15 "*-=" ANSI_11 " Choose New Report Menu Help " ANSI_15 "=-*"
ECHO ANSI_14 "*- 1. Get A New Report - "
ECHO ANSI_15 "*This option downloads new port information for you and compares it to the *selected permanent file. (You must be online to use this)*" 
ECHO ANSI_14 "*- 2. Run From A File - "
ECHO ANSI_15 "*This option compares the selected permanent file to a file that you specify. *(can be run offline)*"
ECHO ANSI_14 "*- 3. Back - "
ECHO ANSI_15 "*Takes you back to the Main Menu*"
ECHO ANSI_14 "*Press any key to continue...*"
getConsoleInput $shitter SINGLEKEY
goto :get_new_prt

:help_Main
ECHO ANSI_15 "*-=" ANSI_11 " Main Menu Help " ANSI_15 "=-*"
ECHO ANSI_14 "*- 1. Run Port Compare - *"
ECHO ANSI_15 "Run Port Compare runs the script. You must have a permanent file added to the *scripts config file. To do this you must either choose option 2 or 3 to add *a game. Once added the script will hold this data until it is deleted or *overwritten.*" 
ECHO ANSI_14 "*- 2. Add A Game and Make New Permanent File - *"
ECHO ANSI_15 "This option creates a new permanent file for you and adds it to the scripts *config file. A permanent file holds the initial known port data to which all *future port data is compared to.*"
ECHO ANSI_14 "*- 3. Add A Game with an Existing Permanent File - *"
ECHO ANSI_15 "This option takes an existing permanent file (created elsewhere or maybe a *corpies data file) and adds it to the scripts config file.*"
ECHO ANSI_15 "Using someone elses data may cause misleading results in your final report, as *the script checks on sectors YOU have explored, so unless they have explored *exactly the same sectors as you, you may get misleading unresponsive ports.*"
ECHO ANSI_14 "*- 4. Remove A Game - *"
ECHO ANSI_15 "Removes game from script's config file AND deletes the permanent file for this *game. Only use this if you are sure you don't need that data anymore!*"
ECHO ANSI_14 "*Press any key to continue...*"
getConsoleInput $shitter SINGLEKEY
goto :add_or_run

 
#=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-==-=-=-=-=-=-=-=-=-==-=-=-=-=-=-=-=-=-=-=-=-=-=-=
:run_compare
setVar $prmport_temp "prm.tmp"
setVar $deadportlog "deadports.cim"
getLength $prmport $File_Length
subtract $File_length 4
cutText $prmport $prmfile_tmp 1 $File_length
mergeText $prmfile_tmp ".rpt" $reportlog
delete $reportlog
#$newportlog new data filename 
#$prmport permanent data filename
#now we have all the data, it's time to process it.

:process
setVar $smallfile 0
setVar $total_sector 0
setVar $j 1
setVar $i 1
setVar $lastentered 0   
ECHO ANSI_15 "* Reading New file and updating permanent file... *"
write $reportlog "Sectors added to permanent file :"
window Status 250 50 "Status" yes 

:makenewprmport
 read $newportlog $newportreport $j 
 if ($newportreport <> "EOF")
    setvar $text "Checking New Secs : " 
    getword $newportreport $newsec 1 
    mergetext $text $newsec $windowtext  
    setwindowcontents Status $windowtext 
    goto :checksec                       
  else
   goto :compare   
  end

:checksec
   read $prmport $portreport $i   
   if ($portreport = "EOF")
      setVar $smallfile 1
   else
     getword $portreport $sec 1
   end  
    if ($newsec = $sec)
        #if newsector is equal to the old sector, no update needs to be done to permfile
        #but since we deleted it, we have to write it in anyway.  :)
        write $prmport_temp $portreport
        add $total_sector 1
        setvar $lastentered $sec
        add $j 1
        goto :makenewprmport
     end    
     if ($sec > $newsec) or ($smallfile = 1)
        #if sector is greater than the new one, it mean we have a new sector to add to the
        #permfile, so we write it there and to the report as an added file.
        write $prmport_temp $newportreport
        add $total_sector 1
        write $reportlog $newsec
         setVar $lastentered $newsec
        add $j 1
        goto :makenewprmport
     end
     if ($sec < $newsec)
        #when the sector is lower than the new one we have to check if it's been added to 
        #the new perm file so we check to see if it's greater than the last sector entered
        #to the perm file, if it is we write it in, if not we keep looping till it is greater
       if ($sec > $lastentered)
          write $prmport_temp $portreport
          add $total_sector 1
          setvar $lastentered $sec
          add $i 1
          goto :checksec
        else   
          add $i 1
          goto :checksec
        end
      end     
   #else
   #    goto :checksec
   #end


:compare
       delete $prmport
       rename $prmport_temp $prmport
       delete $prmport_tmp
       setvar $numdeadport 0   
       setVar $comparing 1     
       setVar $i 1             
       setVar $j 1             
       ECHO ANSI_15 "* Checking For UnResponsive Ports... *"   
       write $reportlog "Sectors not responding:"
      
:compareprocess

 read $prmport $portreport $i   
   if ($portreport <> "EOF")
     getword $portreport $sec 1                
      setvar $text "Finding Non-Reporting Secs : "  
      mergetext $text $sec $windowtext      
      setwindowcontents Status $windowtext   
:j 
read $newportlog $newportreport $j 
 if ($newportreport <> "EOF")
       getword $newportreport $newsec 1        
        if ($newsec = $sec)                   
            add $i 1
            goto :compareprocess   
        elseif ($newsec > $sec)                 
            write $reportlog $sec
            write $deadportlog $sec
            add $i 1
            add $numdeadport 1          
            goto :compareprocess
        else                              
            add $j 1
            goto :j
        end
      end
   else
      write $reportlog "Total Ports Recorded : " 
      write $reportlog $total_sector                 
      ECHO ANSI_15 "* Done! Report Filed in " ANSI_7 $reportlog ANSI_15 " in your TWX directory.* " 
      goto :comparebubble                      
   end  
  
:comparebubble
  Echo ANSI_15 "*Do you want to compare the list of dead ports to a bubble list? [y/n]"
  getConsoleInput $yesno YES
    if ($yesno = "y") or ($yesno = "Y")
         setvar $bubblecount 1
:bubble_exist          
         getInput $bubblelist "What is the name of the bubble list?"
         fileexists $bubblelist_exist $bubblelist
          if ($bubblelist_exist = "FALSE")
             echo "*File does not exist, check spelling and try again. You must include the file*extention.*"
             goto :bubble_exist
          end
         echo ANSI_15 "* Reading Bubble List, Please Wait... *"
         goto :bubblecomp
     elseif ($yesno = "n") or ($yesno = "N")
           delete $deadportlog
         halt
     else
         goto :comparebubble
     end

:bubblecomp
   setvar $windowtext ""
   setVar $g 1
   ECHO ANSI_15 "* Checking For UnResponsive Ports In Bubble Sectors... *"
   write $reportlog "Ports not responding in a bubble/deadend:"

:bubbleprocess
    read $bubblelist $bubblesector $g
    getword $bubblesector $bubblesector 1
    if ($bubblesector = "EOF")
        delete $deadportlog
        ECHO ANSI_15 "Done! Report Filed in " ANSI_7 $reportlog ANSI_15 "."
        halt
     else
       setvar $text "Comparing with Bubbles : "
       mergetext $text $bubblesector $windowtext 
       setwindowcontents Status $windowtext          
       goto :h
     end

:h
    if ($h > $numdeadport)                      
        add $g 1
        goto :bubbleprocess
    else
        read $deadportlog $dport $h
        getword $dport $dp 1
        if ($bubblesector = $dp)
           getsector $dp $port
           if ($port.port.class = 0)
               setvar $porttype "Unknown"
           elseif ($port.port.class = 1)
               setvar $porttype "BBS"
           elseif ($port.port.class = 2)
               setvar $porttype "BSB"
           elseif ($port.port.class = 3)
               setvar $porttype "SBB"
           elseif ($port.port.class = 4)
               setvar $porttype "SSB"
           elseif ($port.port.class = 5)
               setvar $porttype "SBS"
           elseif ($port.port.class = 6)
               setvar $porttype "BSS"
           elseif ($port.port.class = 7)
               setvar $porttype "SSS"
           elseif ($port.port.class = 8)
               setvar $porttype "BBB"
           else
               setvar $porttype "Unknown"
           end
           mergeText $dp " " $port_info
           mergeText $port_info $porttype $port_info 
           write $reportlog $port_info
           add $g 1
    	   goto :bubbleprocess
        elseif ($bubblesector < $dp)
           add $g 1
           goto :bubbleprocess
        else
           add $h 1
           goto :h
  	end
     end

:Game_menu
setVar $choose_loop 0
echo "*" ANSI_15 "Which Game?" "**"
:Choose_loop
 if ($choose_loop < $name_loop)
  echo ANSI_11 $choose_loop " " ANSI_3 " - " $game_name[$choose_loop] "*"
  add $choose_loop 1
  goto :Choose_loop
 else
  if ($remove <> 1)
  echo ANSI_11 "*" $choose_loop " " ANSI_3 " - Cancel*"
 else
   echo ANSI_11 "*" $choose_loop " " ANSI_3 " - Cancel*"
 end
 setVar $remove 0 
 return
end  

:get_new_port_report
 setVar $stopper 0
 if ($newprm = 1)
  setVar $new_report_filename $game_name_prm
 else
   setVar $new_report_filename newreport.prt
 end
 delete $new_report_filename
 setVar $newportlog $new_report_filename
 send "^R"
:cim
 setTextLineTrigger cim :cimmer
 pause
:cimmer
 setvar $info CURRENTLINE
 setVar $spaceless $info
 striptext $spaceless " " 
 if ($spaceless <> ":") and ($info <> ":ENDINTERROG") and ($info <> "")
  killtrigger done
  setTextTrigger done :done ":"
  write $new_report_filename $info
 end
 goto :cim
:done
 
 killtrigger done
 killtrigger cim
 send "q"
 waitfor "(?="
 setVar $newprm 0
return
