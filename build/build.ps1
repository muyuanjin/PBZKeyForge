#requires -Version 5.1
<#
    Builds PBZKeyForge with the .NET Framework compiler shipped with Windows.
    No SDK or NuGet. The editor uses Windows' .NET Framework.
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$src = Join-Path $root "src"
$icon = Join-Path $root "assets\icon.ico"
$manifest = Join-Path $PSScriptRoot "app.manifest"

$cscCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw "csc.exe (.NET Framework 4.x) not found." }

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$sources = @(Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName })
$refs = @("/r:System.dll", "/r:System.Core.dll", "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", "/r:System.Web.Extensions.dll", "/r:System.IO.Compression.dll")
$protobuf = Join-Path $root "third_party\protobuf-net\protobuf-net.dll"
$refs += "/r:$protobuf"
$iconArg = @()
if (Test-Path $icon) { $iconArg = @("/win32icon:$icon") }
$artworkArgs = @(
    "/resource:$protobuf,PBZKeyForge.protobuf-net",
    "/resource:$root\samples\vanilla-base.sav,PBZKeyForge.vanilla-base.sav",
    "/resource:$root\assets\ui\wallpaper.jpg,PBZKeyForge.Wallpaper",
    "/resource:$root\assets\ui\game-wordmark.png,PBZKeyForge.GameWordmark",
    "/resource:$root\assets\ui\app-mark.png,PBZKeyForge.AppMark",
    "/resource:$root\assets\ui\key-enhancement-puppet.png,PBZKeyForge.KeyEnhancementMark",
    "/resource:$root\assets\ui\devices\hotspots.json,PBZKeyForge.Device.hotspots"
)
foreach ($deviceAsset in Get-ChildItem "$root\assets\ui\devices" -Filter *.png) {
    $artworkArgs += "/resource:$($deviceAsset.FullName),PBZKeyForge.Device.$($deviceAsset.BaseName)"
}
foreach ($actionAsset in Get-ChildItem "$root\assets\ui\actions" -Filter *.png -ErrorAction SilentlyContinue) {
    $artworkArgs += "/resource:$($actionAsset.FullName),PBZKeyForge.Action.$($actionAsset.BaseName)"
}
foreach ($extension in @("pak", "utoc", "ucas")) {
    $artworkArgs += "/resource:$root\assets\perfect-deflect-no-rapid-click-penalty\PBZKeyForge_PerfectDeflectNoRapidClickPenalty_P.$extension,PBZKeyForge.PerfectDeflectNoRapidClickPenalty.$extension"
}
$artworkArgs += "/resource:$root\assets\perfect-deflect-no-rapid-click-penalty\layout.json,PBZKeyForge.PerfectDeflectNoRapidClickPenalty.json"
& "$PSScriptRoot\make-key-enhancement-runtime.ps1" -Output "$OutputDir\key-enhancement-runtime.zip"
$artworkArgs += "/resource:$OutputDir\key-enhancement-runtime.zip,PBZKeyForge.KeyEnhancement.runtime.zip"
$artworkArgs += "/resource:$PSScriptRoot\ue4ss-sources.json,PBZKeyForge.UE4SS.sources.json"
$artworkArgs += "/resource:$root\prototype\input-router\actions.json,PBZKeyForge.KeyEnhancement.actions.json"
$artworkArgs += "/resource:$root\assets\key-enhancement\action-localization.json,PBZKeyForge.KeyEnhancement.action-localization.json"
$artworkArgs += "/resource:$root\assets\key-enhancement\action-details.json,PBZKeyForge.KeyEnhancement.action-details.json"
$artworkArgs += "/resource:$root\assets\key-enhancement\native-input-plan.json,PBZKeyForge.KeyEnhancement.native-input-plan.json"
$artworkArgs += "/resource:$root\assets\key-enhancement\legacy-runtime.zip,PBZKeyForge.KeyEnhancement.legacy.zip"

$sourceRevision = $null
$sourceRef = $null
$sourceModified = $true
if (Get-Command git -ErrorAction SilentlyContinue) {
    try {
        $revision = & git -C $root rev-parse --verify HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            $changes = @(& git -C $root status --porcelain --untracked-files=normal 2>$null)
            if ($LASTEXITCODE -eq 0) {
                $sourceRevision = $revision
                $sourceRef = $revision
                $sourceModified = [bool]$changes.Count
            }
        }
    } catch {
        # Git metadata is optional when building a source archive.
    }
}
$buildInfo = Join-Path $OutputDir "build-info.json"
@{ revision = $sourceRevision; sourceRef = $sourceRef; modified = $sourceModified } |
    ConvertTo-Json | Set-Content -LiteralPath $buildInfo -Encoding UTF8
$artworkArgs += "/resource:$buildInfo,PBZKeyForge.BuildInfo.json"

Write-Host "Compiling GUI with $csc"
$guiArgs = @("/nologo", "/optimize+", "/target:winexe", "/out:$OutputDir\PBZKeyForge.exe", "/win32manifest:$manifest") + $iconArg + $artworkArgs + $refs + $sources
& $csc $guiArgs
if ($LASTEXITCODE -ne 0) { throw "GUI build failed" }

Write-Host "Compiling CLI"
$cliArgs = @("/nologo", "/optimize+", "/target:exe", "/define:CLI", "/out:$OutputDir\PBZKeyForge-cli.exe", "/win32manifest:$manifest") + $artworkArgs + $refs + $sources
& $csc $cliArgs
if ($LASTEXITCODE -ne 0) { throw "CLI build failed" }

Copy-Item "$root\THIRD-PARTY-NOTICES.md" $OutputDir
New-Item -ItemType Directory -Path "$OutputDir\docs" -Force | Out-Null
Copy-Item "$root\docs\cli.md", "$root\docs\key-enhancement.md" "$OutputDir\docs"

Get-ChildItem "$OutputDir\PBZKeyForge*.exe" | Select-Object Name, Length | Format-Table -AutoSize
