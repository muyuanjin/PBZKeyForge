#requires -Version 5.1
param([string]$GameDirectory,
    [switch]$Force,
    [switch]$Release,
    [string]$CacheFile = "$env:LOCALAPPDATA\PBZKeyForge\cache\runtime-updates.json",
    [string]$OutputFile)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
. "$PSScriptRoot\ue4ss-release.ps1"
$taskSources = Get-Content "$PSScriptRoot\ue4ss-sources.json" -Raw -Encoding UTF8 | ConvertFrom-Json

function Read-InstalledGameBuilds {
    $taskManifests = @()
    if ($GameDirectory) {
        $taskGameRoot = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\', '/')
        if (-not (Test-Path -LiteralPath (Join-Path $taskGameRoot 'platform\steam\PBZ.exe'))) {
            throw 'GameDirectory must contain platform/steam/PBZ.exe.'
        }
        $taskSteamApps = Split-Path (Split-Path $taskGameRoot -Parent) -Parent
        $taskManifests = @(Join-Path $taskSteamApps "appmanifest_$($taskSources.Game.SteamAppId).acf")
    }
    else {
        $taskSteam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
        if (-not $taskSteam) { $taskSteam = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath }
        if ($taskSteam) {
            $taskLibraries = @($taskSteam)
            $taskFolders = Join-Path $taskSteam 'steamapps\libraryfolders.vdf'
            if (Test-Path -LiteralPath $taskFolders) {
                foreach ($taskMatch in [regex]::Matches([IO.File]::ReadAllText($taskFolders), '"path"\s*"([^"]+)"')) {
                    $taskLibraries += $taskMatch.Groups[1].Value.Replace('\\', '\')
                }
            }
            $taskManifests = @($taskLibraries | Select-Object -Unique | ForEach-Object { Join-Path $_ "steamapps\appmanifest_$($taskSources.Game.SteamAppId).acf" })
        }
    }
    foreach ($taskManifest in $taskManifests) {
        if (-not (Test-Path -LiteralPath $taskManifest)) {
            if ($GameDirectory) { [pscustomobject]@{ Directory = $taskGameRoot; BuildId = $null } }
            continue
        }
        $taskText = [IO.File]::ReadAllText($taskManifest)
        $taskInstall = [regex]::Match($taskText, '"installdir"\s*"([^"]+)"').Groups[1].Value
        $taskRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $taskManifest -Parent) "common\$taskInstall"))
        if ($GameDirectory -and $taskRoot -ne $taskGameRoot) {
            [pscustomobject]@{ Directory = $taskGameRoot; BuildId = $null }
            continue
        }
        if (-not (Test-Path -LiteralPath (Join-Path $taskRoot 'platform\steam\PBZ.exe'))) { continue }
        $taskBuild = [regex]::Match($taskText, '"buildid"\s*"(\d+)"').Groups[1].Value
        [pscustomobject]@{ Directory = $taskRoot; BuildId = $taskBuild }
    }
}

$taskInstalled = @(Read-InstalledGameBuilds | Sort-Object Directory)
$taskInstalledText = ($taskInstalled | ConvertTo-Json -Compress) -join ''
$taskCached = $null
if (Test-Path -LiteralPath $CacheFile) {
    try { $taskCached = Get-Content -LiteralPath $CacheFile -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { $taskCached = $null }
}
$taskUseCache = $false
if (-not $Force -and -not $Release -and $taskCached) {
    try {
        $taskAge = [DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse($taskCached.CheckedAt)
        $taskUseCache = $taskAge.TotalHours -ge 0 -and $taskAge.TotalHours -lt 24 `
            -and $taskCached.SteamApi -eq $taskSources.Game.Api `
            -and ($taskCached.Ue4ssApi -join "`n") -eq ($taskSources.Api -join "`n") `
            -and $taskCached.InstalledText -eq $taskInstalledText `
            -and $null -ne $taskCached.Ue4ss.Metadata -and -not [string]::IsNullOrEmpty($taskCached.SteamBuildId)
    }
    catch { $taskUseCache = $false }
}
$taskErrors = @()
if ($taskUseCache) {
    $taskObservation = $taskCached
}
else {
    $taskObservation = [pscustomobject]@{
        CheckedAt = [DateTimeOffset]::UtcNow.ToString('o')
        Ue4ssApi = @($taskSources.Api); SteamApi = $taskSources.Game.Api
        InstalledText = $taskInstalledText; Ue4ss = $null; SteamBuildId = $null
    }
    try { $taskObservation.Ue4ss = Get-PbzUe4ssRelease $taskSources }
    catch { $taskErrors += 'UE4SS: ' + $_.Exception.Message }
    try {
        $taskSteamInfo = Invoke-RestMethod -Uri $taskSources.Game.Api -TimeoutSec 10 -Headers @{ 'User-Agent' = 'PBZKeyForge-build' }
        $taskObservation.SteamBuildId = [string]$taskSteamInfo.data.($taskSources.Game.SteamAppId).depots.branches.public.buildid
        if ($taskObservation.SteamBuildId -notmatch '^\d+$') { throw 'No public Steam build ID in the response.' }
    }
    catch { $taskErrors += 'Steam: ' + $_.Exception.Message }
    if ($taskErrors.Count -eq 0) {
        try {
            $taskCachePath = [IO.Path]::GetFullPath($CacheFile)
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskCachePath)) | Out-Null
            [IO.File]::WriteAllText($taskCachePath, ($taskObservation | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
        }
        catch { Write-Warning ('Unable to cache update observations: ' + $_.Exception.Message) }
    }
}

$taskReasons = @()
$taskLatest = $taskObservation.Ue4ss.Metadata
if ($taskLatest -and ($taskLatest.Version -ne $taskSources.Bundled.Version `
    -or $taskLatest.AssetId -ne $taskSources.Bundled.AssetId `
    -or $taskLatest.Updated -ne $taskSources.Bundled.Updated)) { $taskReasons += 'ue4ss_asset_changed' }
if ($taskSources.Bundled.AssetId -ne $taskSources.Game.ReviewedUe4ssAssetId `
    -or $taskSources.Bundled.Updated -ne $taskSources.Game.ReviewedUe4ssUpdated) { $taskReasons += 'ue4ss_review_pending' }
if ($taskObservation.SteamBuildId -and $taskObservation.SteamBuildId -ne $taskSources.Game.ReviewedSteamBuildId) {
    $taskReasons += 'steam_public_build_changed'
}
if (@($taskInstalled | Where-Object { $_.BuildId -and [long]$_.BuildId -gt [long]$taskSources.Game.ReviewedSteamBuildId }).Count) {
    $taskReasons += 'installed_game_build_changed'
}
if (@($taskInstalled | Where-Object { -not $_.BuildId }).Count) { $taskReasons += 'installed_game_build_unknown' }
$taskNext = @()
if ($taskReasons.Count) {
    $taskNext += 'Read Ue4ss.ReleaseNotes and the upstream release page; investigate compatibility with the changed game build.'
    $taskNext += 'Download the candidate with build/download-ue4ss.ps1 -OutputDirectory dist -UpdatePinnedRelease; run prerequisite, installation, coexistence and update tests.'
    $taskNext += 'Verify game startup, UE4SS loading and physical input when the change affects signatures/hooks; installation tests alone do not establish game compatibility.'
    $taskNext += 'Document the evidence in docs/game-version-compatibility.md; update Game.ReviewedSteamBuildId and ReviewedUe4ssAssetId/ReviewedUe4ssUpdated only after the corresponding review, then rerun this check with -Release.'
}
if ($taskErrors.Count) { $taskNext += 'Retry with -Force when network access is available. Continue local development with the pin; do not publish with an incomplete update check.' }
$taskReport = [pscustomobject][ordered]@{
    CheckedAt = $taskObservation.CheckedAt; CacheUsed = $taskUseCache
    NeedsReview = $taskReasons.Count -gt 0; CanRelease = $taskReasons.Count -eq 0 -and $taskErrors.Count -eq 0
    Reasons = @($taskReasons); Errors = @($taskErrors)
    Ue4ss = @{ Bundled = $taskSources.Bundled; Latest = $taskLatest; ReleaseNotes = $taskObservation.Ue4ss.Notes
        ReviewedAssetId = $taskSources.Game.ReviewedUe4ssAssetId; ReviewedUpdated = $taskSources.Game.ReviewedUe4ssUpdated }
    Game = @{ ReviewedSteamBuildId = $taskSources.Game.ReviewedSteamBuildId; LatestSteamBuildId = $taskObservation.SteamBuildId; Installed = @($taskInstalled); Source = $taskSources.Game.Api }
    NextActions = @($taskNext)
}
$taskJson = $taskReport | ConvertTo-Json -Depth 10
if ($OutputFile) {
    $taskOutputPath = [IO.Path]::GetFullPath($OutputFile)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskOutputPath)) | Out-Null
    [IO.File]::WriteAllText($taskOutputPath, $taskJson, (New-Object Text.UTF8Encoding($false)))
}
$taskJson
if ($Release -and -not $taskReport.CanRelease) { throw 'Runtime compatibility review is incomplete. Inspect Reasons, Errors and NextActions before publishing.' }
