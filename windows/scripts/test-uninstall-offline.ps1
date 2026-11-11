param(
    [Parameter(Mandatory = $true)][string]$CompilerPath,
    [string]$DotnetPath = 'dotnet',
    [string]$ArtifactDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) ('zhuodazi-uninstall-installer-' + [Guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$nativeRoot = Split-Path -Parent $PSScriptRoot
$sourceInstaller = Join-Path $nativeRoot 'installer\ZhuoDazi.iss'
$root = [System.IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $root) { throw 'Use a new, dedicated artifact directory for this isolated test.' }
New-Item -ItemType Directory -Path $root | Out-Null
$fixtureId = [Guid]::NewGuid().ToString('D')
$fixtureRegistryKey = 'Software\ZhuoDaziUninstallFixture\' + $fixtureId + '\Run'
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$cases = [System.Collections.Generic.List[object]]::new()
$buildDirectory = Join-Path $root 'fixture'
$testInstallerDirectory = Join-Path $root 'installer'
New-Item -ItemType Directory -Path $testInstallerDirectory | Out-Null
$batchPath = Join-Path $root 'install-batch.json'
$dotnetCommand = (Get-Command $DotnetPath).Source
$savedDotnetRoot = $env:DOTNET_ROOT
$env:DOTNET_ROOT = Split-Path -Parent $dotnetCommand

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Start-HiddenProcess([string]$FilePath, [string[]]$Arguments) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $processes.Add($process)
    return $process
}

function Wait-Ready([string]$Path, [System.Diagnostics.Process]$Process) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $Path)) {
        if ($Process.HasExited) { throw "Fixture exited before readiness: $($Process.ExitCode)" }
        if ($timer.Elapsed.TotalSeconds -gt 10) { throw "Fixture readiness timed out: $Path" }
        Start-Sleep -Milliseconds 50
    }
}

function Run-AndWait([string]$FilePath, [string[]]$Arguments) {
    # Inno's launcher can hand off to a temporary executable. PowerShell -Wait
    # waits for the process tree; Process.WaitForExit only waits for the launcher.
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    $processes.Add($process)
    return $process.ExitCode
}

function Write-FixtureSettings([string]$Directory, [string]$Choice, [bool]$FailPreparation = $false, [bool]$IgnoreExitSignal = $false) {
    @{ choice = $Choice; failPreparation = $FailPreparation; ignoreExitSignal = $IgnoreExitSignal } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $Directory 'fixture-settings.json') -Encoding utf8NoBOM
}

try {
    & $dotnetCommand publish (Join-Path $nativeRoot 'tests\UninstallInstaller.Fixture\UninstallInstaller.Fixture.csproj') --configuration Release --output $buildDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
    $portProbe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $portProbe.Start()
    $port = $portProbe.LocalEndpoint.Port
    $portProbe.Stop()
    $source = [System.IO.File]::ReadAllText($sourceInstaller)
    $source = $source.Replace('AppId={{B8FFDA14-C49B-4C72-91E5-A88A92747D26}', ('AppId={{' + $fixtureId + '}'))
    $source = $source.Replace('AppName=桌搭子', 'AppName=ZhuoDazi uninstall fixture')
    $source = $source.Replace('AppVerName=桌搭子', 'AppVerName=ZhuoDazi uninstall fixture')
    $source = $source.Replace('DefaultDirName={localappdata}\Programs\ZhuoDazi', ('DefaultDirName=' + (Join-Path $root 'installed')))
    $source = $source.Replace('DefaultGroupName=桌搭子', ('DefaultGroupName=ZhuoDazi uninstall fixture ' + $fixtureId))
    $source = $source.Replace('SetupIconFile=..\build\app-icon.ico', ('SetupIconFile=' + (Join-Path $nativeRoot 'build\app-icon.ico')))
    $source = $source.Replace("AutoStartKey = 'Software\Microsoft\Windows\CurrentVersion\Run'", ("AutoStartKey = '" + $fixtureRegistryKey + "'"))
    $source = $source.Replace("InstallEventsUrl = 'https://in.desktoppet.online/api/device/install-events'", ("InstallEventsUrl = 'http://127.0.0.1:" + $port + "/api/device/install-events'"))
    $source = $source.Replace('{userappdata}\poko-desktop-pet\install-batch.json', $batchPath.Replace("'", "''"))
    $source = $source.Replace('Local\ZhuoDazi.Uninstall.InProgress', ('Local\ZhuoDazi.UninstallFixture.' + $fixtureId))
    $source = [regex]::Replace($source, '(?ms)^\[Icons\]\r?\n.*?(?=^\[)', "[Icons]`n")
    $source = [regex]::Replace($source, '(?ms)^\[Run\]\r?\n.*?(?=^\[)', "[Run]`n")
    Assert-Check (-not $source.Contains('https://in.desktoppet.online')) 'Production endpoint survived test isolation.'
    Assert-Check (-not $source.Contains('Software\Microsoft\Windows\CurrentVersion\Run')) 'Production auto-start key survived test isolation.'
    Assert-Check (-not $source.Contains('{userappdata}\poko-desktop-pet')) 'Production data directory survived test isolation.'
    Assert-Check (-not $source.Contains('AppId={{B8FFDA14-C49B-4C72-91E5-A88A92747D26}')) 'Production installer identity survived test isolation.'
    Assert-Check (-not $source.Contains('Name: "{autodesktop}')) 'Production desktop shortcut survived test isolation.'
    $isolatedInstaller = Join-Path $testInstallerDirectory 'ZhuoDazi.iss'
    [System.IO.File]::WriteAllText($isolatedInstaller, $source, [System.Text.UTF8Encoding]::new($false))
    & $CompilerPath '/DAppVersion=0.0.1' "/DSourceDir=$buildDirectory" "/DOutputDir=$testInstallerDirectory" $isolatedInstaller
    if ($LASTEXITCODE -ne 0) { throw 'Isolated Inno installer build failed.' }
    $installer = Join-Path $testInstallerDirectory 'ZhuoDazi-Desktop-Pet-0.0.1.exe'
    $otherDirectory = Join-Path $root 'unrelated-installation'
    New-Item -ItemType Directory -Path $otherDirectory | Out-Null
    Copy-Item -Path (Join-Path $buildDirectory '*') -Destination $otherDirectory
    $otherReady = Join-Path $root 'other.ready'
    $other = Start-HiddenProcess (Join-Path $otherDirectory 'ZhuoDazi.exe') @('--keep-alive', ('"' + $otherReady + '"'))
    Wait-Ready $otherReady $other

    foreach ($case in @(
        @{ Name = 'skip-retry'; Choice = 'skip'; Server = 'retry'; Requests = 2; Removed = $true; Force = $true },
        @{ Name = 'confirm'; Choice = 'confirm'; Server = 'success'; Requests = 1; Removed = $true },
        @{ Name = 'cancel'; Choice = 'cancel'; Server = 'success'; Requests = 0; Removed = $false },
        @{ Name = 'prepare-failed'; Choice = 'skip'; Server = 'success'; Requests = 0; Removed = $false; FailPreparation = $true },
        @{ Name = 'unauthorized'; Choice = 'skip'; Server = 'unauthorized'; Requests = 1; Removed = $true },
        @{ Name = 'wrong-id'; Choice = 'skip'; Server = 'wrong-id'; Requests = 3; Removed = $true },
        @{ Name = 'stale'; Choice = 'skip'; Server = 'stale'; Requests = 3; Removed = $true }
    )) {
        $directory = Join-Path $root $case.Name
        New-Item -ItemType Directory -Path $directory | Out-Null
        $installed = Join-Path $directory 'installed'
        $installLog = Join-Path $directory 'install.log'
        $installCode = Run-AndWait $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOAPPSTART', ('/DIR="' + $installed + '"'), ('/LOG="' + $installLog + '"'))
        Assert-Check ($installCode -eq 0) "Fixture installation failed: $($case.Name), exit $installCode"
        $applicationDirectory = Join-Path $installed 'app'
        $application = Join-Path $applicationDirectory 'ZhuoDazi.exe'
        $uninstaller = Join-Path $installed 'uninstall\unins000.exe'
        Write-FixtureSettings $applicationDirectory $case.Choice ([bool]$case.FailPreparation) ([bool]$case.Force)
        [System.IO.File]::WriteAllText($batchPath, '{"fixture":true}')
        $ready = Join-Path $directory 'target.ready'
        $target = Start-HiddenProcess $application @('--keep-alive', ('"' + $ready + '"'))
        Wait-Ready $ready $target
        $requestLog = Join-Path $directory 'requests.jsonl'
        $server = Start-HiddenProcess (Join-Path $buildDirectory 'ZhuoDazi.exe') @('--server', "$port", ('"' + $requestLog + '"'), $case.Server)
        Wait-Ready ($requestLog + '.ready') $server
        $uninstallLog = Join-Path $directory 'uninstall.log'
        $uninstallCode = Run-AndWait $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + $uninstallLog + '"'))
        $requests = if (Test-Path -LiteralPath $requestLog) { @(Get-Content -LiteralPath $requestLog | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() }
        $uninstallText = [System.IO.File]::ReadAllText($uninstallLog)
        Assert-Check ($requests.Count -eq $case.Requests) "Unexpected completion request count for $($case.Name): $($requests.Count)"
        Assert-Check (-not $other.HasExited) "Unrelated same-named installation was stopped: $($case.Name)"
        if ($case.Removed) {
            Assert-Check ($uninstallCode -eq 0 -and -not (Test-Path -LiteralPath $application)) "Application not removed: $($case.Name), exit $uninstallCode"
            Assert-Check ($target.HasExited) "Installed process survived removal: $($case.Name)"
            Assert-Check (-not (Test-Path -LiteralPath $batchPath)) "Removed installation retained its batch marker: $($case.Name)"
        } else {
            Assert-Check ((Test-Path -LiteralPath $application) -and -not $target.HasExited) "Canceled or failed preparation changed the installation: $($case.Name), exit $uninstallCode"
            Assert-Check (Test-Path -LiteralPath $batchPath) "Canceled or failed preparation removed the batch marker: $($case.Name)"
        }
        if ($requests.Count -gt 0) {
            Assert-Check (@($requests.eventId | Select-Object -Unique).Count -eq 1) "Retry changed event ID: $($case.Name)"
            Assert-Check (@($requests.occurredAt | Select-Object -Unique).Count -eq 1) "Retry changed completion timestamp: $($case.Name)"
            Assert-Check (-not ($requests.occurredAt -contains '__POST_UNINSTALL_UTC__')) "Completion timestamp was not filled: $($case.Name)"
            Assert-Check (@($requests | Where-Object { $_.userAgent -notlike '*ZhuoDazi-Uninstaller/0.0.1*' }).Count -eq 0) 'Product user agent was missing.'
        }
        if ($case.Server -in @('success', 'retry') -and $case.Removed) {
            Assert-Check ($uninstallText.Contains('Uninstall completion acknowledged.')) "Successful completion was not acknowledged: $($case.Name)"
        } else {
            Assert-Check (-not $uninstallText.Contains('Uninstall completion acknowledged.')) "Invalid completion was treated as acknowledged: $($case.Name)"
        }
        $cases.Add([pscustomobject]@{ name = $case.Name; uninstallExitCode = $uninstallCode; completionRequests = $requests.Count; removed = $case.Removed; unrelatedProcessAlive = -not $other.HasExited; passed = $true })
        $cases | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8NoBOM
        Write-Host "Passed $($case.Name): exit $uninstallCode, requests $($requests.Count), removed $($case.Removed)"
        $server.Kill()
        $server.WaitForExit(3000) | Out-Null
        if (-not $case.Removed) {
            Write-FixtureSettings $applicationDirectory 'skip'
            $cleanupCode = Run-AndWait $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $directory 'cleanup.log') + '"'))
            Assert-Check ($cleanupCode -eq 0 -and -not (Test-Path -LiteralPath $application)) "Fixture cleanup failed: $($case.Name)"
        }
    }
    $cases | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8NoBOM
    Write-Host "Isolated Inno uninstall checks passed ($($cases.Count) scenarios). Artifacts: $root"
}
finally {
    foreach ($process in $processes) {
        try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(3000) | Out-Null } } catch { }
        $process.Dispose()
    }
    $env:DOTNET_ROOT = $savedDotnetRoot
}
