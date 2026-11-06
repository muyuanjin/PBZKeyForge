#requires -Version 5.1
param([Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$CacheDirectory = "$env:LOCALAPPDATA\PBZKeyForge\cache\ue4ss-build",
    [switch]$UpdatePinnedRelease)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$taskSources = Get-Content "$PSScriptRoot\ue4ss-sources.json" -Raw | ConvertFrom-Json
$taskMetadata = $taskSources.Bundled
if ($UpdatePinnedRelease) {
    . "$PSScriptRoot\ue4ss-release.ps1"
    $taskMetadata = (Get-PbzUe4ssRelease $taskSources -TimeoutSeconds 20).Metadata
}
if (-not $taskMetadata.Version -or -not $taskMetadata.AssetId -or -not $taskMetadata.Download -or -not $taskMetadata.Updated) {
    throw 'No pinned UE4SS release is configured in build/ue4ss-sources.json.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
$taskArchive = Join-Path $OutputDirectory 'ue4ss-download.zip'
$taskCache = Join-Path $CacheDirectory ("$($taskMetadata.AssetId)-$(([DateTimeOffset]$taskMetadata.Updated).UtcTicks).zip")
$taskTemporary = "$taskArchive.$([Guid]::NewGuid().ToString('N')).download"
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Assert-Ue4ssArchive([string]$Path) {
    $taskZip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $taskLibrary = @($taskZip.Entries | Where-Object { $_.FullName.Replace('\', '/') -match '(^|/)ue4ss/UE4SS.dll$' })
        if ($taskLibrary.Count -ne 1) { throw 'The archive does not contain one UE4SS runtime.' }
        $taskPrefix = $taskLibrary[0].FullName.Replace('\', '/') -replace 'ue4ss/UE4SS.dll$', ''
        if (-not ($taskZip.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq ($taskPrefix + 'dwmapi.dll') })) { throw 'The archive is missing its proxy loader.' }
    } finally { $taskZip.Dispose() }
}
try {
    $taskDownloaded = $false
    if (Test-Path -LiteralPath $taskCache) {
        $taskCacheValid = $false
        try {
            Assert-Ue4ssArchive $taskCache
            $taskCacheValid = $true
        } catch [IO.InvalidDataException] { Remove-Item -LiteralPath $taskCache -Force }
        if ($taskCacheValid) {
            Copy-Item -LiteralPath $taskCache -Destination $taskTemporary
            $taskDownloaded = $true
            Write-Host "Using cached Kintaro PBZ UE4SS $($taskMetadata.Version)."
        }
    }
    foreach ($taskPrefixUrl in $taskSources.DownloadPrefixes) {
        if ($taskDownloaded) { break }
        for ($taskAttempt = 0; $taskAttempt -lt 2 -and -not $taskDownloaded; $taskAttempt++) {
            try {
                Invoke-WebRequest -UseBasicParsing -Uri ($taskPrefixUrl + $taskMetadata.Download) -OutFile $taskTemporary -TimeoutSec 60 | Out-Null
                Assert-Ue4ssArchive $taskTemporary
                $taskDownloaded = $true
            }
            catch {
                if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary }
            }
        }
        if ($taskDownloaded) { break }
    }
    if (-not $taskDownloaded) { throw "Unable to download pinned Kintaro PBZ UE4SS $($taskMetadata.Version). Restore its ZIP to $taskCache or retry with network access." }
    Copy-Item -LiteralPath $taskTemporary -Destination $taskCache -Force
    Move-Item -LiteralPath $taskTemporary -Destination $taskArchive -Force
    $taskUtf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'ue4ss-release.json'), ($taskMetadata | ConvertTo-Json), $taskUtf8)
    if ($UpdatePinnedRelease) {
        $taskSources.Bundled = $taskMetadata
        [IO.File]::WriteAllText("$PSScriptRoot\ue4ss-sources.json", ($taskSources | ConvertTo-Json -Depth 5) + [Environment]::NewLine, $taskUtf8)
    }
    Write-Host "Prepared pinned Kintaro PBZ UE4SS $($taskMetadata.Version)."
}
finally {
    if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary }
}
