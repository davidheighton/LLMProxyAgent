param(
  [string]$StatusFile = ".\logs\192.168.4.75.status.json",
  [int]$RefreshMs = 500,
  [int]$RecentSectorPreview = 6
)

if ([System.IO.Path]::IsPathRooted($StatusFile)) {
  $statusCandidate = $StatusFile
} else {
  $statusCandidate = Join-Path $PSScriptRoot $StatusFile
}

$resolved = Resolve-Path -LiteralPath $statusCandidate -ErrorAction SilentlyContinue
if (-not $resolved) {
  $path = $statusCandidate
  Write-Host "Status file not found yet: $path"
  Write-Host "Waiting for bot to create it... (Ctrl+C to stop)"
} else {
  $path = $resolved.Path
  Write-Host "Watching status file: $path"
  Write-Host "Press Ctrl+C to stop."
}

$scriptStartedAt = Get-Date
$lastTimestamp = ""
$lastUpdateSeenAt = $null
$lastUpdateFromFile = $null
$snapshot = $null
$updateCount = 0
$readErrorCount = 0
$sectorHopCount = 0
$lastSeenSector = $null
$actionCounts = @{}

function Format-Value {
  param([object]$Value)
  if ($null -eq $Value) {
    return "-"
  }
  $text = [string]$Value
  if ([string]::IsNullOrWhiteSpace($text)) {
    return "-"
  }
  return $text
}

function Parse-UpdateTime {
  param([object]$Value)
  if ($null -eq $Value) {
    return $null
  }

  try {
    return [datetimeoffset]::Parse([string]$Value)
  } catch {
    return $null
  }
}

function Join-Limited {
  param(
    [object[]]$Items,
    [int]$MaxItems = 6
  )

  $list = @($Items)
  if ($list.Count -eq 0) {
    return "-"
  }

  $take = [math]::Min($MaxItems, $list.Count)
  $slice = $list[($list.Count - $take)..($list.Count - 1)]
  return ($slice -join ',')
}

function Render-Dashboard {
  param(
    [object]$Status,
    [string]$FilePath,
    [datetime]$StartedAt,
    [Nullable[datetime]]$LastSeenAt,
    [datetimeoffset]$LastFromFile,
    [int]$Updates,
    [int]$Errors,
    [int]$HopCount,
    [hashtable]$Actions,
    [int]$SectorPreviewCount
  )

  Clear-Host
  $now = Get-Date
  $uptime = $now - $StartedAt
  $uptimeText = "{0:00}:{1:00}:{2:00}" -f [int]$uptime.TotalHours, $uptime.Minutes, $uptime.Seconds

  Write-Host "TradeWars Bot Dashboard (Compact)" -ForegroundColor Cyan
  Write-Host "================================" -ForegroundColor Cyan

  if ($null -eq $Status) {
    Write-Host "No status snapshot yet." -ForegroundColor Yellow
    Write-Host "Waiting for bot updates..."
    return
  }

  $ageText = "-"
  $ageColor = "Green"
  if ($LastFromFile) {
    $rawAgeSec = ($now.ToUniversalTime() - $LastFromFile.UtcDateTime).TotalSeconds
    if ($rawAgeSec -lt 0) {
      $skewSec = [math]::Round([math]::Abs($rawAgeSec), 1)
      $ageText = "0.0 s (clock skew +$skewSec s)"
      $ageColor = "Cyan"
    } else {
      $ageSec = [math]::Round($rawAgeSec, 1)
      $ageText = "$ageSec s"
    }
    if ($rawAgeSec -ge 30) {
      $ageColor = "Red"
    } elseif ($rawAgeSec -ge 10) {
      $ageColor = "Yellow"
    }
  } elseif ($LastSeenAt) {
    $ageSec = [math]::Round(($now - $LastSeenAt).TotalSeconds, 1)
    $ageText = "$ageSec s (local)"
    if ($ageSec -ge 30) {
      $ageColor = "Red"
    } elseif ($ageSec -ge 10) {
      $ageColor = "Yellow"
    }
  }

  $watcherMinutes = [math]::Max(0.016, ($uptime.TotalMinutes))
  $updatesPerMin = [math]::Round($Updates / $watcherMinutes, 2)
  $hopsPerMin = [math]::Round($HopCount / $watcherMinutes, 2)

  $stats = $Status.stats
  $routing = $Status.routing
  $dock = $Status.stardock
  $startup = $Status.startupSurvey
  $mode = $Status.mode
  $supervisor = $Status.supervisor
  $events = $Status.events
  $trade = $Status.trade
  $upgrades = $Status.upgrades
  $kpi = $Status.kpi
  $localOpportunity = if ($null -ne $trade) { $trade.localOpportunity } else { $null }
  $warps = @($stats.warps)
  $recent = @($routing.recentSectors)
  $busted = @($routing.bustedSectors)

  $recentSlice = @()
  if ($recent.Count -gt 0) {
    $take = [math]::Min($SectorPreviewCount, $recent.Count)
    $recentSlice = $recent[($recent.Count - $take)..($recent.Count - 1)]
  }

  $uniqueRecent = 0
  if ($recent.Count -gt 0) {
    $uniqueRecent = (@($recent | Sort-Object -Unique)).Count
  }
  $recentText = Join-Limited -Items $recent -MaxItems $SectorPreviewCount
  $warpsText = Join-Limited -Items $warps -MaxItems 6
  $bustedText = Join-Limited -Items $busted -MaxItems 6
  $topActions = @($Actions.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 5)
  $topActionText = "-"
  if ($topActions.Count -gt 0) {
    $topActionText = ($topActions | ForEach-Object { "{0}:{1}" -f $_.Key, $_.Value }) -join " | "
  }

  Write-Host ("Now {0}  Up {1}" -f $now.ToString('HH:mm:ss'), $uptimeText)
  Write-Host "Freshness: " -NoNewline
  Write-Host $ageText -NoNewline -ForegroundColor $ageColor
  Write-Host ("   Updates:{0} ({1}/m)  Err:{2}" -f $Updates, $updatesPerMin, $Errors)
  Write-Host ("User:{0}  Host:{1}:{2}" -f (Format-Value $Status.username), (Format-Value $Status.host), (Format-Value $Status.port))
  Write-Host ("Phase:{0}  Action:{1}" -f (Format-Value $Status.phase), (Format-Value $Status.currentAction))
  if ($null -ne $mode) {
    Write-Host ("Mode :{0}  Why:{1}" -f (Format-Value $mode.current), (Format-Value $mode.reason))
  }
  Write-Host ("Sec:{0} Prev:{1} Turns:{2} Cred:{3} Exp:{4} Align:{5}" -f (Format-Value $stats.sector), (Format-Value $routing.previousSector), (Format-Value $stats.turns), (Format-Value $stats.credits), (Format-Value $stats.exp), (Format-Value $stats.alignment))
  Write-Host ("Holds:{0} Empty:{1} Cargo:{2} Shld:{3} Figs:{4}" -f (Format-Value $stats.holds), (Format-Value $stats.emptyHolds), (Format-Value $stats.cargoHolds), (Format-Value $stats.shields), (Format-Value $stats.fighters))
  Write-Host ("Ship : ID:{0} Type:{1} Scan:{2}" -f (Format-Value $stats.shipId), (Format-Value $stats.shipType), (Format-Value $stats.scannerType))
  Write-Host ("Quick: Ore:{0} Org:{1} Equ:{2} Col:{3}" -f (Format-Value $stats.fuelHolds), (Format-Value $stats.organicsHolds), (Format-Value $stats.equipmentHolds), (Format-Value $stats.colonists))
  Write-Host ("Port:{0}/{1} Expl:{2} EqBuy:{3}" -f (Format-Value $stats.portClass), (Format-Value $stats.portProfile), (Format-Value $stats.explored), (Format-Value $stats.portEquipBuying))
  if ($null -ne $trade) {
    Write-Host ("Trade: CanNow:{0} Desired:{1} Outcome:{2} NetCred:{3}" -f (Format-Value $trade.localPortCanTradeNow), (Format-Value $trade.desiredCargo), (Format-Value $trade.lastOutcome), (Format-Value $trade.netCredits))
    Write-Host ("Trade: Deals OK/No/Reject = {0}/{1}/{2}" -f (Format-Value $trade.successfulDeals), (Format-Value $trade.noTradeDeals), (Format-Value $trade.rejectedDeals))
  }
  if ($null -ne $localOpportunity) {
    $sellFuel = if ($null -ne $localOpportunity.canSell) { $localOpportunity.canSell.fuel } else { $null }
    $sellOrg = if ($null -ne $localOpportunity.canSell) { $localOpportunity.canSell.organics } else { $null }
    $sellEq = if ($null -ne $localOpportunity.canSell) { $localOpportunity.canSell.equipment } else { $null }
    Write-Host ("Cargo: F/O/E={0}/{1}/{2} Empty:{3}" -f (Format-Value $localOpportunity.cargo.fuel), (Format-Value $localOpportunity.cargo.organics), (Format-Value $localOpportunity.cargo.equipment), (Format-Value $localOpportunity.emptyHolds))
    Write-Host ("Local: BuyDesired:{0} Sell(F/O/E):{1}/{2}/{3}" -f (Format-Value $localOpportunity.canBuyDesired), (Format-Value $sellFuel), (Format-Value $sellOrg), (Format-Value $sellEq))
  }
  Write-Host ("Dock:{0} AtDock:{1} Pod:{2}" -f (Format-Value $dock.sector), (Format-Value $dock.atStardock), (Format-Value $dock.escapePod))
  if ($null -ne $startup) {
    $startupText = "{0}/{1}" -f (Format-Value $startup.executed), (Format-Value $startup.total)
    $nextCmd = Format-Value $startup.nextCommand
    Write-Host ("Startup:{0} Done:{1} Next:{2}" -f $startupText, (Format-Value $startup.done), $nextCmd)
  }
  if ($null -ne $supervisor) {
    Write-Host ("Supv :Rcv:{0} Consec:{1} Freeze:{2}" -f (Format-Value $supervisor.recoveriesTotal), (Format-Value $supervisor.consecutiveRecoveries), (Format-Value $supervisor.freezeDetected))
  }
  if ($null -ne $upgrades) {
    $upgradeLast = $upgrades.lastAction
    $upgradeLastText = "-"
    if ($null -ne $upgradeLast -and $upgradeLast.atMs -gt 0) {
      $ageSec = if ($null -ne $upgradeLast.ageMs) { [math]::Round(([double]$upgradeLast.ageMs / 1000.0), 1) } else { $null }
      if ($null -ne $ageSec) {
        $upgradeLastText = "{0} ({1}s ago)" -f (Format-Value $upgradeLast.reason), $ageSec
      } else {
        $upgradeLastText = Format-Value $upgradeLast.reason
      }
    }
    Write-Host ("Upgr :Budget:{0} Shield/Fig/Hold Plan:{1}/{2}/{3}" -f (Format-Value $upgrades.budgetNow), (Format-Value $upgrades.plannedShieldBuy), (Format-Value $upgrades.plannedFighterBuy), (Format-Value $upgrades.plannedHoldsBuy))
    Write-Host ("Upgr :Last:{0}" -f $upgradeLastText)
  }
  if ($null -ne $events) {
    Write-Host ("Evts :Total:{0}" -f (Format-Value $events.totalPublished))
  }
  if ($null -ne $kpi) {
    if ($kpi.ready -eq $true) {
      Write-Host (
        "Rate : Cred/m:{0}  Act/m:{1}  Deal/m:{2}  NoElig/m:{3}" -f
        (Format-Value $kpi.creditsPerMin),
        (Format-Value $kpi.actionsPerMin),
        (Format-Value $kpi.successfulDealsPerMin),
        (Format-Value $kpi.noEligiblePerMin)
      )
    } else {
      Write-Host ("Rate : warming up KPI window ({0} samples, {1} ms observed)" -f (Format-Value $kpi.sampleCount), (Format-Value $kpi.windowObservedMs))
    }
  }
  Write-Host ("Hops:{0} ({1}/m) Recent:{2}/{3}u" -f $HopCount, $hopsPerMin, $recent.Count, $uniqueRecent)
  Write-Host ("Recent: {0}" -f $recentText)
  Write-Host ("Warps : {0}" -f $warpsText)
  Write-Host ("Busted: {0}" -f $bustedText)
  Write-Host ("Acts  : {0}" -f $topActionText)
  Write-Host ("TX    : {0}" -f (Format-Value $Status.lastTx))
  Write-Host ("Prompt: {0}" -f (Format-Value $Status.lastPromptLine))
  Write-Host ("RX    : {0}" -f (Format-Value $Status.lastRxLine))
  Write-Host ("Note  : {0}" -f (Format-Value $Status.note))
}

while ($true) {
  try {
    if (-not (Test-Path -LiteralPath $path)) {
      Render-Dashboard -Status $snapshot -FilePath $path -StartedAt $scriptStartedAt -LastSeenAt $lastUpdateSeenAt -LastFromFile $lastUpdateFromFile -Updates $updateCount -Errors $readErrorCount -HopCount $sectorHopCount -Actions $actionCounts -SectorPreviewCount $RecentSectorPreview
      Start-Sleep -Milliseconds $RefreshMs
      continue
    }

    $raw = Get-Content -LiteralPath $path -Raw
    $status = $raw | ConvertFrom-Json

    if ($status.updatedAt -ne $lastTimestamp) {
      $previousSector = $lastSeenSector
      $currentSector = $status.stats.sector
      if ($null -ne $currentSector -and $currentSector -ne "") {
        if ($null -ne $previousSector -and [string]$previousSector -ne [string]$currentSector) {
          $sectorHopCount++
        }
        $lastSeenSector = $currentSector
      }

      $actionName = [string]$status.currentAction
      if (-not [string]::IsNullOrWhiteSpace($actionName)) {
        if (-not $actionCounts.ContainsKey($actionName)) {
          $actionCounts[$actionName] = 0
        }
        $actionCounts[$actionName]++
      }

      $snapshot = $status
      $lastTimestamp = $status.updatedAt
      $lastUpdateSeenAt = Get-Date
      $lastUpdateFromFile = Parse-UpdateTime $status.updatedAt
      $updateCount++
    }
  } catch {
    $readErrorCount++
  }

  Render-Dashboard -Status $snapshot -FilePath $path -StartedAt $scriptStartedAt -LastSeenAt $lastUpdateSeenAt -LastFromFile $lastUpdateFromFile -Updates $updateCount -Errors $readErrorCount -HopCount $sectorHopCount -Actions $actionCounts -SectorPreviewCount $RecentSectorPreview
  Start-Sleep -Milliseconds $RefreshMs
}
