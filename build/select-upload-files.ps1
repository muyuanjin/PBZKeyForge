#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files,
    [string]$WindowTitle = 'Editing PBZKeyForge - Nexus Mods - Google Chrome',
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if (-not ('PBZKeyForge.UploadPicker' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace PBZKeyForge {
    public static class UploadPicker {
        [DllImport("user32.dll", CharSet=CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr argument, string text);
        [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="SendMessageW")]
        public static extern IntPtr ReadText(IntPtr window, int message, IntPtr capacity, StringBuilder text);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr window, int message, IntPtr argument, IntPtr value);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr window);
    }
}
"@
}

$uploadPaths = @($Files | ForEach-Object {
    $item = Get-Item -LiteralPath $_
    if ($item.PSIsContainer) { throw "Upload target is a directory: $_" }
    $item.FullName
})
if (-not $uploadPaths.Count) { throw 'Select at least one file.' }
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$ownerCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $WindowTitle)
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$dialog = $null
do {
    $owner = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $ownerCondition)
    if ($owner) {
        $elements = $owner.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($element in $elements) {
            if ($element.Current.ClassName -eq '#32770' -and $element.Current.Name -match '^(Open|打开|開啟)$') {
                $dialog = $element
                break
            }
        }
    }
    if (-not $dialog) { Start-Sleep -Milliseconds 200 }
} while (-not $dialog -and [DateTime]::UtcNow -lt $deadline)
if (-not $dialog) { throw "No open file picker under '$WindowTitle'. Click the upload control first." }

# Chromium exposes some standard dialog controls as panes without UIA patterns.
# Find their native edit/button handles within this owner's dialog tree.
$edit = $null
$open = $null
$elements = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
foreach ($element in $elements) {
    if ($element.Current.AutomationId -eq '1148' -and $element.Current.ClassName -eq 'Edit') { $edit = $element }
    if ($element.Current.AutomationId -eq '1' -and $element.Current.ClassName -eq 'Button') { $open = $element }
}
if (-not $edit -or -not $open) { throw 'The file-name field or Open button is unavailable.' }
$fileNames = ($uploadPaths | ForEach-Object { '"' + $_ + '"' }) -join ' '
$editHandle = [IntPtr]$edit.Current.NativeWindowHandle
[void][PBZKeyForge.UploadPicker]::SendMessage($editHandle, 12, [IntPtr]::Zero, $fileNames)
$readback = New-Object Text.StringBuilder ($fileNames.Length + 1)
# GetWindowText does not read another process's edit control; use WM_GETTEXT.
[void][PBZKeyForge.UploadPicker]::ReadText($editHandle, 13, [IntPtr]$readback.Capacity, $readback)
if ($readback.ToString() -ne $fileNames) { throw 'The picker did not accept the complete file list.' }
$dialogHandle = [IntPtr]$dialog.Current.NativeWindowHandle
if (-not [PBZKeyForge.UploadPicker]::PostMessage([IntPtr]$open.Current.NativeWindowHandle,
        245, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Could not activate the Open button.' }
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([PBZKeyForge.UploadPicker]::IsWindow($dialogHandle) -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 200
}
if ([PBZKeyForge.UploadPicker]::IsWindow($dialogHandle)) { throw 'The file picker remains open; inspect its validation message before retrying.' }
Write-Output ("Selected {0} file(s). Verify upload completion on the website." -f $uploadPaths.Count)
