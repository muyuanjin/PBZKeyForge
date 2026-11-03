#requires -Version 5.1
param(
    [string]$ApplicationPath = "$PSScriptRoot\..\dist\PBZKeyForge.exe",
    [string]$SavePath,
    [string]$OutputDir,
    [string]$Preset = "mouse_guard",
    [ValidateSet("en", "zh-Hans")]
    [string[]]$Languages = @("en", "zh-Hans")
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$application = (Resolve-Path -LiteralPath $ApplicationPath).Path
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($application).FileVersion
$version = ([Version]$version).ToString(3)
if (-not $OutputDir) { $OutputDir = Join-Path $root "assets\screenshots\$version" }
$output = [IO.Path]::GetFullPath($OutputDir)
if ($SavePath) { $SavePath = (Resolve-Path -LiteralPath $SavePath).Path }

$toolDir = Join-Path $root "dist\screenshot-tool"
New-Item -ItemType Directory -Path $toolDir -Force | Out-Null
$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$capture = Join-Path $toolDir "CaptureScreenshots.exe"
& $compiler /nologo /optimize+ "/out:$capture" "/win32manifest:$PSScriptRoot\app.manifest" "/r:$application" /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\CaptureScreenshots.cs"
if ($LASTEXITCODE -ne 0) { throw "Screenshot tool compilation failed." }

Write-Host "The editor will briefly appear in front of other windows. Keep the desktop unlocked."
foreach ($language in $Languages) {
    & $capture $application $root $output $language $Preset "$SavePath"
    if ($LASTEXITCODE -ne 0) { throw "Screenshot failed: $language" }
}
Get-ChildItem -LiteralPath $output -Filter "main-*.png" | Select-Object Name, Length
