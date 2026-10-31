# figDelay.ts
# by Onyx
# 11.12.2002 Ver. .01b (beta)
# Thanks to Xide for his 1_Trade.ts script, to SupG
# for the great scripts at the scripters tavern, and other
# players at the TWXproxy help site for the questions and answers.
# This script does not check for one ways and will error if anything
# else is in the adjacent sectors like mines or enemy figs
# BEWARE!

cutText CURRENTLINE $locale 1 12

if ( $locale <> "Command [TL=" )
    echo ANSI_10 "This script must be run form the Command prompt, halting script!"
    goto :end_script
end

:input1
setVar $figType 0
setVar $figAmt 0
echo ANSI_15 "** ========================"
echo ANSI_11 "**    WHAT FIGHTER TYPE? "
echo ANSI_11 "*       [Q to QUIT]*"
echo ANSI_15 "* ========================*"
echo ANSI_7  "*    TOLL" ANSI_8 " -------" ANSI_2 " 1"
echo ANSI_7  "*    DEFENSIVE" ANSI_8 " --" ANSI_2 " 2"
echo ANSI_7  "*    OFFENSIVE" ANSI_8 " --" ANSI_2 " 3*"
echo ANSI_15 "* ========================*"
getConsoleInput $figChoice SINGLEKEY
if ( $figChoice <> 1 ) and ( $figChoice <> 2 ) and ( $figChoice <> 3 )
    goto :input1
elseif ( $figChoice = "q" ) or ( $figChoice = "Q" )
    goto :end_script
else
end
if ( $figChoice = 1 )
    setVar $figType "t"
elseif ( $figChoice = 2 )
    setVar $figType "d"
elseif ( $figChoice = 3 )
    setVar $figType "o"
else
end

:input2
echo ANSI_15 "** ========================"
echo ANSI_11  "**   NUMBER OF FIGHTERS   "
echo ANSI_11  "*      PER SECTOR?        "
echo ANSI_15 "** ========================*"
getConsoleInput $figAmt
isNumber $test1 $figAmt 

if ( $test1 <> "TRUE" )
    goto :input2
end

send "*"

setTextLineTrigger baseSector :base_sector "Sector  :"
pause

# first get our current sector
:base_sector
getWord CURRENTLINE $baseSect 3


# density scan sectors

:scan_sectors
send "sd"

setTextLineTrigger sectInfo :get_scan_info "Sector "
pause


:get_scan_info
setVar $i 0

    :scan_loop
    killAllTriggers
    
    add $i 1
    setVar $line CURRENTLINE
    getWord $line $warp[$i] 2
    getWord $line $density[$i] 4
    getWord $line $anom[$i] 13
    
    setTextTrigger scanDone :scan_density_fini "Command [TL="
    setTextLineTrigger sectInfo2 :scan_loop "Sector "
    pause

    :scan_density_fini
    killTrigger sectInfo2
    killTrigger sectInfo
    killTrigger scanDone
    setVar $b $i
    setVar $i 0

    :deploy_loop
   
    killAllTriggers
    add $i 1
    send $warp[$i] "*"
    send "f"
    
    setTextTrigger go :go "How many fighters do you want defending this sector?"
    pause

    :go
    killTrigger go
    send $figAmt "*"

    send "c"

    send $figType

    send $baseSect "*"
    if ( $i <> $b )
        goto :deploy_loop
    end

    
    :end_script
