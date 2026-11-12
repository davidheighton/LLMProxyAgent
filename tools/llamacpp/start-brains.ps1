<#
.SYNOPSIS
  Starts the two llama.cpp brain servers: a small, fast Pilot and a larger, deliberating Strategic model.

.DESCRIPTION
  Each brain gets its own llama-server so both stay loaded and never wait for each other. Every MTC agent on this
  machine shares them: --Parallel is the number of agents a server answers at once. Re-running the script skips a
  brain whose server is already healthy.

.EXAMPLE
  .\start-brains.ps1 -PilotModel D:\models\Qwen3-1.7B-Q8_0.gguf -StrategicModel D:\models\Qwen3-14B-Q4_K_M.gguf

.EXAMPLE
  # 12 GB GPU, MoE strategist: keep 20 expert layers on the CPU so the rest fits in VRAM.
  .\start-brains.ps1 -PilotModel D:\models\Qwen3-0.6B-Q8_0.gguf `
    -StrategicModel D:\models\Qwen3-30B-A3B-Thinking-2507-Q4_K_M.gguf -StrategicExtraArgs "--n-cpu-moe 20"
#>
param(
    [Parameter(Mandatory)] [string] $PilotModel,
    [Parameter(Mandatory)] [string] $StrategicModel,
    [string] $ServerPath = "llama-server",
    [string] $PilotAlias = "pilot",
    [string] $StrategicAlias = "strategist",
    [int] $PilotPort = 8081,
    [int] $StrategicPort = 8082,
    [int] $Parallel = 4,
    [int] $PilotContext = 8192,
    [int] $StrategicContext = 16384,
    [int] $GpuLayers = 999,
    [string] $PilotExtraArgs = "",
    [string] $StrategicExtraArgs = "",
    [string] $LogDirectory = "$env:LOCALAPPDATA\LLMSurvivingProxyAgent\llama-server"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null

function Test-Healthy([int] $Port) {
    try { (Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 "http://127.0.0.1:$Port/health").StatusCode -eq 200 }
    catch { $false }
}

function Start-Brain([string] $Name, [string] $Model, [string] $Alias, [int] $Port, [int] $Context, [string] $Extra) {
    if (Test-Healthy $Port) { Write-Host "$Name brain already running on port $Port."; return }
    if (-not (Test-Path $Model)) { throw "$Name model not found: $Model" }
    # --ctx-size is shared by all slots, so give each of the $Parallel slots $Context tokens.
    $arguments = @(
        "--model", "`"$Model`"", "--alias", $Alias, "--host", "127.0.0.1", "--port", $Port,
        "--ctx-size", ($Context * $Parallel), "--parallel", $Parallel,
        "--n-gpu-layers", $GpuLayers, "--jinja"
    )
    if ($Extra) { $arguments += $Extra.Split(" ", [StringSplitOptions]::RemoveEmptyEntries) }
    $log = Join-Path $LogDirectory "$($Name.ToLowerInvariant()).log"
    Start-Process -FilePath $ServerPath -ArgumentList $arguments -WindowStyle Hidden `
        -RedirectStandardError $log -RedirectStandardOutput "$log.out" | Out-Null
    Write-Host "Starting $Name brain on port $Port (log: $log)..."
}

Start-Brain "Pilot" $PilotModel $PilotAlias $PilotPort $PilotContext $PilotExtraArgs
Start-Brain "Strategic" $StrategicModel $StrategicAlias $StrategicPort $StrategicContext $StrategicExtraArgs

$deadline = (Get-Date).AddMinutes(5)
foreach ($port in @($PilotPort, $StrategicPort)) {
    while (-not (Test-Healthy $port)) {
        if ((Get-Date) -gt $deadline) { throw "llama-server on port $port did not become healthy; see $LogDirectory." }
        Start-Sleep -Seconds 2
    }
    Write-Host "Port $port ready."
}

Write-Host ""
Write-Host "Point the agents at them (environment variables, or the same keys in appsettings.json):"
Write-Host "  `$env:Brains__Pilot__Backend = 'LlamaCpp';     `$env:Brains__Pilot__Model = '$PilotAlias'"
Write-Host "  `$env:Brains__Reactive__Backend = 'LlamaCpp';  `$env:Brains__Reactive__Model = '$PilotAlias'"
Write-Host "  `$env:Brains__Strategic__Backend = 'LlamaCpp'; `$env:Brains__Strategic__Model = '$StrategicAlias'"
