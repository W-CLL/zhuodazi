#Requires -Version 7.0
param(
    [string]$QrImagePath,
    [string]$WxlocalRoot,
    [string]$AccountDirectory,
    [string]$KeysFile,
    [string]$StateDirectory,
    [int]$Port = 18765,
    [switch]$NoBuild,
    [switch]$NoWindow
)

$ErrorActionPreference = 'Stop'
$clientRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$workspaceRoot = Split-Path -Parent $clientRoot
$projectsRoot = Split-Path -Parent $workspaceRoot
$serverRoot = Join-Path $workspaceRoot 'deskpet'
if (-not $WxlocalRoot) { $WxlocalRoot = Join-Path $projectsRoot 'wxlocal' }
if (-not $StateDirectory) { $StateDirectory = Join-Path $clientRoot '.local-review\local-market' }
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory)
$python = Join-Path $WxlocalRoot '.venv\Scripts\python.exe'
$node = (Get-Command node -ErrorAction Stop).Source
$sdk = Join-Path $workspaceRoot '.local-review\tooling\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
if (-not (Test-Path -LiteralPath $python)) { throw 'wxlocal Python environment was not found.' }
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw 'The test port is already in use. Stop the previous local test before starting another.'
}

$prepare = @((Join-Path $clientRoot 'scripts\local_market_environment.py'), '--wxlocal-root', $WxlocalRoot,
    '--state-dir', $StateDirectory, '--port', [string]$Port)
if ($QrImagePath) { $prepare += @('--qr-image', [IO.Path]::GetFullPath($QrImagePath)) }
if ($AccountDirectory) { $prepare += @('--account-dir', $AccountDirectory) }
if ($KeysFile) { $prepare += @('--keys-file', $KeysFile) }
& $python @prepare
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the local test environment.' }

$serverConfig = Join-Path $StateDirectory 'server.json'
$clientConfig = Join-Path $StateDirectory 'client.json'
$bridgeConfig = Join-Path $StateDirectory 'bridge.json'
$bridge = Join-Path $clientRoot 'scripts\local_market_payments.py'
$stopFile = Join-Path $StateDirectory 'stop.signal'
& $python $bridge --config $bridgeConfig --check-config
if ($LASTEXITCODE -ne 0) { throw 'Local payment listener configuration failed validation.' }

if (-not $NoBuild) {
    & $sdk build (Join-Path $clientRoot 'windows\ZhuoDazi\ZhuoDazi.csproj') -c Release -p:Platform=x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows local-test build failed.' }
}
$clientDll = Join-Path $clientRoot 'windows\ZhuoDazi\bin\x64\Release\net8.0-windows\ZhuoDazi.dll'
if (-not $NoWindow -and -not (Test-Path -LiteralPath $clientDll)) { throw 'Build the Windows client first.' }
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }

function Quote-Argument([string]$Value) {
    if ($Value.Contains('"')) { throw 'A local path contains an unsupported quote.' }
    return '"' + $Value + '"'
}

$records = [Collections.Generic.List[object]]::new()
if ($NoWindow -and (Test-Path -LiteralPath (Join-Path $StateDirectory 'processes.json'))) {
    $previousProcesses = Get-Content -LiteralPath (Join-Path $StateDirectory 'processes.json') -Raw | ConvertFrom-Json
    if ([IO.Path]::GetFullPath($previousProcesses.stateDirectory) -eq $StateDirectory) {
        foreach ($record in $previousProcesses.processes) {
            if ($record.role -ne 'window') { continue }
            $existingWindow = Get-Process -Id $record.id -ErrorAction SilentlyContinue
            if ($existingWindow -and $existingWindow.StartTime.ToUniversalTime() -eq ([DateTimeOffset]$record.startedAt).UtcDateTime) {
                $records.Add([ordered]@{role='window';id=$existingWindow.Id;startedAt=$existingWindow.StartTime.ToUniversalTime().ToString('o')})
            }
        }
    }
}
function Save-Process([string]$Role, $Process) {
    $records.Add([ordered]@{ role=$Role; id=$Process.Id; startedAt=$Process.StartTime.ToUniversalTime().ToString('o') })
    $state = [ordered]@{ stateDirectory=$StateDirectory; baseUrl="http://127.0.0.1:$Port"; processes=$records.ToArray() }
    [IO.File]::WriteAllText((Join-Path $StateDirectory 'processes.json'), ($state | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
}

try {
    $serverArgs = (Quote-Argument (Join-Path $serverRoot 'scripts\local-market-server.js')) + ' --config ' + (Quote-Argument $serverConfig)
    $server = Start-Process -FilePath $node -ArgumentList $serverArgs -WorkingDirectory $serverRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $StateDirectory 'server.stdout.log') -RedirectStandardError (Join-Path $StateDirectory 'server.stderr.log')
    Save-Process 'server' $server
    $healthy = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 2 -NoProxy
            if ($health.ok -and $health.data.mode -eq 'local-test') { $healthy=$true; break }
        } catch { }
        if ($server.HasExited) { break }
        Start-Sleep -Milliseconds 300
    }
    if (-not $healthy) { throw 'Local service did not become ready; inspect its local log.' }
    $bridgeArgs = (Quote-Argument $bridge) + ' --config ' + (Quote-Argument $bridgeConfig) + ' --stop-file ' + (Quote-Argument $stopFile)
    $listener = Start-Process -FilePath $python -ArgumentList $bridgeArgs -WorkingDirectory $clientRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $StateDirectory 'listener.stdout.log') -RedirectStandardError (Join-Path $StateDirectory 'listener.stderr.log')
    Save-Process 'listener' $listener
    $listenerReady = $false
    $listenerStatusPath = Join-Path $StateDirectory 'bridge.status.json'
    $listenerStartedAt = $listener.StartTime.ToUniversalTime()
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        if ($listener.HasExited) { break }
        if (Test-Path -LiteralPath $listenerStatusPath) {
            try {
                $listenerStatus = Get-Content -LiteralPath $listenerStatusPath -Raw | ConvertFrom-Json
                $pollAt = ([DateTimeOffset]$listenerStatus.lastSuccessfulPollAt).UtcDateTime
                if ($listenerStatus.running -and $listenerStatus.baselineReady -and
                    $listenerStatus.lastPollSuccess -and $pollAt -ge $listenerStartedAt) {
                    $listenerReady = $true
                    break
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $listenerReady) { throw 'The payment listener did not establish a fresh baseline. Inspect bridge.status.json and local listener logs before paying.' }
    if ($listenerStatus.pollLimitSaturated) {
        Write-Warning 'The payment-notice polling limit was reached. Review listener health before relying on notification coverage.'
    }
    if (-not $NoWindow) {
        $clientArgs = (Quote-Argument $clientDll) + ' --local-market-config ' + (Quote-Argument $clientConfig)
        $window = Start-Process -FilePath $sdk -ArgumentList $clientArgs -WorkingDirectory $clientRoot -WindowStyle Normal -PassThru
        Save-Process 'window' $window
    }
    Write-Output "Local payment test started: http://127.0.0.1:$Port"
    Write-Output 'Payment listener is running; notice and transfer baselines are ready.'
    Write-Output "State: $StateDirectory"
    Write-Output 'Only the local 0.01 CNY fixture is available. Friend transfers need no QR image; collection-code mode uses your local image.'
} catch {
    [IO.File]::WriteAllText($stopFile, 'stop', [Text.UTF8Encoding]::new($false))
    throw
}
