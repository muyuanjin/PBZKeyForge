#requires -Version 5.1
param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new fixture directory.' }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
$taskSandbox = Join-Path $taskOutput 'build'
[IO.Directory]::CreateDirectory($taskSandbox) | Out-Null
Copy-Item "$taskRoot\build\check-runtime-updates.ps1", "$taskRoot\build\ue4ss-release.ps1", "$taskRoot\build\ue4ss-sources.json" $taskSandbox
$taskConfig = Join-Path $taskSandbox 'ue4ss-sources.json'
$taskSources = Get-Content -LiteralPath $taskConfig -Raw -Encoding UTF8 | ConvertFrom-Json
$taskSources.Api = @('https://github.example.invalid/releases')
$taskSources.Game.Api = 'https://steam.example.invalid/info'
$taskSources | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskConfig -Encoding UTF8
$taskOriginal = [IO.File]::ReadAllText($taskConfig)
$taskGame = Join-Path $taskOutput 'library\steamapps\common\PBZ'
[IO.Directory]::CreateDirectory((Join-Path $taskGame 'platform\steam')) | Out-Null
[IO.File]::WriteAllText((Join-Path $taskGame 'platform\steam\PBZ.exe'), 'fixture, never launched')
$taskManifest = Join-Path $taskOutput "library\steamapps\appmanifest_$($taskSources.Game.SteamAppId).acf"
function Set-GameBuild([string]$Build) {
    [IO.File]::WriteAllText($taskManifest, ('"AppState" { "installdir" "PBZ" "buildid" "' + $Build + '" }'))
}
Set-GameBuild $taskSources.Game.ReviewedSteamBuildId
$taskAsset = [pscustomobject]@{
    id = $taskSources.Bundled.AssetId; updated_at = $taskSources.Bundled.Updated
    name = $taskSources.Bundled.Asset; browser_download_url = $taskSources.Bundled.Download
}
$taskStable = [pscustomobject]@{
    draft = $false; prerelease = $false; published_at = $taskSources.Bundled.Published
    tag_name = $taskSources.Bundled.Version; html_url = $taskSources.Bundled.Source
    body = 'PBZ compatibility fixes.'; assets = @($taskAsset)
}
$global:pbzRuntimeUpdateTest = @{
    Calls = 0; Offline = $false; BuildId = $taskSources.Game.ReviewedSteamBuildId
    Releases = @([pscustomobject]@{ draft = $false; prerelease = $true; published_at = '2100-01-01T00:00:00Z'; assets = @($taskAsset) }, $taskStable)
}
function Invoke-RestMethod {
    param([string]$Uri, [object]$Headers, [int]$TimeoutSec)
    $global:pbzRuntimeUpdateTest.Calls++
    if ($global:pbzRuntimeUpdateTest.Offline) { throw 'Fixture network unavailable.' }
    if ($Uri -like 'https://steam.*') {
        $taskData = @{}; $taskData[$taskSources.Game.SteamAppId] = @{ depots = @{ branches = @{ public = @{ buildid = $global:pbzRuntimeUpdateTest.BuildId } } } }
        return [pscustomobject]@{ data = $taskData }
    }
    return $global:pbzRuntimeUpdateTest.Releases
}
$script:taskAssertions = 0
function Assert([bool]$Condition, [string]$Message) {
    $script:taskAssertions++
    if (-not $Condition) { throw $Message }
}
$taskCache = Join-Path $taskOutput 'observations.json'
function Check-Updates([switch]$Force) {
    & "$taskSandbox\check-runtime-updates.ps1" -GameDirectory $taskGame -CacheFile $taskCache -Force:$Force | ConvertFrom-Json
}
$taskReport = Check-Updates
Assert ($taskReport.CanRelease -and -not $taskReport.NeedsReview -and -not $taskReport.CacheUsed) 'Unchanged releases were not accepted.'
Assert ($taskReport.Ue4ss.Latest.Version -eq $taskStable.tag_name) 'A prerelease replaced the stable PBZ release.'
Assert ($global:pbzRuntimeUpdateTest.Calls -eq 2) 'Initial observation did not query both sources.'
$taskReport = Check-Updates
Assert ($taskReport.CacheUsed -and $global:pbzRuntimeUpdateTest.Calls -eq 2) 'A current observation queried the network again.'
$taskReport = & "$taskSandbox\check-runtime-updates.ps1" -GameDirectory ($taskGame + '\') -CacheFile $taskCache | ConvertFrom-Json
Assert ($taskReport.CacheUsed -and $taskReport.CanRelease) 'A trailing directory separator changed the installation identity.'

Set-GameBuild '1'
$taskReport = Check-Updates
Assert ($taskReport.CanRelease -and -not $taskReport.CacheUsed) 'An older local installation blocked review of the current public game.'
Set-GameBuild $taskSources.Game.ReviewedSteamBuildId
$taskReport = Check-Updates

$taskAsset.updated_at = '2099-01-01T00:00:00Z'
$taskReport = Check-Updates -Force
Assert ($taskReport.NeedsReview -and $taskReport.Reasons -contains 'ue4ss_asset_changed') 'A reuploaded asset under the same version was missed.'
Assert ($taskReport.Ue4ss.ReleaseNotes -eq $taskStable.body -and $taskReport.NextActions.Count -gt 0) 'The candidate lacks release notes or adaptation steps.'
Assert ([IO.File]::ReadAllText($taskConfig) -eq $taskOriginal) 'Observation changed the pinned runtime.'
$taskPromoted = $taskOriginal | ConvertFrom-Json
$taskPromoted.Bundled.Updated = $taskAsset.updated_at
$taskPromoted | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskConfig -Encoding UTF8
$taskReport = Check-Updates
Assert ($taskReport.Reasons -contains 'ue4ss_review_pending' -and -not $taskReport.CanRelease) 'Changing the pin alone bypassed runtime compatibility review.'
$taskPromoted.Game.ReviewedUe4ssUpdated = $taskAsset.updated_at
$taskPromoted | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskConfig -Encoding UTF8
$taskReport = Check-Updates
Assert ($taskReport.CanRelease) 'Recording the reviewed runtime did not resolve the candidate.'
[IO.File]::WriteAllText($taskConfig, $taskOriginal)
$taskAsset.updated_at = $taskSources.Bundled.Updated
$taskReport = Check-Updates -Force

Set-GameBuild '99999999'
$taskCalls = $global:pbzRuntimeUpdateTest.Calls
$taskReport = Check-Updates
Assert (-not $taskReport.CacheUsed -and $global:pbzRuntimeUpdateTest.Calls -eq $taskCalls + 2) 'A local game update reused the old observation.'
Assert ($taskReport.Reasons -contains 'installed_game_build_changed' -and -not $taskReport.CanRelease) 'A changed installed game was accepted as reviewed.'
Set-GameBuild $taskSources.Game.ReviewedSteamBuildId
$global:pbzRuntimeUpdateTest.BuildId = '88888888'
$taskReport = Check-Updates -Force
Assert ($taskReport.Reasons -contains 'steam_public_build_changed') 'A public game update was missed before local installation.'
$taskRejected = $false
try { & "$taskSandbox\check-runtime-updates.ps1" -GameDirectory $taskGame -CacheFile $taskCache -Release | Out-Null }
catch { $taskRejected = $_.Exception.Message.Contains('compatibility review is incomplete') }
Assert $taskRejected 'Release accepted an unreviewed public game build.'

$global:pbzRuntimeUpdateTest.BuildId = $taskSources.Game.ReviewedSteamBuildId
$taskReport = Check-Updates -Force
$taskCached = Get-Content -LiteralPath $taskCache -Raw -Encoding UTF8 | ConvertFrom-Json
$taskCached.CheckedAt = [DateTimeOffset]::UtcNow.AddHours(-25).ToString('o')
$taskCached | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskCache -Encoding UTF8
$taskCalls = $global:pbzRuntimeUpdateTest.Calls
$taskReport = Check-Updates
Assert (-not $taskReport.CacheUsed -and $global:pbzRuntimeUpdateTest.Calls -eq $taskCalls + 2) 'An expired observation was reused.'

$global:pbzRuntimeUpdateTest.Offline = $true
$taskReport = Check-Updates -Force
Assert (-not $taskReport.CanRelease -and $taskReport.Errors.Count -eq 2) 'An offline check reported successful current observations.'
$taskRejected = $false
try { & "$taskSandbox\check-runtime-updates.ps1" -GameDirectory $taskGame -CacheFile $taskCache -Release | Out-Null }
catch { $taskRejected = $_.Exception.Message.Contains('compatibility review is incomplete') }
Assert $taskRejected 'Release reused a cached success while offline.'
$taskReport = Check-Updates
Assert ($taskReport.CacheUsed -and $taskReport.CanRelease) 'A transient network failure destroyed the last successful development cache.'
$global:pbzRuntimeUpdateTest.Offline = $false

[IO.File]::WriteAllText($taskCache, 'invalid JSON')
$taskReport = Check-Updates
Assert (-not $taskReport.CacheUsed -and $taskReport.CanRelease) 'A damaged cache did not recover.'
Remove-Item -LiteralPath $taskManifest
$taskReport = Check-Updates
Assert ($taskReport.Reasons -contains 'installed_game_build_unknown') 'An unidentifiable copied game was treated as a reviewed Steam build.'
Set-GameBuild $taskSources.Game.ReviewedSteamBuildId
$taskReport = Check-Updates -Force

# Recompute decisions from the current pin even while reusing source observations.
$taskSources.Bundled.AssetId = 'unreviewed-pin'
$taskSources | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskConfig -Encoding UTF8
$taskCalls = $global:pbzRuntimeUpdateTest.Calls
$taskReport = Check-Updates
Assert ($taskReport.CacheUsed -and $taskReport.NeedsReview -and $global:pbzRuntimeUpdateTest.Calls -eq $taskCalls) 'A changed pin reused a cached compatibility decision.'
[IO.File]::WriteAllText($taskConfig, $taskOriginal)
$taskCalls = $global:pbzRuntimeUpdateTest.Calls
& "$taskSandbox\check-runtime-updates.ps1" -GameDirectory $taskGame -CacheFile $taskCache -Release | Out-Null
Assert ($global:pbzRuntimeUpdateTest.Calls -eq $taskCalls + 2) 'Release did not obtain fresh observations.'
Write-Host "RuntimeUpdateTests PASS: $taskAssertions assertions; no network or game process used."
