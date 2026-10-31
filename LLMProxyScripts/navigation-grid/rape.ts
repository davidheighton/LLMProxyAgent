# Exiled TWX Script: 
# Author           : Pinky
# Description      : 
# Warnings         : 
# Other            :


# Rape Sector Script 1

# show script banner
echo "**" ANSI_3 "     --" ANSI_11 "===| " ANSI_15 "Rape Sector 0.01" ANSI_11 " |===" ANSI_3 "--"
echo "**" ANSI_15 "You May not Survive After this But it should rape the sector run close to midnight*"

# get the sector we're in
send "d"
setTextLineTrigger 2 :getSector "Sector  : "
pause
:getSector
getWord CURRENTLINE $origsector 3

getinput $photonship "Enter ID of Photon Ship (This Ship)" 0
getinput $rapeship   "Enter ID of Invading Ship (Interdictor)" 0
getinput $rapesector "Enter Sector to Rape" 0
setVar $rapebeacon "Sm00Ch! Th1S B1tCh -;)"
setVar $rapepname1 "sn4k3 v0m1t"
setVar $rapepname2 "sn4k3 sh1t"
setVar $rapepname3 "sn4k3 p00p"
setVar $rapepname4 "sn4k3 cr4ck"
setVar $rapepname5 "sn4k3 sm4ck"
setVar $rapepname6 "sn4k3 f4rt"
setVar $rapepname7 "sn4k3 v0m1t"
setVar $rapepname8 "sn4k3 sh1t"
setVar $rapepname9 "sn4k3 p00p"
setVar $rapepname10 "sn4k3 cr4ck"
setVar $rapepname11 "sn4k3 sm4ck"
setVar $rapepname12 "sn4k3 f4rt"
setVar $rapepname13 "sn4k3 v0m1t"
setVar $rapepname14 "sn4k3 sh1t"
setVar $rapepname15 "sn4k3 p00p"
setVar $rapepname16 "sn4k3 cr4ck"
setVar $rapepname17 "sn4k3 sm4ck"
setVar $rapepname18 "sn4k3 f4rt"
setVar $rapepname19 "sn4k3 sm4ck"
setVar $rapepname20 "sn4k3 f4rt"

if ($photonship = 0)
   halt
end

if ($rapeship = 0)
   halt
end

if ($rapesector = 0)
   halt
end

send "cpy" $rapesector "*q"
send "x*" $rapeship "*q"
send "m" $rapesector "**"
send "uyn" $rapepname1 "*c"
send "uyn" $rapepname2 "*c"
send "uyn" $rapepname3 "*c"
send "uyn" $rapepname4 "*c"
send "uyn" $rapepname5 "*c"
send "uyn" $rapepname6 "*c"
send "uyn" $rapepname7 "*c"
send "uyn" $rapepname8 "*c"
send "uyn" $rapepname9 "*c"
send "uyn" $rapepname10 "*c"
send "uyn" $rapepname11 "*c"
send "uyn" $rapepname12 "*c"
send "uyn" $rapepname13 "*c"
send "uyn" $rapepname14 "*c"
send "uyn" $rapepname15 "*c"
send "uyn" $rapepname16 "*c"
send "uyn" $rapepname17 "*c"
send "uyn" $rapepname18 "*c"
send "uyn" $rapepname19 "*c"
send "uyn" $rapepname20 "*c"
send "h1200*c"
send "sh"