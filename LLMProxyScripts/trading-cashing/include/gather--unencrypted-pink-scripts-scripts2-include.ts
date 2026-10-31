:Gather
sys_check
  
setVar $gathered 0
setVar $Failed 0
if ($Holds = 0)
gosub :PlayerInfo~InfoQuick
setVar $Holds $PlayerInfo~Holds    
end
:goGather
  
if (($Quantity - $gathered) < $Holds)
setVar $get ($Quantity - $gathered)
else
setVar $get $Holds
end
if ($Seek)
setVar $Move~checkSub :Gather~CheckSector
send "d"
gosub :Move~Move
else
setVar $FindProduct~Quantity $get
setVar $FindProduct~Product $Product
setVar $FindProduct~IgnoreList $IgnoreList
setVar $FindProduct~StayOnPlanet 1
setVar $FindProduct~Sector $Sector
  
gosub :FindProduct~FindProduct
    
setVar $IgnoreList $FindProduct~IgnoreList
    
if ($FindProduct~Location = 0)
setVar $Failed 1
send "t"
return
end
    
setVar $sourceSector $Sector
setVar $found $FindProduct~Location
end
  
if ($Product = "C")
setVar $MoveProduct~SourceCategory $FindProduct~Category
setVar $MoveProduct~DestCategory $DestCategory
end
  
setVar $MoveProduct~Source $found
setVar $MoveProduct~SourceSector $sourceSector
setVar $MoveProduct~Dest $PlanetID
setVar $MoveProduct~DestSector $Sector
setVar $MoveProduct~Product $Product
setVar $MoveProduct~Quantity ($Quantity - $gathered)
setVar $MoveProduct~Safe 0
gosub :MoveProduct~MoveProduct
  
add $gathered $MoveProduct~Moved
  
if ($gathered < $Quantity)
send "q"
goto :goGather
end
  
if ($StayOnPlanet = 0)
send "q"
end
return
 
:Gather~CheckSector
setVar $FindProduct~Quantity $get
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
  
include "..\includes\aliases\pink-scripts-scripts2-include\move.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\findProduct.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\moveProduct.ts"
include "..\includes\aliases\pink-scripts-scripts2-include\playerInfo.ts"
