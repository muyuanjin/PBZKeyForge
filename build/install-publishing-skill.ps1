#requires -Version 5.1
param([string]$SkillsDirectory)
$ErrorActionPreference = 'Stop'
if (-not $SkillsDirectory) {
    $codexDirectory = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
    $SkillsDirectory = Join-Path $codexDirectory 'skills'
}
$source = (Resolve-Path -LiteralPath "$PSScriptRoot\..\skills\nexusmods-publishing").Path
$destination = Join-Path $SkillsDirectory 'nexusmods-publishing'
if (Test-Path -LiteralPath $destination) {
    $item = Get-Item -LiteralPath $destination -Force
    if ($item.LinkType -eq 'Junction' -and @($item.Target).Contains($source)) {
        Write-Output "Publishing skill already linked: $destination"
        return
    }
    throw "A different skill already exists at $destination. Keep it and choose the intended skill source."
}
[void](New-Item -ItemType Directory -Path $SkillsDirectory -Force)
[void](New-Item -ItemType Junction -Path $destination -Target $source)
Write-Output "Linked Nexus publishing skill: $destination"
