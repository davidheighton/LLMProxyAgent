:WorldTrade
sys_check
setVar $Move~CheckSub :WorldTrade~Sub_MoveCheck
setVar $Credits 0
setVar $Checked 0
  
setVar $GamePrefs~Bank "WorldTrade"
setVar $GamePrefs~Animation[$GamePrefs~Bank] OFF
setVar $GamePrefs~PageMessages[$GamePrefs~Bank] OFF
setVar $GamePrefs~AbortDisplayAll[$GamePrefs~Bank] OFF
setVar $GamePrefs~ScreenPauses[$GamePrefs~Bank] OFF
gosub :GamePrefs~SetGamePrefs
  
:start
  
if ($Credits >= $Quota) and ($Quota > 0)
return
end
  
send "d"
setVar $Move~ScanHolo 0
setVar $Move~ExtraSend ""
gosub :Move~Move
  
goto :start
  
:sub_MoveCheck
if ($Checked)
setVar $Checked 0
elseif (SECTOR.FIGS.OWNER[$Move~CurSector] = "yours") or (SECTOR.FIGS.OWNER[$Move~CurSector] = "belong to your Corp") or (SECTOR.FIGS.QUANTITY[$Move~CurSector] = 0)
setVar $PortCheck~Sector $Move~CurSector
setVar $PortCheck~Scanned 0
setVar $PortCheck~PortType 1
gosub :PortCheck~PortCheck
setVar $Move~NoScan $PortCheck~Scanned
    
if ($PortCheck~Pair > 0)
setVar $PPT~SectorA $Move~CurSector
setVar $PPT~SectorB $PortCheck~Pair
setVar $PPT~ProdA $PortCheck~TradeProdA
setVar $PPT~ProdB $PortCheck~TradeProdB
gosub :PPT~PPT
      
if ($PPT~Aborted = "0")
if ($Haggle~HaggleFactor = 0)
gosub :PlayerInfo~InfoQuick
setVar $Credits $PlayerInfo~Credits
else
setVar $Credits $Haggle~Credits
end
      
setVar $Move~Found 1
end
      
setVar $Checked 1
end
end
  
return    
include "..\includes\aliases\pink-scripts-scripts3-include\PPT.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\PortCheck.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\move.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\haggle.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\playerInfo.ts"
include "..\includes\aliases\pink-scripts-scripts3-include\gamePrefs.ts"
