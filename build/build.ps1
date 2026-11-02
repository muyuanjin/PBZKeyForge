#requires -Version 5.1
<#
    Builds PBZKeyForge with the .NET Framework compiler shipped with Windows.
    No SDK, no NuGet, no runtime install for the end user.
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
$refs = @("/r:System.dll", "/r:System.Core.dll", "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll")
$iconArg = @()
if (Test-Path $icon) { $iconArg = @("/win32icon:$icon") }
$artworkArgs = @(
    "/resource:$root\assets\ui\wallpaper.jpg,PBZKeyForge.Wallpaper",
    "/resource:$root\assets\ui\game-wordmark.png,PBZKeyForge.GameWordmark",
    "/resource:$root\assets\ui\app-mark.png,PBZKeyForge.AppMark"
)

Write-Host "Compiling GUI with $csc"
$guiArgs = @("/nologo", "/optimize+", "/target:winexe", "/out:$OutputDir\PBZKeyForge.exe", "/win32manifest:$manifest") + $iconArg + $artworkArgs + $refs + $sources
& $csc $guiArgs
if ($LASTEXITCODE -ne 0) { throw "GUI build failed" }

Write-Host "Compiling CLI"
$cliArgs = @("/nologo", "/optimize+", "/target:exe", "/out:$OutputDir\PBZKeyForge-cli.exe", "/win32manifest:$manifest") + $artworkArgs + $refs + $sources
& $csc $cliArgs
if ($LASTEXITCODE -ne 0) { throw "CLI build failed" }

Get-ChildItem "$OutputDir\PBZKeyForge*.exe" | Select-Object Name, Length | Format-Table -AutoSize
