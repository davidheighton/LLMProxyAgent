# TWX SupG Script  : SupGClass0.ts
# Author           : SupG
# Description      : Simple script that uses ZTM data to find likely
#                    sectors that may contain a class 0 port.
# Trigger Point    : Any
# Other            : Let me know of any problems with this script
#www.twtavern.com

echo ANSI_6 "**-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 "     SupGClass0     " ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-*"
echo ANSI_6 "-" ansi_5 "=" ansi_6 "-" ansi_5 "=" ansi_10 "(" ansi_15 "  www.twtavern.com  " ansi_10 ")" ANSI_5 "=" ansi_6 "-" ansi_5 "=" ansi_6 "-**"


delete GAMENAME & class0.txt
setVar $speedy 11

echo ansi_15 "*Finding Sectors : Status (| = 10%)*"
echo "1%        100%* "
setVar $heh SECTORS
divide $heh 10
setVar $statuscount 0

:readit
if ($speedy < SECTORS)
	add $statuscount 1
	if ($statuscount = $heh)
		echo ansi_12 "|"
		setVar $statuscount 0
	end
	setVar $secinf.backdoor[1] 0
    	getSector $speedy $secinf
	if ($secinf.warp[6] <> 0) and ($secinf.backdoor[1] <> 0)
			write GAMENAME & Class0.txt $speedy
	end
    	add $speedy 1
	goto :readit
end
:done
echo ansi_15 "*Done, sector list in file name: " GAMENAME "Class0.txt*"