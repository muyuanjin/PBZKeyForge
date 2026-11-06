#requires -Version 5.1
param([string]$Output)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.IO.Compression
& "$PSScriptRoot\download-ue4ss.ps1" -OutputDirectory (Split-Path ([IO.Path]::GetFullPath($Output)) -Parent)
$taskDownload = [IO.Compression.ZipArchive]::new([IO.File]::OpenRead((Join-Path (Split-Path $Output -Parent) 'ue4ss-download.zip')), [IO.Compression.ZipArchiveMode]::Read)
if (Test-Path -LiteralPath $Output) { Remove-Item -LiteralPath $Output }
$taskStream = [IO.File]::Create($Output)
$taskZip = New-Object IO.Compression.ZipArchive($taskStream, [IO.Compression.ZipArchiveMode]::Create)
function Add-RuntimeEntry([string]$Source, [string]$Name) {
    $taskEntry = $taskZip.CreateEntry($Name, [IO.Compression.CompressionLevel]::Optimal)
    $taskWriter = $taskEntry.Open()
    try { $taskBytes = [IO.File]::ReadAllBytes($Source); $taskWriter.Write($taskBytes, 0, $taskBytes.Length) }
    finally { $taskWriter.Dispose() }
}
try {
    $taskLibrary = $taskDownload.Entries | Where-Object { $_.FullName.Replace('\', '/') -match '(^|/)ue4ss/UE4SS.dll$' } | Select-Object -First 1
    $taskPrefix = $taskLibrary.FullName.Replace('\', '/') -replace 'ue4ss/UE4SS.dll$', ''
    foreach ($taskEntry in $taskDownload.Entries) {
        $taskName = $taskEntry.FullName.Replace('\', '/')
        if (-not $taskName.StartsWith($taskPrefix) -or $taskName.EndsWith('/')) { continue }
        $taskName = $taskName.Substring($taskPrefix.Length)
        if ($taskName -match '(^|/)\.\.(/|$)' -or $taskName.StartsWith('ue4ss/Mods/', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $taskTarget = $taskZip.CreateEntry("runtime/platform/steam/$taskName", [IO.Compression.CompressionLevel]::Optimal)
        $taskInput = $taskEntry.Open(); $taskWriter = $taskTarget.Open()
        try { $taskInput.CopyTo($taskWriter) }
        finally { $taskInput.Dispose(); $taskWriter.Dispose() }
    }
    Add-RuntimeEntry (Join-Path (Split-Path $Output -Parent) 'ue4ss-release.json') 'release.json'
    foreach ($taskFile in Get-ChildItem "$taskRoot\prototype\input-router\ue4ss\Scripts" -Filter *.lua) {
        if ($taskFile.Name -ne 'router-config.lua') {
            Add-RuntimeEntry $taskFile.FullName "runtime/platform/steam/ue4ss/Mods/PBZInputRouter/Scripts/$($taskFile.Name)"
        }
    }
    Add-RuntimeEntry "$taskRoot\assets\key-enhancement\pbzinput-focus.dll" 'runtime/platform/steam/ue4ss/Mods/PBZInputRouter/lib/pbzinput-focus.dll'
}
finally { $taskDownload.Dispose(); $taskZip.Dispose(); $taskStream.Dispose() }
