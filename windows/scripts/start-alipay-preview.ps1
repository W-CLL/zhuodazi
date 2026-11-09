#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$ConfigPath, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$clientRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { throw 'Local Alipay preview configuration was not found.' }
$sdk = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$project = Join-Path $clientRoot 'windows\ZhuoDazi\ZhuoDazi.csproj'
$binary = Join-Path $clientRoot 'windows\ZhuoDazi\bin\x64\Release\net8.0-windows\ZhuoDazi.dll'
if (-not $NoBuild) {
    & $sdk build $project -c Release -p:Platform=x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Local preview build failed.' }
}
if (-not (Test-Path -LiteralPath $binary)) { throw 'Build the local preview first.' }
if ($binary.Contains('"') -or $ConfigPath.Contains('"')) { throw 'Unsupported quote in local path.' }
Start-Process -FilePath $sdk -ArgumentList ('"' + $binary + '" --alipay-preview-config "' + $ConfigPath + '"') -WorkingDirectory $clientRoot -WindowStyle Hidden
