# Script Title		: Quasar Cannon Blast Calculator
# Author			: RammaR
# Revision Date		: 10/10/02
# Description		: Input the blast data from 2 consecutive mothing's and it back calculates
#                     both the quasar cannon setting and the ore on the planet. Use the Menu
#                     to select between "Sector", "MBBS - Atmosphere", or "TWGS - Atmosphere"
#                     type blasts. Very useful for planning invasions.
# Notes				: Calculations are based on -
#                           Sector: cannon percent * ore / 3 
#                        MBBS Atmo: cannon percent * ore * 2
#                        TWGS Atmo: cannon percent * ore / 2

  
:Main_Menu
	echo "* * *"
	echo ANSI_9 "===== " ANSI_14 "Q-Cannon Calculator" ANSI_9 " =====**"
	echo ANSI_14 "   1" ANSI_10 "      Sector (MMBS / TWGS) *" 
   	echo ANSI_14 "   2" ANSI_10 "      Atmosphere - MBBS *" 
    echo ANSI_14 "   3" ANSI_10 "      Atmosphere - TWGS **"
	echo ANSI_14 "   4" ANSI_10 "      Exit **" 
    echo ANSI_9 "=============================== *"
	echo ANSI_14 "Selection: "
	getConsoleInput $menu_selection YES
	isNumber $menu_test $menu_selection
    if ($menu_test <> "TRUE")
        echo ANSI_12 "** BAD INPUT - MUST BE A NUMBER!**"
        goto :main_menu
    end
	if ($menu_selection > 0) AND ($menu_selection <= 4)
		echo "*"
		if ($menu_selection = 1)
            echo "*"
			setVar $type "Sector"
			getInput $blast_1 ANSI_14&"How much "&ANSI_12&"TOTAL"&ANSI_14&" damage was done by "&ANSI_12&"Blast #1? "
			getInput $blast_2 ANSI_14&"How much "&ANSI_12&"TOTAL"&ANSI_14&" damage was done by "&ANSI_12&"Blast #2? "
			goto :Sector_Calc
        end
		if ($menu_selection = 2)
            echo "*"
			setVar $type "Atmosphere (MBBS Mode)"
			getInput $blast_1 ANSI_14&"How much "&ANSI_12&"TOTAL"&ANSI_14&" damage was done by "&ANSI_12&"Blast #1? "
			getInput $blast_2 ANSI_14&"How much "&ANSI_12&"TOTAL"&ANSI_14&" damage was done by "&ANSI_12&"Blast #2? " 
			goto :MBBS_Calc
		end
        if ($menu_selection = 3)
            echo "*"
			setVar $type "Atmosphere (TWGS Mode)"
			getInput $blast_1 ANSI_14&"How much "&ANSI_12&"TOTAL"&ANSI_14&" damage was done by "&ANSI_12&"Blast #1? "
			getInput $blast_2 ANSI_14&"How much "&ANSI_12&"TOTAL"&ANSI_14&" damage was done by "&ANSI_12&"Blast #2? "
			goto :TWGS_Calc
		end
		if ($menu_selection = 4)
            echo ANSI_9 "*Script Ended Properly*"
            halt
        end
   else
		echo "*"
		echo ANSI_12 "BAD INPUT - TRY AGAIN!"
		goto :main_menu
	end


:Sector_Calc
	setVar $ore_blast_1 $blast_1
	multiply $ore_blast_1 3
	setVar $ore_blast_2 $blast_2
	multiply $ore_blast_2 3
	setVar $ore_difference $ore_blast_1
    subtract $ore_difference $ore_blast_2
	setVar $decimal_conversion $ore_difference
	multiply $decimal_conversion 101
	setVar $cannon_% $decimal_conversion
	divide $cannon_% $ore_blast_1
    setVar $original_ore $ore_blast_1
	multiply $original_ore 100
	divide $original_ore $cannon_%
	setVar $after_1st_blast $original_ore
	subtract $after_1st_blast $ore_blast_1
	setVar $after_2nd_blast $after_1st_blast
	subtract $after_2nd_blast $ore_blast_2
	setVar $next_blast $after_2nd_blast
	multiply $next_blast $cannon_%
	divide $next_blast 300
	goto :Display

:MBBS_Calc
	setVar $ore_blast_1 $blast_1
	divide $ore_blast_1 2
	setVar $ore_blast_2 $blast_2
	divide $ore_blast_2 2
	setVar $ore_difference $ore_blast_1
	subtract $ore_difference $ore_blast_2
	setVar $decimal_conversion $ore_difference
	multiply $decimal_conversion 101
	setVar $cannon_% $decimal_conversion
	divide $cannon_% $ore_blast_1
    setVar $original_ore $ore_blast_1
	multiply $original_ore 100
	divide $original_ore $cannon_%
	setVar $after_1st_blast $original_ore
	subtract $after_1st_blast $ore_blast_1
	setVar $after_2nd_blast $after_1st_blast
	subtract $after_2nd_blast $ore_blast_2
	setVar $next_blast $after_2nd_blast
	multiply $next_blast $cannon_%
	divide $next_blast 50
	goto :Display


:TWGS_Calc
	setVar $ore_blast_1 $blast_1
	multiply $ore_blast_1 2
	setVar $ore_blast_2 $blast_2
	multiply $ore_blast_2 2
	setVar $ore_difference $ore_blast_1
	subtract $ore_difference $ore_blast_2
	setVar $decimal_conversion $ore_difference
	multiply $decimal_conversion 101
	setVar $cannon_% $decimal_conversion
	divide $cannon_% $ore_blast_1
    setVar $original_ore $ore_blast_1
	multiply $original_ore 100
	divide $original_ore $cannon_%
	setVar $after_1st_blast $original_ore
	subtract $after_1st_blast $ore_blast_1
	setVar $after_2nd_blast $after_1st_blast
	subtract $after_2nd_blast $ore_blast_2
	setVar $next_blast $after_2nd_blast
	multiply $next_blast $cannon_%
	divide $next_blast 200
	goto :Display

:Display
	echo ANSI_9&"** ==== " ANSI_14&"Cannon Calculation Results" ANSI_9&" ==== **"
	echo ANSI_10&" Cannon Setting: " ANSI_12&$cannon_% ANSI_10&" % - "& ANSI_12&$type "**"
	echo ANSI_10&" Initial Ore    : " ANSI_14&$original_ore ANSI_10&" Units Ore*"
	echo " After 1st Blast: " ANSI_14&$after_1st_blast ANSI_10&" Units Ore*"
	echo " After 2nd Blast: " ANSI_14&$after_2nd_blast ANSI_10&" Units Ore**"
	echo ANSI_11&" Next Blast Projection: " ANSI_12&$next_blast ANSI_11&" Damage Points**"





