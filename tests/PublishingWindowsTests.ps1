#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$fixtureRoot = Join-Path $repo ('dist\publishing\windows-tests-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $fixtureRoot)
$expected = @('English image.png', '中文 图片.png') | ForEach-Object {
    $path = Join-Path $fixtureRoot $_
    [IO.File]::WriteAllText($path, 'Picker fixture')
    $path
}
$form = New-Object Windows.Forms.Form
$form.Text = 'PBZ upload picker test'
$dialog = New-Object Windows.Forms.OpenFileDialog
$dialog.Multiselect = $true
$dialog.Title = '打开'
$helper = Join-Path $repo 'build\select-upload-files.ps1'
$childScript = Join-Path $fixtureRoot 'run-picker.ps1'
$content = '& ''' + $helper.Replace("'", "''") + ''' -WindowTitle ''PBZ upload picker test'' -Files @(' +
    (($expected | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ',') + ')'
[IO.File]::WriteAllText($childScript, $content, (New-Object Text.UTF8Encoding $true))
$form.Add_Shown({
    $script:pickerChild = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $childScript + '"'))
    $script:pickerResult = $dialog.ShowDialog($form)
    $form.Close()
})
try {
    [void]$form.ShowDialog()
    if (-not $pickerChild.WaitForExit(20000) -or $pickerChild.ExitCode -ne 0) { throw 'Picker helper failed.' }
    if ($pickerResult -ne [Windows.Forms.DialogResult]::OK) { throw 'Picker did not accept selection.' }
    if (@(Compare-Object $expected $dialog.FileNames).Count) { throw 'Wrong selected paths.' }
}
finally { $dialog.Dispose(); $form.Dispose() }

# A local fake gh validates argv/stdin; this test cannot change a real repository secret.
$stubPath = Join-Path $fixtureRoot 'gh-fixture.exe'
$marker = Join-Path $fixtureRoot 'stdin-accepted.txt'
$stub = @'
using System;
using System.IO;
class FixtureCli {
    static int Main(string[] args) {
        if (string.Join(" ", args) != "secret set NEXUSMODS_API_KEY --repo fixture/mod") return 10;
        if (Console.In.ReadToEnd().Trim() != "fixture-value-for-local-test") return 11;
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stdin-accepted.txt"), "accepted");
        return 0;
    }
}
'@
Add-Type -TypeDefinition $stub -OutputAssembly $stubPath -OutputType ConsoleApplication
$secretHelper = Join-Path $repo 'skills\nexusmods-publishing\scripts\set-github-secret.ps1'
$secretArguments = @(
    '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $secretHelper + '"'),
    '-Repository', 'fixture/mod', '-GitHubCli', ('"' + $stubPath + '"'))
$secretStart = New-Object Diagnostics.ProcessStartInfo
$secretStart.FileName = 'powershell.exe'
$secretStart.Arguments = $secretArguments -join ' '
$secretStart.UseShellExecute = $false
$secretStart.CreateNoWindow = $true
$secretChild = [Diagnostics.Process]::Start($secretStart)
try {
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, 'GitHub repository secret - fixture/mod')
$deadline = [DateTime]::UtcNow.AddSeconds(15)
$window = $null
do {
    $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $condition)
    if (-not $window) { Start-Sleep -Milliseconds 100 }
} while (-not $window -and [DateTime]::UtcNow -lt $deadline)
if (-not $window) { throw 'Secret entry form did not open.' }
$controls = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$edit = @($controls | Where-Object { $_.Current.ClassName -like 'WindowsForms10.EDIT.*' })[0]
# This desktop exposes WinForms controls as panes. Check the native password character.
# Send only this dummy fixture through native messages; no real secret is involved.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SecretFixtureInput {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr argument, string value);
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr window, int message, IntPtr argument, IntPtr value);
}
'@
if ([SecretFixtureInput]::SendMessage([IntPtr]$edit.Current.NativeWindowHandle,
        210, [IntPtr]::Zero, $null) -eq [IntPtr]::Zero) { throw 'Secret field is not masked.' }
[void][SecretFixtureInput]::SendMessage([IntPtr]$edit.Current.NativeWindowHandle, 12,
    [IntPtr]::Zero, 'fixture-value-for-local-test')
$save = @($controls | Where-Object { $_.Current.Name -eq 'Save secret' })[0]
[void][SecretFixtureInput]::PostMessage([IntPtr]$save.Current.NativeWindowHandle,
    245, [IntPtr]::Zero, [IntPtr]::Zero)
if (-not $secretChild.WaitForExit(15000)) { throw 'Secret helper did not finish.' }
if (-not (Test-Path -LiteralPath $marker)) { throw 'The fake CLI did not receive the expected stdin/argv.' }
}
finally {
    if (-not $secretChild.HasExited) { $secretChild.Kill(); $secretChild.WaitForExit() }
    $secretChild.Dispose()
}
Remove-Item -LiteralPath $marker
$secretChild = [Diagnostics.Process]::Start($secretStart)
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $window = $null
    do {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condition)
        if (-not $window) { Start-Sleep -Milliseconds 100 }
    } while (-not $window -and [DateTime]::UtcNow -lt $deadline)
    if (-not $window) { throw 'Cancel fixture form did not open.' }
    $controls = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $cancel = @($controls | Where-Object { $_.Current.Name -eq 'Cancel' })[0]
    [void][SecretFixtureInput]::PostMessage([IntPtr]$cancel.Current.NativeWindowHandle,
        245, [IntPtr]::Zero, [IntPtr]::Zero)
    if (-not $secretChild.WaitForExit(15000)) { throw 'Cancelled helper did not finish.' }
    if (Test-Path -LiteralPath $marker) { throw 'Cancel must not invoke gh.' }
}
finally {
    if (-not $secretChild.HasExited) { $secretChild.Kill(); $secretChild.WaitForExit() }
    $secretChild.Dispose()
}
Write-Output 'PASS: Unicode multi-file picker; masked secret stdin transfer to fake gh; cancellation submits nothing.'
