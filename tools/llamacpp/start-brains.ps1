<#
.SYNOPSIS
  Starts the two llama.cpp brain servers: a small, fast Pilot and a larger, deliberating Strategic model.

.DESCRIPTION
  Each brain gets its own llama-server so both stay loaded and never wait for each other. Every MTC agent on this
  machine shares them: --Parallel is the number of agents a server answers at once. Re-running the script skips a
  brain whose server is already healthy.

.EXAMPLE
  # 16 GB GPU (e.g. RTX 3080 Ti Laptop), 1-2 agents: 4B Pilot + 8B strategist with a 0.6B draft model.
  .\start-brains.ps1 -PilotModel D:\models\Qwen3-4B-Instruct-2507-Q4_K_M.gguf `
    -StrategicModel D:\models\Qwen3-8B-Q4_K_M.gguf -StrategicDraftModel D:\models\Qwen3-0.6B-Q8_0.gguf

.EXAMPLE
  # Same card, bigger strategist: 14B with shorter strategy context.
  .\start-brains.ps1 -PilotModel D:\models\Qwen3-4B-Instruct-2507-Q4_K_M.gguf `
    -StrategicModel D:\models\Qwen3-14B-Q4_K_M.gguf -StrategicContext 8192
#>
param(
    [Parameter(Mandatory)] [string] $PilotModel,
    [Parameter(Mandatory)] [string] $StrategicModel,
    [string] $ServerPath = "llama-server",
    [string] $PilotAlias = "pilot",
    [string] $StrategicAlias = "strategist",
    [int] $PilotPort = 8081,
    [int] $StrategicPort = 8082,
    [int] $Parallel = 2,
    [int] $PilotContext = 8192,
    [int] $StrategicContext = 16384,
    [int] $GpuLayers = 999,
    # 8-bit KV cache roughly halves context memory; it relies on flash attention, which CUDA builds enable by default.
    [string] $KvCacheType = "q8_0",
    # Small same-family model for speculative decoding of the strategist's long deliberation.
    [string] $StrategicDraftModel = "",
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

function Start-Brain([string] $Name, [string] $Model, [string] $Alias, [int] $Port, [int] $Context, [string] $Extra, [string] $Draft) {
    if (Test-Healthy $Port) { Write-Host "$Name brain already running on port $Port."; return }
    if (-not (Test-Path $Model)) { throw "$Name model not found: $Model" }
    # --ctx-size is shared by all slots, so give each of the $Parallel slots $Context tokens.
    $arguments = @(
        "--model", "`"$Model`"", "--alias", $Alias, "--host", "127.0.0.1", "--port", $Port,
        "--ctx-size", ($Context * $Parallel), "--parallel", $Parallel,
        "--n-gpu-layers", $GpuLayers, "--jinja"
    )
    if ($KvCacheType) { $arguments += @("--cache-type-k", $KvCacheType, "--cache-type-v", $KvCacheType) }
    if ($Draft) {
        if (-not (Test-Path $Draft)) { throw "$Name draft model not found: $Draft" }
        $arguments += @("--model-draft", "`"$Draft`"", "--gpu-layers-draft", $GpuLayers)
    }
    if ($Extra) { $arguments += $Extra.Split(" ", [StringSplitOptions]::RemoveEmptyEntries) }
    $log = Join-Path $LogDirectory "$($Name.ToLowerInvariant()).log"
    Start-Process -FilePath $ServerPath -ArgumentList $arguments -WindowStyle Hidden `
        -RedirectStandardError $log -RedirectStandardOutput "$log.out" | Out-Null
    Write-Host "Starting $Name brain on port $Port (log: $log)..."
}

Start-Brain "Pilot" $PilotModel $PilotAlias $PilotPort $PilotContext $PilotExtraArgs ""
Start-Brain "Strategic" $StrategicModel $StrategicAlias $StrategicPort $StrategicContext $StrategicExtraArgs $StrategicDraftModel

$deadline = (Get-Date).AddMinutes(5)
foreach ($port in @($PilotPort, $StrategicPort)) {
    while (-not (Test-Healthy $port)) {
        if ((Get-Date) -gt $deadline) { throw "llama-server on port $port did not become healthy; see $LogDirectory." }
        Start-Sleep -Seconds 2
    }
    Write-Host "Port $port ready."
}

if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
    Write-Host ""
    nvidia-smi --query-gpu=name,memory.used,memory.total --format=csv,noheader
}
Write-Host ""
Write-Host "Point the agents at them (environment variables, or the same keys in appsettings.json):"
Write-Host "  `$env:Brains__Pilot__Backend = 'LlamaCpp';     `$env:Brains__Pilot__Model = '$PilotAlias'"
Write-Host "  `$env:Brains__Reactive__Backend = 'LlamaCpp';  `$env:Brains__Reactive__Model = '$PilotAlias'"
Write-Host "  `$env:Brains__Strategic__Backend = 'LlamaCpp'; `$env:Brains__Strategic__Model = '$StrategicAlias'"
