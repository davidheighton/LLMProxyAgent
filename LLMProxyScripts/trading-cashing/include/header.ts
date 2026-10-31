:Pack2Header
sys_check
sys_showmsg
  
getLength $Script $spaces
setVar $spaces (20 - ($spaces / 2)) - 1
  
echo "**" ANSI_15 "/-------TWX-PROXY-SCRIPT-PACK-2-------\*"
echo              "             Version 2.00**"
  
while ($spaces > 0)
echo " "
subtract $spaces 1
end
  
echo $Script "***"
echo ANSI_7 "For script information please refer to the*Pack2 Documentation.  Remember that you can*always use the help option (+) to get*information on any script feature."
echo "**" ANSI_15 "Read the warnings about this script before*using it!**"
return
