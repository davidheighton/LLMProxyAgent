# Script name         : Create Planets
# Last Update         : 4.9.2003
# Description         : Select multiple planets and stop if 
#                     : any of them are created. Use's random numbers
#                     : to prevent blowing up the wrong planet and
#                     : check's the first time it lands for colos.
# bugs                : None
# Future update's     : No planet scanner requirement (Maybe).
# Author              : Onyx don_patch@yahoo.com <--if the problem is serious

CutText CURRENTLINE $locale 1 12
IF ($locale <> "Command [TL=")
  Echo ANSI_11 "*** This script must be run from the sector " ANSI_14 ">> " ANSI_12 "Command [TL=" ANSI_14 " <<" ANSI_11 " prompt, SCRIPT HALTING!***"
  GoTo :end_script
END
SEND "I"
SetTextTrigger 0 :scanner "Planet Scanner : "
SetTextTrigger 1 :no_scan "Credits        :"
pause

:no_scan
Echo ANSI_11 "*** You need a " ANSI_14 ">> " ANSI_12 "PLANET SCANNER" ANSI_14 " <<" ANSI_11 " to run this script, HALTING SCRIPT!***"
GoTo :end_script

:scanner
KillTrigger 0
KillTrigger 1
GoSub :get_planets
SetVar $i 1
:update_list
SEND "CJ*QQ"
WaitFor "in (?=List) ?"
SetTextLineTrigger 3 :list_loop "Class "
pause

  :list_loop
  KillTrigger 2
  SetVar $list[$i] CURRENTLINE
  ADD $i 1
  SetTextLineTrigger 2 :list "You shut off the Vid Term."
  SetTextLineTrigger 3 :list_loop "Class "
  pause
  
:list
# setup the stop triggers for planets WHEW!
KillTrigger 2
KillTrigger 3
SUBTRACT $i 1
SetVar $planetCount $i
SetVar $i 1
:line_loop
GetText $list[$i] $textA "<" ","
MergeText $textA "," $textB
MergeText "<" $textB $textC
StripText $list[$i] $textC
GetText $textC $textD "> " ","
MergeText $textD "," $alphaClass
GetWord $list[$i] $textE 1
GetWordPos $list[$i] $textEPos $textE
SUBTRACT $textEPos 1
CutText $list[$i] $trash 1 $textEPos
StripText $list[$i] $trash
SetVar $nameClass $list[$i]
MergeText $alphaClass " " $class 
MergeText $class $nameClass $planetType[$i]
IF ($i = $planetCount)
  Echo #12
  GoTo :display_1
END
ADD $i 1
GoTo :line_loop

:display_1
SetVar $i 1
Echo ANSI_8  "*  ====================================="
Echo ANSI_14 "*               PLANET LIST             "
Echo ANSI_8  "*  ====================================="

:display_loop_1
Echo         "*    " ANSI_8 "<" ANSI_14 $i ANSI_8 "> " ANSI_2 $planetType[$i]
IF ($i = $planetCount)
  SetVar $i 1
  GoTo :display_2
END
ADD $i 1
GoTo :display_loop_1

:display_2
Echo ANSI_8  "*  ====================================="
Echo ANSI_14 "*     CREATE WHAT TYPES FROM 1 TO " $planetCount
Echo ANSI_14 "*       C to continue Q to Quit         "
Echo ANSI_8  "*  ====================================="

:display_loop_2
IF ($planetType[$i] = $createType[$i])
  Echo       "*    " ANSI_8 "<" ANSI_10 $i ANSI_8 "> " ANSI_3 $createType[$i]
END
IF ($i = $planetCount)
  SetVar $i 0
  GoTo :input_1
END
ADD $i 1
GoTo :display_loop_2

:input_1
Echo "* "
GetConsoleInput $i 
:reset_loop
IF ($i = "q") OR ($i = "Q")
  GoTo :end_script
ELSEIF ($i = "c") OR ($i = "C")
  Echo #12
  GoTo :input_2
END
IF ($createType[$i] = $planetType[$i])
  SetVar $createType[$i] 0
  GoTo :display_1
END
SetVar $createType[$i] $planetType[$i]
GoTo :display_1

:input_2
IF ($noPlanets <> 0)
  SetVar $i 0
  GoTo :start
END
Echo ANSI_8  "*  ====================================="
Echo ANSI_14 "*       WOULD YOU LIKE TO START BY      "
Echo ANSI_14 "*      BLOWING UP A PLANET? [Y/N/Q]        "
Echo ANSI_8  "*  =====================================*"
GetConsoleInput $i
IF ($i = "y") OR ($i = "Y")
  SetVar $i 1
  GoTo :display_3
ELSEIF ($i = "n") OR ($i = "N")
  SetVar $i 0
  SetVar $blowMe 0
  GoTo :start
ELSEIF ($i = "q") OR ($i = "Q")
  GoTo :end_script
ELSE
  GoTo :input_2
END

:display_3
Echo ANSI_8  "*  ====================================="
Echo ANSI_14 "*          DESTROY WHICH ONE            "
Echo ANSI_8  "*  ====================================="
:display_loop_3
Echo        "*    " ANSI_8 "<" ANSI_14 $planet[$i] ANSI_8 "> " ANSI_6  " Planet Name: " ANSI_3 $name[$i]
IF ($i = $count)
  GoTo :input_3
END
ADD $i 1
GoTo :display_loop_3

:input_3
Echo "*"
GetConsoleInput $i
SetVar $blowMe $i

:start
IF ($i = "q") OR ($i = "Q")
  GoTo :end_script
END
KillAllTriggers
IF ($blowMe <> 0)
  SEND "L" 
  SetTextTrigger 4 :end_script "That planet is not in this sector."
  SetTextTrigger 5 :end_script "There isn't a planet in this sector."
  SEND $blowMe "*"
  WaitFor "2 Build 1"
  SetTextLineTrigger 6 :colo1 "Fuel Ore "
  pause
  
  :colo1
  KillTrigger 6
  GetWord CURRENTLINE $colo 3
  StripText $colo ","
  IF ($colo <> 0)
    GoTo :end_script
  END
  SetTextLineTrigger 7 :colo2 "Organics "
  pause
  
  :colo2
  KillTrigger 7
  GetWord CURRENTLINE $colo 2
  StripText $colo ","
  IF ($colo <> 0)
    GoTo :end_script
  END
  SetTextLineTrigger 8 :colo3 "Equipment "
  pause
  
  :colo3
  KillTrigger 8
  GetWord CURRENTLINE $colo 2
  StripText $colo ","
  IF ($colo <> 0)
    GoTo :end_script
  END
  SEND "ZDY"
  SetTextTrigger 9 :end_script "You do not"
  WaitFor "Command [TL="
  KillTrigger 9
END



:make_it
SetVar $i 1
SEND "U"
SetTextTrigger 10 :end_script "You don't have any Genesis Torpedoes"
SetTextTrigger 11 :end_safe "Do you wish to abort?"
WaitFor "You have "
SEND "Y"
SetTextLineTrigger 12 :checkType "What do you want to name this planet?"
pause


:checkType
KillTrigger 10
KillTrigger 11
KillTrigger 12
GetText CURRENTLINE $created "(" ")"
IF ($createType[$i] <> $created)
  IF ($i = $planetCount)
    GoTo :generate_rnd
  END
ELSEIF ($createType[$i] = $created)
  Echo #7
  GoTo :end_script
END
ADD $i 1
GoTo :checkType

:generate_rnd
GetRnd $randNum 100000000 1900000000
SEND $randNum "*"


KillTrigger 13
KillTrigger 14
SetTextTrigger 13 :set_personal "Should this be a (C)orporate"
SetTextTrigger 14 :personal "Command [TL="
pause

:set_personal
KillTrigger 13
KillTrigger 14
SEND "P"

:personal
KillTrigger 13
KillTrigger 14
SetVar $findBlowMe 1
GoSub :get_planets
SetVar $i 0
:find_it
ADD $i 1
IF ($name[$i] <> $randNum)
  GoTo :find_it
ELSEIF ($randNum = 0) OR ($i > 20)
  Echo ANSI_12 "*** THE SCRIPT COULD NOT FIND THE RIGHT PLANET TO DESTROY, don_patch@yahoo.com ***"
  GoTo :end_script
END

SEND $planet[$i] "*"
WaitFor "Planet command "
SEND "ZDY"
SetTextTrigger 15 :end_script "You do not"
WaitFor "Command [TL="
KillTrigger 15
GoTo :make_it


:end_safe
SEND "Y"
:end_script
KillAllTriggers
OUTPUTVARS
pause
HALT
  




# ----------------- SUBROUTINES -----------------------



:get_planets
KillAllTriggers
SetVar $i 0
SEND "L"
WaitFor "<Atmospheric maneuvering system engaged>"
SetTextLineTrigger 1 :no_planets "There isn't a planet in this sector."
SetTextLineTrigger 2 :planet_loop "<"
pause

  :no_planets
  KillTrigger 1
  KillTrigger 2
  SetVar $noPlanets 1
  GoTo :end_planets
  
  :planet_loop
  KillTrigger 1
  KillTrigger 2
  SetVar $noPlanets 0
  ADD $i 1
  SetVar $line[$i] CURRENTLINE
  GetWord $line[$i] $checkit 1
  IF ($checkit = "<")
    GetWord $line[$i] $planet[$i] 2
    GetWord $line[$i] $name[$i] 3
  ELSE
    GetWord $line[$i] $planet[$i] 3
    GetWord $line[$i] $name[$i] 4
  END
  StripText $planet[$i] ">"
  StripText $planet[$i] "<"
  StripText $planet[$i] " "
  SetTextLineTrigger 2 :planet_loop "<"
  SetTextTrigger 1 :got_planets "Land on which planet"
  pause
  
  :got_planets
  SetVar $count $i
  KillTrigger 1
  KillTrigger 2
  IF ($findBlowMe = 1)
    GoTo :end_planets
  ELSE
    SEND "Q*"
  END
  
  :end_planets   
    
Return
