:SeekProduct
sys_check
  
if ($Holds = 0)
gosub :PlayerInfo~InfoQuick
setVar $Holds $PlayerInfo~Holds    
end
:goGather
setVar $Move~checkSub :SeekProduct~CheckSector
send "d"
gosub :Move~Move
  
if ($found = "P")
:buyProduct
  
    
if ($Product = 1)
setVar $Haggle~BuyProd "Fuel"
elseif ($Product = 2)
setVar $Haggle~BuyProd "Organics"
else
setVar $Haggle~BuyProd "Equipment"
end
    
setVar $Haggle~Quantity 0
setVar $Haggle~Sector $sourceSector
send "pt"
gosub :Haggle~Haggle
    
if ($Haggle~Abort)
goto :buyProduct
end
else
send "tnt" & $Product "*q"
end
  
return
 
:checkSector
setVar $FindProduct~Quantity $Holds
setVar $FindProduct~Product $Product
setVar $FindProduct~IgnoreList $IgnoreList
setVar $FindProduct~StayOnPlanet 1
setVar $FindProduct~Sector $Move~CurSector
gosub :FindProduct~FindProduct
  
setVar $IgnoreList $FindProduct~IgnoreList
  
if ($FindProduct~Location <> 0)
setVar $Move~Found 1
setVar $sourceSector $Move~CurSector
setVar $found $FindProduct~Location
end
  
return
  
include "include\move"
include "include\findProduct"
include "include\playerInfo"
include "include\haggle"
