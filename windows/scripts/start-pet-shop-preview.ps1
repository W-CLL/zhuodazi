#Requires -Version 7.0
param(
    [string]$StateDirectory,
    [string]$BridgeSourceDirectory,
    [int]$Port = 18767,
    [switch]$NoBuild,
    [switch]$NoWindow
)

$ErrorActionPreference = 'Stop'
$clientRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$workspaceRoot = Split-Path -Parent $clientRoot
$serverRoot = Join-Path $workspaceRoot 'deskpet'
if (-not $StateDirectory) { $StateDirectory = Join-Path $clientRoot '.local-review\pet-shop-showcase-20261029' }
if (-not $BridgeSourceDirectory) { $BridgeSourceDirectory = Join-Path $clientRoot '.local-review\local-market-transfer' }
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory)
$BridgeSourceDirectory = [IO.Path]::GetFullPath($BridgeSourceDirectory)
if ($StateDirectory -eq $BridgeSourceDirectory -or $StateDirectory -eq [IO.Path]::GetPathRoot($StateDirectory) -or
    $StateDirectory.StartsWith('\\') -or $Port -lt 1024 -or $Port -gt 65535) {
    throw 'Use a separate local showcase directory and an unprivileged port.'
}
$baseUrl = "http://127.0.0.1:$Port"
$markerPath = Join-Path $StateDirectory 'local-pet-market-demo.json'
if (-not (Test-Path -LiteralPath $markerPath)) { throw 'Prepare the isolated pet shop showcase before starting this launcher.' }
$marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
if ($marker.kind -ne 'deskpet-local-market-demo-v1' -or $marker.baseUrl -ne $baseUrl -or
    [IO.Path]::GetFullPath($marker.dataDirectory) -ne (Join-Path $StateDirectory 'server')) {
    throw 'The showcase marker does not match this local directory and port.'
}
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw 'The showcase port is already in use. Stop only its matching local service before restarting.'
}
$sourceBridgePath = Join-Path $BridgeSourceDirectory 'bridge.json'
$sourceCollectionPath = Join-Path $BridgeSourceDirectory 'server.json'
$externalMonitor = $null
$externalPath = Join-Path $StateDirectory 'external-payment-monitor.json'
if (Test-Path -LiteralPath $externalPath) {
    $externalMonitor = Get-Content -LiteralPath $externalPath -Raw | ConvertFrom-Json
    if ($externalMonitor.kind -ne 'zhuodazi-external-payments-v1' -or $externalMonitor.baseUrl -ne $baseUrl -or
        -not [IO.Path]::IsPathFullyQualified($externalMonitor.toolDirectory)) {
        throw 'External payment monitor marker does not match this local shop.'
    }
    $sourceBridgePath = Join-Path $externalMonitor.toolDirectory 'config\local.json'
}
$sourceBridge = Get-Content -LiteralPath $sourceBridgePath -Raw | ConvertFrom-Json
$python = Join-Path $sourceBridge.wxlocalRoot '.venv\Scripts\python.exe'
if ($externalMonitor) { $python = Join-Path $externalMonitor.toolDirectory 'runtime\python\python.exe' }
if (-not (Test-Path -LiteralPath $python)) { throw 'The existing wxlocal Python environment was not found.' }
$node = (Get-Command node -ErrorAction Stop).Source
$sdk = Join-Path $workspaceRoot '.local-review\tooling\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$bridgeScript = Join-Path $clientRoot 'scripts\local_market_payments.py'
$serverScript = Join-Path $serverRoot 'scripts\local-pet-market-demo.js'
$clientDll = Join-Path $clientRoot 'windows\ZhuoDazi\bin\x64\Release\net8.0-windows\ZhuoDazi.dll'
$serverStop = Join-Path $StateDirectory 'market-stop.signal'
$listenerStop = Join-Path $StateDirectory 'payment-listener.stop'
$bridgeConfig = Join-Path $StateDirectory 'bridge.json'
$statusPath = Join-Path $StateDirectory 'bridge.status.json'
$processPath = Join-Path $StateDirectory 'payment-preview-processes.json'

function Quote-Argument([string]$Value) {
    if ($Value.Contains('"')) { throw 'A local path contains an unsupported quote.' }
    return '"' + $Value + '"'
}

$records = [Collections.Generic.List[object]]::new()
function Save-Process([string]$Role, $Process) {
    $records.Add([ordered]@{role=$Role; id=$Process.Id; startedAt=$Process.StartTime.ToUniversalTime().ToString('o')})
    $state = [ordered]@{stateDirectory=$StateDirectory; baseUrl=$baseUrl; serverStop=$serverStop; listenerStop=$listenerStop; processes=$records.ToArray()}
    [IO.File]::WriteAllText($processPath, ($state | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
}

if (-not $NoBuild) {
    & $sdk build (Join-Path $clientRoot 'windows\ZhuoDazi\ZhuoDazi.csproj') -c Release -p:Platform=x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows preview build failed.' }
}
if (-not $NoWindow -and -not (Test-Path -LiteralPath $clientDll)) { throw 'Build the Windows preview first.' }
if (Test-Path -LiteralPath $listenerStop) { Remove-Item -LiteralPath $listenerStop }

try {
    $serverArguments = (Quote-Argument $serverScript) + ' --state-directory ' + (Quote-Argument $StateDirectory) +
        ' --port ' + $Port + ' --bridge-config ' + (Quote-Argument $sourceBridgePath) +
        ' --collection-config ' + (Quote-Argument $sourceCollectionPath)
    $serverProcess = Start-Process -FilePath $node -ArgumentList $serverArguments -WorkingDirectory $serverRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $StateDirectory 'payment-server.stdout.log') -RedirectStandardError (Join-Path $StateDirectory 'payment-server.stderr.log')
    Save-Process 'server' $serverProcess
    $serverReady = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($serverProcess.HasExited) { break }
        $runtimePath = Join-Path $StateDirectory 'runtime.json'
        if (Test-Path -LiteralPath $runtimePath) {
            try {
                $runtime = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
                if ($runtime.pid -eq $serverProcess.Id -and $runtime.baseUrl -eq $baseUrl -and (Test-Path -LiteralPath $bridgeConfig)) {
                    $serverReady = $true
                    break
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $serverReady) { throw 'The isolated shop service did not become ready; inspect its local log.' }
    if ($externalMonitor) {
        # The independent BAT owns its listener. Never start a second observer here.
        Write-Output ('Payment monitoring is managed separately: ' + (Join-Path $externalMonitor.toolDirectory 'start-payments.bat'))
        Write-Output 'Keep that status window open. The shop will show payment unavailable until its heartbeat is healthy.'
    } else {
    & $python $bridgeScript --config $bridgeConfig --check-config
    if ($LASTEXITCODE -ne 0) { throw 'The new payment listener configuration is invalid.' }
    $listenerArguments = (Quote-Argument $bridgeScript) + ' --config ' + (Quote-Argument $bridgeConfig) +
        ' --stop-file ' + (Quote-Argument $listenerStop)
    $listener = Start-Process -FilePath $python -ArgumentList $listenerArguments -WorkingDirectory $clientRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $StateDirectory 'payment-listener.stdout.log') -RedirectStandardError (Join-Path $StateDirectory 'payment-listener.stderr.log')
    Save-Process 'listener' $listener
    $listenerReady = $false
    $startedAt = $listener.StartTime.ToUniversalTime()
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        if ($listener.HasExited) { break }
        if (Test-Path -LiteralPath $statusPath) {
            try {
                $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
                if ($status.running -and $status.baselineReady -and $status.lastPollSuccess -and
                    $status.lastHeartbeatSuccess -and -not $status.pollLimitSaturated -and
                    ([DateTimeOffset]$status.lastHeartbeatAt).UtcDateTime -ge $startedAt) {
                    $listenerReady = $true
                    break
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $listenerReady) { throw 'The new listener has not established a healthy baseline and heartbeat. Payment remains unavailable.' }
    }
    if (-not $NoWindow) {
        $clientArguments = (Quote-Argument $clientDll) + ' --app-preview-config ' + (Quote-Argument (Join-Path $StateDirectory 'client.json'))
        $previewProcess = Start-Process -FilePath $sdk -ArgumentList $clientArguments -WorkingDirectory $clientRoot -WindowStyle Normal -PassThru
        Save-Process 'window' $previewProcess
    }
    Write-Output "Local pet shop ready: $baseUrl/admin#pet-market"
    if (-not $externalMonitor) { Write-Output 'The payment listener is connected with fresh baselines. Configure the public recipient in the shop admin.' }
} catch {
    [IO.File]::WriteAllText($listenerStop, 'stop', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($serverStop, 'stop', [Text.UTF8Encoding]::new($false))
    throw
}
