#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$PreviousVersion,
    [string]$ApplicationPath = "$PSScriptRoot\..\dist\PBZKeyForge.exe"
)
$ErrorActionPreference = 'Stop'
$application = (Resolve-Path -LiteralPath $ApplicationPath).Path
$actual = ([Version][Diagnostics.FileVersionInfo]::GetVersionInfo($application).FileVersion).ToString(3)
if ($actual -ne $Version) { throw "Application version $actual does not match requested $Version." }
# Eight complete views and only the five illustrations used by the description.
& "$PSScriptRoot\capture-screenshots.ps1" -ApplicationPath $application -Languages en,zh-Hans `
    -Shots enhancement-main,enhancement-main-gamepad,enhancement-main-playstation,enhancement-actions
& "$PSScriptRoot\capture-screenshots.ps1" -ApplicationPath $application -Languages en `
    -Shots bindings,presets-save,presets-share,enhancement-moves,enhancement-tuning
& python "$PSScriptRoot\publish-nexus.py" prepare --version $Version --previous-version $PreviousVersion
if ($LASTEXITCODE -ne 0) { throw 'Media preparation failed.' }
