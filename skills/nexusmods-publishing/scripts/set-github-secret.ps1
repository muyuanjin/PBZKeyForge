#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')][string]$SecretName = 'NEXUSMODS_API_KEY',
    [string]$GitHubCli = 'gh.exe'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$cli = (Get-Command $GitHubCli -CommandType Application -ErrorAction Stop).Source
$form = New-Object Windows.Forms.Form
$form.Text = "GitHub repository secret - $Repository"
$form.ClientSize = New-Object Drawing.Size(560, 200)
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.StartPosition = 'CenterScreen'
$label = New-Object Windows.Forms.Label
$label.Text = "Paste $SecretName for $Repository locally. The value goes directly to GitHub CLI."
$label.SetBounds(20, 18, 520, 42)
$field = New-Object Windows.Forms.TextBox
$field.Name = 'SecretValue'
$field.UseSystemPasswordChar = $true
$field.SetBounds(20, 68, 520, 26)
$save = New-Object Windows.Forms.Button
$save.Text = 'Save secret'
$save.SetBounds(302, 118, 114, 32)
$cancel = New-Object Windows.Forms.Button
$cancel.Text = 'Cancel'
$cancel.SetBounds(426, 118, 114, 32)
$cancel.DialogResult = [Windows.Forms.DialogResult]::Cancel
$status = New-Object Windows.Forms.Label
$status.SetBounds(20, 160, 520, 26)
$form.Controls.AddRange(@($label, $field, $save, $cancel, $status))
$form.AcceptButton = $save
$form.CancelButton = $cancel
$script:secretSaved = $false
$script:secretSubmitted = $false
$save.Add_Click({
    if ([string]::IsNullOrWhiteSpace($field.Text)) {
        $status.Text = 'Paste a key before saving.'
        return
    }
    $save.Enabled = $false
    $process = $null
    try {
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $cli
        $start.Arguments = "secret set $SecretName --repo $Repository"
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        # Never put the value in argv, environment variables, clipboard reads or files.
        $script:secretSubmitted = $true
        $process.StandardInput.WriteLine($field.Text.Trim())
        $process.StandardInput.Close()
        $field.Clear()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [void]$outputTask.Result
        [void]$errorTask.Result
        if ($process.ExitCode -ne 0) {
            # Do not echo child output; it may come from a credential helper.
            $status.Text = 'Upload failed. Check gh login and repository access before retrying.'
            $save.Enabled = $true
            return
        }
        $script:secretSaved = $true
        $form.DialogResult = [Windows.Forms.DialogResult]::OK
        $form.Close()
    }
    catch {
        $status.Text = 'Could not run GitHub CLI. Check its installation and authentication.'
        $save.Enabled = $true
    }
    finally {
        $field.Clear()
        if ($process) { $process.Dispose() }
    }
})
try {
    [void]$form.ShowDialog()
    if ($script:secretSaved) {
        Write-Output "Saved repository secret $SecretName for $Repository."
    }
    elseif ($script:secretSubmitted) {
        Write-Output 'Secret submission was not confirmed. Check repository secret metadata before retrying.'
    }
    else { Write-Output 'Cancelled. No secret was submitted.' }
}
finally {
    $field.Clear()
    $form.Dispose()
}
