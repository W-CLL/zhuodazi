param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '2.4.1'
)

$ErrorActionPreference = 'Stop'
$nativeRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $nativeRoot 'ZhuoDazi\ZhuoDazi.csproj'
$publishDirectory = Join-Path $nativeRoot 'publish\win-x64'
$installerScript = Join-Path $nativeRoot 'installer\ZhuoDazi.iss'
$outputDirectory = Join-Path $nativeRoot 'dist'
$compilerCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) {
    throw 'Inno Setup 6 was not found. Install it with: winget install --id JRSoftware.InnoSetup --exact'
}

$resolvedNativeRoot = [System.IO.Path]::GetFullPath($nativeRoot)
$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublishDirectory.StartsWith(
    $resolvedNativeRoot + [System.IO.Path]::DirectorySeparatorChar,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish directory resolved outside the native project.'
}
if (Test-Path -LiteralPath $resolvedPublishDirectory) {
    Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedPublishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version `
    -p:FileVersion="$Version.0" `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

& $compiler "/DAppVersion=$Version" "/DSourceDir=$publishDirectory" "/DOutputDir=$outputDirectory" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

$installerPath = Join-Path $outputDirectory "ZhuoDazi-Desktop-Pet-$Version.exe"
$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
Write-Host "Installer: $installerPath"
Write-Host "SHA-256: $($hash.Hash.ToLowerInvariant())"
