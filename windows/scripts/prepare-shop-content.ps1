#Requires -Version 7.0
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$clientRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $clientRoot '.local-review\pet-shop-showcase-20261029\free-content' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$items = @()
foreach ($source in @(
    @{ folder='互动词包'; category='interaction-words'; description='覆盖日常互动场景的完整台词词库，可与其他已启用词库共同使用。' },
    @{ folder='小剧场'; category='theater-scripts'; description='主桌宠与搭档的五轮小剧场对白，安装后可手动启用加入演出。' }
)) {
    foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $clientRoot ('content-packs\' + $source.folder)) -Filter '*.json' | Sort-Object Name)) {
        $document = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $title = if ($document.name) { [string]$document.name } else { $file.BaseName }
        $archivePath = Join-Path $OutputDirectory ($source.category + '-' + $file.BaseName + '.zip')
        if (-not (Test-Path -LiteralPath $archivePath)) {
            $archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
            try { $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $file.Name, [IO.Compression.CompressionLevel]::Optimal) }
            finally { $archive.Dispose() }
        }
        $items += [ordered]@{ category=$source.category; title=$title; description=$source.description; version='1.0.0'; archive=$archivePath }
    }
}
$manifestPath = Join-Path $OutputDirectory 'catalog.json'
[IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject @($items) -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output "Prepared $($items.Count) free content packs: $manifestPath"
