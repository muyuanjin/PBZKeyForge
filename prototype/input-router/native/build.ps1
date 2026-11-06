param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
if (-not $OutputDirectory)
{
    $OutputDirectory = Join-Path $repository 'dist\input-router\runtime\Mods\PBZInputRouter\lib'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Get-Command cl.exe -ErrorAction Stop
$source = Join-Path $PSScriptRoot 'focus.cpp'
$object = Join-Path $OutputDirectory 'pbzinput-focus.obj'
$library = Join-Path $OutputDirectory 'pbzinput-focus.lib'
$binary = Join-Path $OutputDirectory 'pbzinput-focus.dll'
& $compiler.Path /nologo /LD /O2 /MT "/Fo:$object" "/Fe:$binary" $source /link user32.lib "/IMPLIB:$library"
if ($LASTEXITCODE -ne 0) { throw "Native input probe build failed: $LASTEXITCODE" }
Write-Output $binary
