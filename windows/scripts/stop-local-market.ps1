param([string]$StateDirectory, [switch]$KeepWindow)
$ErrorActionPreference = 'Stop'
$clientRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $StateDirectory) { $StateDirectory = Join-Path $clientRoot '.local-review\local-market' }
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory)
$recordsPath = Join-Path $StateDirectory 'processes.json'
if (-not (Test-Path -LiteralPath $recordsPath)) { throw 'No local-test process record exists in this directory.' }
$records = Get-Content -LiteralPath $recordsPath -Raw | ConvertFrom-Json
if ([IO.Path]::GetFullPath($records.stateDirectory) -ne $StateDirectory) { throw 'Local-test directory identity does not match.' }
[IO.File]::WriteAllText((Join-Path $StateDirectory 'stop.signal'), 'stop', [Text.UTF8Encoding]::new($false))
foreach ($record in $records.processes) {
    $trackedProcess = Get-Process -Id $record.id -ErrorAction SilentlyContinue
    if (-not $trackedProcess) { continue }
    if ($trackedProcess.StartTime.ToUniversalTime() -ne ([DateTimeOffset]$record.startedAt).UtcDateTime) { continue }
    if ($record.role -eq 'window' -and -not $KeepWindow) { [void]$trackedProcess.CloseMainWindow() }
}
$remaining = @()
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    $remaining = @($records.processes | Where-Object {
        if ($KeepWindow -and $_.role -eq 'window') { return $false }
        $trackedProcess = Get-Process -Id $_.id -ErrorAction SilentlyContinue
        $trackedProcess -and $trackedProcess.StartTime.ToUniversalTime() -eq ([DateTimeOffset]$_.startedAt).UtcDateTime
    })
    if ($remaining.Count -eq 0) { break }
    Start-Sleep -Milliseconds 250
}
if ($remaining.Count -gt 0) {
    Write-Warning 'Stop was requested, but some tracked processes are still exiting. Do not restart until they have stopped.'
} else {
    if ($KeepWindow) {
        Write-Output 'The local payment service and listener have stopped. The test window is preserved.'
    } else {
        Write-Output 'The local payment test has stopped. Orders and diagnostic records are preserved.'
    }
}
