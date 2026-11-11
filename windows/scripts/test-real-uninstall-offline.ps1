param(
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [Parameter(Mandatory = $true)][string]$ApplicationDirectory,
    [string]$BaselineCompilerPath,
    [string]$DotnetPath = 'dotnet',
    [string]$ArtifactDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('zhuodazi-real-uninstall-' + [Guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$nativeRoot = Split-Path -Parent $PSScriptRoot
$root = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $root) { throw 'Use a new isolated artifact directory.' }
New-Item -ItemType Directory -Path $root | Out-Null
$fixtureId = [Guid]::NewGuid().ToString('D')
$fixtureDirectory = Join-Path $root 'fixture'
$installerDirectory = Join-Path $root 'installer'
$installedDirectory = Join-Path $root 'installed'
$dataDirectory = Join-Path $root 'preview'
New-Item -ItemType Directory -Path $installerDirectory,$dataDirectory | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$savedDotnetRoot = $env:DOTNET_ROOT
$dotnetCommand = (Get-Command $DotnetPath).Source
$env:DOTNET_ROOT = Split-Path -Parent $dotnetCommand

try {
    & $dotnetCommand publish (Join-Path $nativeRoot 'tests/UninstallInstaller.Fixture/UninstallInstaller.Fixture.csproj') -c Release -o $fixtureDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
    $fixtureExe = Join-Path $fixtureDirectory 'ZhuoDazi.exe'
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $source = [IO.File]::ReadAllText((Join-Path $nativeRoot 'installer/ZhuoDazi.iss'))
    $baselineSource = if ($BaselineCompilerPath) { [IO.File]::ReadAllText((Join-Path $nativeRoot 'tests/UninstallInstaller.Fixture/Legacy3.5.7.iss')) } else { $null }
    $source = $source.Replace('AppId={{B8FFDA14-C49B-4C72-91E5-A88A92747D26}', ('AppId={{' + $fixtureId + '}'))
    $source = $source.Replace('AppName=桌搭子', 'AppName=ZhuoDazi real uninstall verification')
    $source = $source.Replace('DefaultDirName={localappdata}\Programs\ZhuoDazi', ('DefaultDirName=' + $installedDirectory))
    $source = $source.Replace('SetupIconFile=..\build\app-icon.ico', ('SetupIconFile=' + (Join-Path $nativeRoot 'build/app-icon.ico')))
    $source = $source.Replace("AutoStartKey = 'Software\Microsoft\Windows\CurrentVersion\Run'", ("AutoStartKey = 'Software\ZhuoDaziUninstallFixture\" + $fixtureId + "\Run'"))
    $source = $source.Replace("InstallEventsUrl = 'https://in.desktoppet.online/api/device/install-events'", ("InstallEventsUrl = 'http://127.0.0.1:" + $port + "/api/device/install-events'"))
    $source = $source.Replace('{userappdata}\poko-desktop-pet\install-batch.json', (Join-Path $root 'install-batch.json').Replace("'","''"))
    $source = [regex]::Replace($source, '(?ms)^\[Icons\]\r?\n.*?(?=^\[)', "[Icons]`n")
    $source = [regex]::Replace($source, '(?ms)^\[Run\]\r?\n.*?(?=^\[)', "[Run]`n")
    # Only the optional feedback UI/receipt is synthetic. Process cleanup must
    # execute the actual installed application and the production Pascal code.
    $feedbackStart = $source.IndexOf('function InitializeUninstall: Boolean;')
    $feedbackEnd = $source.IndexOf('procedure CaptureInstallBatchMarker;', $feedbackStart)
    $feedback = $source.Substring($feedbackStart, $feedbackEnd - $feedbackStart)
    $feedback = $feedback.Replace("HelperPath := ExpandConstant('{app}\app\ZhuoDazi.exe');", ("HelperPath := '" + $fixtureExe.Replace("'","''") + "';"))
    $source = $source.Substring(0,$feedbackStart) + $feedback + $source.Substring($feedbackEnd)
    if ($source.Contains('https://in.desktoppet.online') -or $source.Contains('Software\Microsoft\Windows\CurrentVersion\Run') -or $source.Contains('{userappdata}\poko-desktop-pet')) { throw 'Test isolation failed.' }
    $testScript = Join-Path $installerDirectory 'ZhuoDazi.iss'
    [IO.File]::WriteAllText($testScript,$source,[Text.UTF8Encoding]::new($false))
    if ($BaselineCompilerPath) {
        # Recreate the released 3.5.7 uninstall script with its original compiler.
        # Using identical payload files keeps this test focused on installer upgrades.
        $baselineSource = $baselineSource.Replace('AppId={{B8FFDA14-C49B-4C72-91E5-A88A92747D26}', ('AppId={{' + $fixtureId + '}'))
        $baselineSource = $baselineSource.Replace('AppName=桌搭子', 'AppName=ZhuoDazi real uninstall verification')
        $baselineSource = $baselineSource.Replace('DefaultDirName={localappdata}\Programs\ZhuoDazi', ('DefaultDirName=' + $installedDirectory))
        $baselineSource = $baselineSource.Replace('SetupIconFile=..\build\app-icon.ico', ('SetupIconFile=' + (Join-Path $nativeRoot 'build/app-icon.ico')))
        $baselineSource = $baselineSource.Replace("AutoStartKey = 'Software\Microsoft\Windows\CurrentVersion\Run'", ("AutoStartKey = 'Software\ZhuoDaziUninstallFixture\" + $fixtureId + "\Run'"))
        $baselineSource = $baselineSource.Replace("InstallEventsUrl = 'https://in.desktoppet.online/api/device/install-events'", ("InstallEventsUrl = 'http://127.0.0.1:" + $port + "/api/device/install-events'"))
        $baselineSource = $baselineSource.Replace('{userappdata}\poko-desktop-pet\install-batch.json', (Join-Path $root 'install-batch.json').Replace("'","''"))
        $baselineSource = $baselineSource.Replace("HelperPath := ExpandConstant('{app}\app\ZhuoDazi.exe');", ("HelperPath := '" + $fixtureExe.Replace("'","''") + "';"))
        $baselineSource = [regex]::Replace($baselineSource, '(?ms)^\[Icons\]\r?\n.*?(?=^\[)', "[Icons]`n")
        $baselineSource = [regex]::Replace($baselineSource, '(?ms)^\[Run\]\r?\n.*?(?=^\[)', "[Run]`n")
        if ($baselineSource.Contains('https://in.desktoppet.online') -or $baselineSource.Contains('{userappdata}\poko-desktop-pet')) { throw 'Baseline isolation failed.' }
        $baselineScript = Join-Path $installerDirectory 'baseline.iss'
        [IO.File]::WriteAllText($baselineScript,$baselineSource,[Text.UTF8Encoding]::new($false))
        & $BaselineCompilerPath /Q '/DAppVersion=3.5.7' "/DSourceDir=$ApplicationDirectory" "/DOutputDir=$installerDirectory" $baselineScript
        if ($LASTEXITCODE -ne 0) { throw 'Baseline installer build failed.' }
        $baselineInstaller = Join-Path $installerDirectory 'ZhuoDazi-Desktop-Pet-3.5.7.exe'
        $baselineInstall = Start-Process -FilePath $baselineInstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOAPPSTART',('/DIR="'+$installedDirectory+'"'),('/LOG="'+(Join-Path $root 'baseline-install.log')+'"')) -WindowStyle Hidden -PassThru -Wait
        if ($baselineInstall.ExitCode -ne 0) { throw 'Baseline installation failed.' }
    }
    & $CompilerPath /Q '/DAppVersion=3.5.8' "/DSourceDir=$ApplicationDirectory" "/DOutputDir=$installerDirectory" $testScript
    if ($LASTEXITCODE -ne 0) { throw 'Real-app isolated installer build failed.' }
    $installer = Join-Path $installerDirectory 'ZhuoDazi-Desktop-Pet-3.5.8.exe'
    $install = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOAPPSTART',('/DIR="' + $installedDirectory + '"'),('/LOG="' + (Join-Path $root 'install.log') + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($install.ExitCode -ne 0) { throw 'Installation failed.' }
    $configuration = @{
        baseUrl='http://127.0.0.1:9'; dataDirectory=$dataDirectory; accountId=[Guid]::NewGuid().ToString();
        licenseId=[Guid]::NewGuid().ToString(); installationId=[Guid]::NewGuid().ToString('N');
        credential=([Guid]::NewGuid().ToString('N')+[Guid]::NewGuid().ToString('N'));
        signingPublicKeySpki=[Convert]::ToBase64String([byte[]]::new(32))
    }
    $configurationPath = Join-Path $dataDirectory 'offline-preview.json'
    $configuration | ConvertTo-Json | Set-Content -LiteralPath $configurationPath -Encoding utf8NoBOM
    [IO.File]::WriteAllText((Join-Path $root 'install-batch.json'),'{"fixture":true}')
    $application = Join-Path $installedDirectory 'app/ZhuoDazi.exe'
    $target = Start-Process -FilePath $application -ArgumentList @('--app-preview-config',('"'+$configurationPath+'"')) -WindowStyle Hidden -PassThru
    $processes.Add($target)
    Start-Sleep -Seconds 5
    $target.Refresh()
    if ($target.HasExited -or $target.MainWindowTitle -notlike '*Windows 本地预览*') { throw 'Real application did not start in isolated preview.' }
    $requestLog = Join-Path $root 'requests.jsonl'
    $server = Start-Process -FilePath $fixtureExe -ArgumentList @('--server',"$port",('"'+$requestLog+'"'),'success') -WindowStyle Hidden -PassThru
    $processes.Add($server)
    $wait = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath ($requestLog+'.ready'))) {
        if ($server.HasExited -or $wait.Elapsed.TotalSeconds -gt 10) { throw 'Loopback server did not start.' }
        Start-Sleep -Milliseconds 50
    }
    $uninstaller = Join-Path $installedDirectory 'uninstall/unins000.exe'
    $uninstallerVersion = (Get-Item -LiteralPath $uninstaller).VersionInfo.ProductVersion.Trim()
    $removal = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $root 'uninstall.log')+'"')) -WindowStyle Hidden -PassThru -Wait
    $target.Refresh()
    $result = [pscustomobject]@{InstalledUninstallerVersion=$uninstallerVersion;ExitCode=$removal.ExitCode;TargetExited=$target.HasExited;ExecutableRemoved=(-not(Test-Path -LiteralPath $application));RequestCount=@(Get-Content -LiteralPath $requestLog -ErrorAction SilentlyContinue).Count;ArtifactDirectory=$root}
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding utf8NoBOM
    $result | ConvertTo-Json -Compress | Write-Host
    if ($uninstallerVersion -ne '3.5.8' -or $removal.ExitCode -ne 0 -or -not $target.HasExited -or -not $result.ExecutableRemoved -or $result.RequestCount -ne 1) { throw 'Real-application uninstall did not stop, remove and report correctly.' }
}
finally {
    foreach($process in $processes) {
        try { if(-not $process.HasExited) { $process.Kill(); $process.WaitForExit(3000) | Out-Null } } catch { }
        $process.Dispose()
    }
    $env:DOTNET_ROOT=$savedDotnetRoot
}
