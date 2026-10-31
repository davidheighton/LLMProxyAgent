# Gridding script by Gravedigr and Xide
# Macro to save ship: b4577*yx60**l86*c

getInput $sector "enter sector to clear"
getsector $sector $sectorinfo
setvar $dens $sectorinfo.DENSITY
send "qq"
waitfor "Command [TL="
send "sd"
waitfor "Command [TL="
getsector $sector $sectorinfo1
setvar $dens1 $sectorinfo1.DENSITY
if ($dens1 > $dens)
send "l6*cs'density change in" $sector "*"
else
send $sector "* at9999** f1*ctsdx 1**  l6*cp" $sector "*yqq x6**h1t4*cq*l6*c"

