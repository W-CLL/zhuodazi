param(
    [Parameter(Mandatory = $true)][string]$ApplicationPath,
    [Parameter(Mandatory = $true)][string]$DataDirectory
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
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }
}
