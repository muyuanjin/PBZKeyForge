#requires -Version 5.1
param(
    [string]$ApplicationPath = "$PSScriptRoot\..\dist\PBZKeyForge.exe",
    [string]$SavePath,
    [string]$OutputDir,
    [string]$Preset = "mouse_guard",
    [string]$ControllerPreset = "souls",
    [ValidateSet("en", "zh-Hans")]
    [string[]]$Languages = @("en", "zh-Hans"),
    [string[]]$Shots = @("main", "main-gamepad", "main-playstation", "bindings", "bindings-gamepad", "actions", "binding-edit", "presets-save", "presets-share", "presets-import")
)

$ErrorActionPreference = "Stop"
$captions = [ordered]@{
    "main" = "Quick setup: keyboard and mouse"
    "main-gamepad" = "Quick setup: Xbox"
    "main-playstation" = "Quick setup: PlayStation"
    "bindings" = "Key view and scheme changes"
    "bindings-gamepad" = "Xbox actions and scheme changes"
    "actions" = "Action view and binding slots"
    "binding-edit" = "Factory binding and explicit unbinding"
    "presets-save" = "Save the current scheme as a preset"
    "presets-share" = "Saved preset, device scopes and sharing"
    "presets-import" = "Preview and import a share code"
    "enhancement-main" = "Key Enhancement and independent schemes"
    "enhancement-actions" = "Additional actions and weapon moves"
    "enhancement-moves" = "Find and bind complete moves"
    "enhancement-tuning" = "Per-binding resources, damage and timing"
}
$shotsToCapture = @($Shots | ForEach-Object { $_ -split ',' })
foreach ($shot in $shotsToCapture) {
    if (-not $captions.Contains($shot)) { throw "Unknown screenshot scene: $shot. Choose from: $($captions.Keys -join ', ')." }
}
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
    foreach ($shot in $shotsToCapture) {
        $previewPreset = if ($shot.EndsWith("-gamepad") -or $shot.EndsWith("-playstation")) { $ControllerPreset } else { $Preset }
        & $capture $application $root $output $language $previewPreset $shot "$SavePath"
        if ($LASTEXITCODE -ne 0) { throw "Screenshot failed: $language / $shot" }
    }
}
Get-ChildItem -LiteralPath $output -Filter "*.png" | Select-Object Name, Length

$index = @("# PBZKeyForge $version screenshots", "", "| Feature | English | Simplified Chinese |", "| --- | --- | --- |")
foreach ($shot in $captions.Keys) {
    $hasEnglish = Test-Path -LiteralPath (Join-Path $output "${shot}-en.png")
    $hasChinese = Test-Path -LiteralPath (Join-Path $output "${shot}-zh-Hans.png")
    if (-not $hasEnglish -and -not $hasChinese) { continue }
    $english = if ($hasEnglish) { "[${shot}-en.png](${shot}-en.png)" } else { "-" }
    $chinese = if ($hasChinese) { "[${shot}-zh-Hans.png](${shot}-zh-Hans.png)" } else { "-" }
    $index += "| $($captions[$shot]) | $english | $chinese |"
}
[IO.File]::WriteAllLines((Join-Path $output "README.md"), $index, [Text.UTF8Encoding]::new($false))
