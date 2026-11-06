#requires -Version 5.1
param([Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$CacheDirectory = "$env:LOCALAPPDATA\PBZKeyForge\cache\ue4ss-build")
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$taskHeaders = @{ 'User-Agent' = 'PBZKeyForge-build' }
if ($env:GITHUB_TOKEN) { $taskHeaders.Authorization = 'Bearer ' + $env:GITHUB_TOKEN }
$taskSources = Get-Content "$PSScriptRoot\ue4ss-sources.json" -Raw | ConvertFrom-Json
$taskReleases = $null
foreach ($taskApi in $taskSources.Api) {
    try {
        $taskApiHeaders = @{ 'User-Agent' = 'PBZKeyForge-build' }
        if ($taskApi.StartsWith('https://api.github.com/') -and $env:GITHUB_TOKEN) { $taskApiHeaders.Authorization = $taskHeaders.Authorization }
        $taskReleases = Invoke-RestMethod -Uri $taskApi -Headers $taskApiHeaders -TimeoutSec 20
        if ($taskReleases -isnot [array]) { throw 'Invalid release response.' }
        break
    } catch { Write-Warning ("Release query failed via $(([Uri]$taskApi).Host): " + $_.Exception.Message); $taskReleases = $null }
}
if (-not $taskReleases) { throw 'Unable to check the latest Kintaro PBZ release. Check network access and retry.' }
$taskAsset = $null
foreach ($taskRelease in @($taskReleases | Where-Object { -not $_.draft -and -not $_.prerelease } | Sort-Object published_at -Descending)) {
    $taskAsset = $taskRelease.assets | Where-Object { $_.name -match '^phantombladezero_ue4ss_.*\.zip$' } | Select-Object -First 1
    if ($taskAsset) { break }
}
if (-not $taskAsset) { throw 'No current Kintaro Phantom Blade Zero runtime release was found.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
$taskArchive = Join-Path $OutputDirectory 'ue4ss-download.zip'
$taskCache = Join-Path $CacheDirectory ("$($taskAsset.id)-$(([DateTime]$taskAsset.updated_at).ToUniversalTime().Ticks).zip")
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
        try {
            Assert-Ue4ssArchive $taskCache
            Copy-Item -LiteralPath $taskCache -Destination $taskTemporary
            $taskDownloaded = $true
            Write-Host "Using cached Kintaro PBZ UE4SS $($taskRelease.tag_name)."
        } catch { Remove-Item -LiteralPath $taskCache -Force }
    }
    foreach ($taskPrefixUrl in $taskSources.DownloadPrefixes) {
        if ($taskDownloaded) { break }
        for ($taskAttempt = 0; $taskAttempt -lt 2 -and -not $taskDownloaded; $taskAttempt++) {
            try {
                Invoke-WebRequest -UseBasicParsing -Uri ($taskPrefixUrl + $taskAsset.browser_download_url) -OutFile $taskTemporary -TimeoutSec 60 | Out-Null
                Assert-Ue4ssArchive $taskTemporary
                $taskDownloaded = $true
            }
            catch {
                if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary }
            }
        }
        if ($taskDownloaded) { break }
    }
    if (-not $taskDownloaded) { throw 'Unable to download the latest Kintaro PBZ runtime from GitHub. No cached or older runtime was substituted.' }
    Copy-Item -LiteralPath $taskTemporary -Destination $taskCache -Force
    Move-Item -LiteralPath $taskTemporary -Destination $taskArchive -Force
    $taskMetadata = [ordered]@{
        Provider = 'KintaroModding'; Version = [string]$taskRelease.tag_name
        Source = [string]$taskRelease.html_url; Asset = [string]$taskAsset.name
        Published = ([DateTimeOffset]$taskRelease.published_at).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
        Download = [string]$taskAsset.browser_download_url; AssetId = [string]$taskAsset.id
        Updated = ([DateTimeOffset]$taskAsset.updated_at).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
    }
    $taskUtf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'ue4ss-release.json'), ($taskMetadata | ConvertTo-Json), $taskUtf8)
    Write-Host "Prepared latest Kintaro PBZ UE4SS $($taskRelease.tag_name)."
}
finally {
    if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary }
}
