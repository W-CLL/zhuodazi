param(
    [Parameter(Mandatory = $true)][string]$ApplicationPath,
    [Parameter(Mandatory = $true)][string]$DataDirectory,
    [switch]$VerifyUninstallExit
)

$ErrorActionPreference = 'Stop'
$application = (Resolve-Path -LiteralPath $ApplicationPath).Path
$dataRoot = [System.IO.Path]::GetFullPath($DataDirectory)
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$configurationPath = Join-Path $dataRoot 'offline-package-preview.json'

# These synthetic values are valid only for the isolated loopback preview.
# The preview transport rejects every production URL before opening a socket.
$configuration = @{
    baseUrl = 'http://127.0.0.1:9'
    dataDirectory = $dataRoot
    accountId = [Guid]::NewGuid().ToString()
    licenseId = [Guid]::NewGuid().ToString()
    installationId = [Guid]::NewGuid().ToString('N')
    credential = ([Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N'))
    signingPublicKeySpki = [Convert]::ToBase64String([byte[]]::new(32))
}
$configuration | ConvertTo-Json | Set-Content -LiteralPath $configurationPath -Encoding utf8NoBOM

$process = Start-Process -FilePath $application `
    -ArgumentList @('--app-preview-config', ('"' + $configurationPath + '"')) `
    -WorkingDirectory (Split-Path -Parent $application) -PassThru -WindowStyle Hidden
try {
    Start-Sleep -Seconds 5
    $process.Refresh()
    if ($process.HasExited) {
        throw "Isolated package preview exited during startup: $($process.ExitCode)"
    }
    if (-not $process.Responding -or $process.MainWindowTitle -notlike '*Windows 本地预览*') {
        throw 'Isolated package preview did not open its expected application window.'
    }
    Write-Host "Isolated package startup passed: $($process.MainWindowTitle)"
    if ($VerifyUninstallExit) {
        $duplicate = Start-Process -FilePath $application `
            -ArgumentList @('--app-preview-config', ('"' + $configurationPath + '"')) -PassThru -WindowStyle Hidden
        if (-not $duplicate.WaitForExit(5000)) {
            Stop-Process -Id $duplicate.Id -Force
            throw 'The normal second instance did not exit.'
        }
        Start-Sleep -Milliseconds 600
        if ($process.HasExited) { throw 'Normal second-instance exit stopped the primary application.' }
        # The normal startup must not reopen a tray process during file removal.
        $fence = [Threading.Mutex]::new($false, 'Local\ZhuoDazi.Uninstall.InProgress')
        try {
            $blockedStart = Start-Process -FilePath $application -ArgumentList '--settings' -PassThru -WindowStyle Hidden
            if (-not $blockedStart.WaitForExit(5000)) {
                Stop-Process -Id $blockedStart.Id -Force
                throw 'An application launched during uninstall did not exit.'
            }
            $helper = Start-Process -FilePath $application -ArgumentList '--prepare-uninstall' -PassThru -WindowStyle Hidden
            if (-not $helper.WaitForExit(15000)) {
                Stop-Process -Id $helper.Id -Force
                throw 'Uninstall process helper did not finish.'
            }
            if ($helper.ExitCode -ne 0 -or -not $process.WaitForExit(3000)) {
                throw "Uninstall preparation failed: helper=$($helper.ExitCode)"
            }
            if ($process.ExitCode -ne 0) { throw "Preview did not exit cleanly: $($process.ExitCode)" }
            Write-Host 'Packaged uninstall preparation passed: launch fence, scoped exit, no residual process.'
        }
        finally { $fence.Dispose() }
    }
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
}
