param(
    [string]$Output = 'dist\input-router\delivery',
    [string]$LoaderRoot,
    [string]$FocusDll = 'dist\input-router\runtime\Mods\PBZInputRouter\lib\pbzinput-focus.dll',
    [string]$ResourceRoot = 'dist\input-router\stance-dash-final'
)
$ErrorActionPreference = 'Stop'
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not [IO.Path]::IsPathRooted($Output)) { $Output = Join-Path $taskRepo $Output }
if (-not [IO.Path]::IsPathRooted($FocusDll)) { $FocusDll = Join-Path $taskRepo $FocusDll }
if (-not [IO.Path]::IsPathRooted($ResourceRoot)) { $ResourceRoot = Join-Path $taskRepo $ResourceRoot }
if (-not (Test-Path -LiteralPath $FocusDll -PathType Leaf)) { throw "Build native focus DLL first: $FocusDll" }
$taskRuntime = Join-Path $Output 'runtime\platform\steam'
$taskMods = Join-Path $taskRuntime 'ue4ss\Mods'
$taskMod = Join-Path $taskMods 'PBZInputRouter'
New-Item -ItemType Directory -Path (Join-Path $taskMod 'Scripts'),(Join-Path $taskMod 'lib') -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'ue4ss\Scripts\*.lua') -Destination (Join-Path $taskMod 'Scripts') -Force
Copy-Item -LiteralPath $FocusDll -Destination (Join-Path $taskMod 'lib\pbzinput-focus.dll') -Force
[IO.File]::WriteAllText((Join-Path $taskMod 'enabled.txt'), '')
if ($LoaderRoot)
{
    $taskLoader = (Resolve-Path -LiteralPath $LoaderRoot).Path
    foreach ($taskFile in @('UE4SS.dll','UE4SS-settings.ini','MemberVariableLayout.ini','LICENSE'))
    {
        Copy-Item -LiteralPath (Join-Path $taskLoader ('ue4ss\' + $taskFile)) -Destination (Join-Path $taskRuntime ('ue4ss\' + $taskFile)) -Force
    }
    Copy-Item -LiteralPath (Join-Path $taskLoader 'ue4ss\UE4SS_Signatures') -Destination (Join-Path $taskRuntime 'ue4ss\UE4SS_Signatures') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $taskLoader 'dwmapi.dll') -Destination (Join-Path $taskRuntime 'dwmapi.dll') -Force
    [IO.File]::WriteAllText((Join-Path $taskMods 'mods.txt'), "PBZInputRouter : 1`r`n")
    [IO.File]::WriteAllText((Join-Path $taskMods 'mods.json'), '[{"mod_name":"PBZInputRouter","mod_enabled":true}]')
}
$taskResource = Join-Path $Output 'resources\ProjectPBZ\Content\Paks\~mods\PBZKeyForge_StanceDashOnJump'
New-Item -ItemType Directory -Path $taskResource -Force | Out-Null
foreach ($taskExt in @('pak','utoc','ucas'))
{
    Copy-Item -LiteralPath (Join-Path $ResourceRoot ('PBZKeyForge_StanceDashOnJump_P.' + $taskExt)) -Destination $taskResource -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'special-dash.md'),(Join-Path $PSScriptRoot 'runtime-evidence.json'),(Join-Path $PSScriptRoot 'actions.json') -Destination $Output -Force
$taskZip = Join-Path (Split-Path $Output -Parent) 'PBZInputRouter-prototype.zip'
Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $taskZip -Force
Write-Output $taskZip
