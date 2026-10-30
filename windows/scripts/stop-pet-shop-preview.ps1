#Requires -Version 7.0
param([string]$StateDirectory)

$ErrorActionPreference = 'Stop'
$clientRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $StateDirectory) { $StateDirectory = Join-Path $clientRoot '.local-review\pet-shop-showcase-20261029' }
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory)
$markerPath = Join-Path $StateDirectory 'local-pet-market-demo.json'
$processPath = Join-Path $StateDirectory 'payment-preview-processes.json'
if (-not (Test-Path -LiteralPath $markerPath) -or -not (Test-Path -LiteralPath $processPath)) {
    throw 'No matching local pet shop launcher record was found.'
}
$marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
$record = Get-Content -LiteralPath $processPath -Raw | ConvertFrom-Json
if ($marker.kind -ne 'deskpet-local-market-demo-v1' -or $record.stateDirectory -ne $StateDirectory -or
    $record.baseUrl -ne $marker.baseUrl -or $record.listenerStop -ne (Join-Path $StateDirectory 'payment-listener.stop') -or
    $record.serverStop -ne (Join-Path $StateDirectory 'market-stop.signal')) {
    throw 'Local pet shop paths do not match; nothing was stopped.'
}
# Let the listener report offline before the local service shuts down.
[IO.File]::WriteAllText($record.listenerStop, 'stop', [Text.UTF8Encoding]::new($false))
foreach ($saved in @($record.processes | Where-Object role -eq 'listener')) {
    $process = Get-Process -Id $saved.id -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime() -eq ([DateTimeOffset]$saved.startedAt).UtcDateTime) {
        $null = $process.WaitForExit(12000)
    }
}
[IO.File]::WriteAllText($record.serverStop, 'stop', [Text.UTF8Encoding]::new($false))
Write-Output 'Stop signals sent to this isolated shop and its listener. Other environments and the Windows preview were left unchanged.'
