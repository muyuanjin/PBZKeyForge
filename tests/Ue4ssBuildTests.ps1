#requires -Version 5.1
param([Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$CacheDirectory = "$env:LOCALAPPDATA\PBZKeyForge\cache\ue4ss-build")
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new fixture directory.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$taskOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$taskSandbox = Join-Path $taskOutput 'build'
New-Item -ItemType Directory -Path $taskSandbox | Out-Null
Copy-Item "$taskRoot\build\download-ue4ss.ps1", "$taskRoot\build\ue4ss-sources.json" $taskSandbox
$taskConfig = Join-Path $taskSandbox 'ue4ss-sources.json'
$taskSources = Get-Content -LiteralPath $taskConfig -Raw | ConvertFrom-Json
$taskPinned = $taskSources.Bundled
$taskCacheName = "$($taskPinned.AssetId)-$(([DateTimeOffset]$taskPinned.Updated).UtcTicks).zip"
$taskCached = Join-Path $CacheDirectory $taskCacheName
if (-not (Test-Path -LiteralPath $taskCached)) { throw 'Build once to populate the pinned runtime cache before running this test.' }
$taskCache = Join-Path $taskOutput 'cache'
New-Item -ItemType Directory -Path $taskCache | Out-Null
Copy-Item -LiteralPath $taskCached -Destination (Join-Path $taskCache $taskCacheName)
$global:pbzUe4ssBuildTestState = @{ ApiCalls = 0; Downloads = 0; Latest = $null }
function Invoke-RestMethod {
    $global:pbzUe4ssBuildTestState.ApiCalls++
    if ($null -eq $global:pbzUe4ssBuildTestState.Latest) { throw 'Network access is disabled in this test.' }
    return $global:pbzUe4ssBuildTestState.Latest
}
function Invoke-WebRequest {
    $global:pbzUe4ssBuildTestState.Downloads++
    throw 'Network access is disabled in this test.'
}
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$taskBefore = [IO.File]::ReadAllText($taskConfig)
& "$taskSandbox\download-ue4ss.ps1" -OutputDirectory "$taskOutput\offline" -CacheDirectory $taskCache
Assert ($global:pbzUe4ssBuildTestState.ApiCalls -eq 0 -and $global:pbzUe4ssBuildTestState.Downloads -eq 0) 'A cached pinned build accessed the network.'
$taskMetadata = Get-Content "$taskOutput\offline\ue4ss-release.json" -Raw | ConvertFrom-Json
Assert ($taskMetadata.Version -eq $taskPinned.Version -and $taskMetadata.AssetId -eq $taskPinned.AssetId) 'Pinned metadata changed.'
Assert ([IO.File]::ReadAllText($taskConfig) -eq $taskBefore) 'An ordinary build rewrote the version configuration.'

$taskBrokenCache = Join-Path $taskOutput 'broken-cache'
New-Item -ItemType Directory -Path $taskBrokenCache | Out-Null
$taskBroken = Join-Path $taskBrokenCache $taskCacheName
[IO.File]::WriteAllText($taskBroken, 'not a ZIP')
$taskRejected = $false
try { & "$taskSandbox\download-ue4ss.ps1" -OutputDirectory "$taskOutput\broken" -CacheDirectory $taskBrokenCache }
catch { $taskRejected = $_.Exception.Message.Contains('Unable to download pinned') }
Assert ($taskRejected -and -not (Test-Path -LiteralPath $taskBroken)) 'A damaged cache was accepted or not discarded.'

# An unwritable output must not discard a valid cached archive.
function Copy-Item { throw 'Fixture output is read-only.' }
$taskRejected = $false
try { & "$taskSandbox\download-ue4ss.ps1" -OutputDirectory "$taskOutput\read-only" -CacheDirectory $taskCache }
catch { $taskRejected = $_.Exception.Message.Contains('Fixture output is read-only') }
finally { Remove-Item Function:\Copy-Item }
Assert ($taskRejected -and (Test-Path -LiteralPath (Join-Path $taskCache $taskCacheName))) 'An output write failure discarded a valid cache.'

# A different cached release must not silently replace the pinned release.
$taskOtherCache = Join-Path $taskOutput 'other-cache'
New-Item -ItemType Directory -Path $taskOtherCache | Out-Null
Copy-Item -LiteralPath $taskCached -Destination (Join-Path $taskOtherCache 'other-release.zip')
$taskRejected = $false
try { & "$taskSandbox\download-ue4ss.ps1" -OutputDirectory "$taskOutput\missing" -CacheDirectory $taskOtherCache }
catch { $taskRejected = $_.Exception.Message.Contains('Unable to download pinned') }
Assert $taskRejected 'A cache miss silently used another runtime.'
Assert ($global:pbzUe4ssBuildTestState.ApiCalls -eq 0) 'A pinned cache miss queried the latest release.'
Assert (-not (Test-Path "$taskOutput\missing\ue4ss-release.json")) 'A failed download produced release metadata.'

# Explicit refresh queries the API and updates the pin only after obtaining a valid ZIP.
$global:pbzUe4ssBuildTestState.Latest = @([pscustomobject]@{
    draft = $false; prerelease = $false; published_at = '2099-01-01T00:00:00Z'; tag_name = 'test-new'; html_url = 'https://example.invalid/test-new'
    assets = @([pscustomobject]@{ id = '42'; updated_at = '2099-01-01T00:00:00Z'; name = 'phantombladezero_ue4ss_test-new.zip'; browser_download_url = 'https://example.invalid/test-new.zip' })
}, [pscustomobject]@{ draft = $true; prerelease = $false; published_at = '2100-01-01'; assets = @() })
$taskRejected = $false
try { & "$taskSandbox\download-ue4ss.ps1" -OutputDirectory "$taskOutput\refresh-failure" -CacheDirectory $taskCache -UpdatePinnedRelease }
catch { $taskRejected = $_.Exception.Message.Contains('Unable to download pinned') }
Assert $taskRejected 'A failed refresh was accepted.'
Assert ([IO.File]::ReadAllText($taskConfig) -eq $taskBefore) 'A failed refresh changed the pin.'
Copy-Item -LiteralPath $taskCached -Destination (Join-Path $taskCache "42-$(([DateTimeOffset]'2099-01-01T00:00:00Z').UtcTicks).zip")
& "$taskSandbox\download-ue4ss.ps1" -OutputDirectory "$taskOutput\refresh" -CacheDirectory $taskCache -UpdatePinnedRelease
$taskUpdated = Get-Content -LiteralPath $taskConfig -Raw | ConvertFrom-Json
Assert ($taskUpdated.Bundled.Version -eq 'test-new' -and $taskUpdated.Bundled.AssetId -eq '42') 'Explicit refresh did not update the pin.'
Assert ($taskUpdated.Api.Count -eq $taskSources.Api.Count) 'Refresh lost runtime update sources.'
$taskCalls = $global:pbzUe4ssBuildTestState.ApiCalls + $global:pbzUe4ssBuildTestState.Downloads
& "$taskSandbox\download-ue4ss.ps1" -OutputDirectory "$taskOutput\updated-offline" -CacheDirectory $taskCache
Assert (($global:pbzUe4ssBuildTestState.ApiCalls + $global:pbzUe4ssBuildTestState.Downloads) -eq $taskCalls) 'A refreshed pin could not build offline.'
Write-Host 'Ue4ssBuildTests PASS: pinned cache, damaged cache, output write failure, cache miss, failed refresh, explicit refresh, offline rebuild; no network used.'
