# Run with Windows PowerShell 5.1, which includes the .NET Framework WinRT projection.
# powershell.exe -NoProfile -STA -File tools\Test-WindowsHaptics.ps1 [-CheckOnly]
[CmdletBinding()]
param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Use powershell.exe (Windows PowerShell 5.1), not pwsh.exe.'
}
$apiPresent = [Windows.Foundation.Metadata.ApiInformation, Windows.Foundation, ContentType=WindowsRuntime]::IsTypePresent('Windows.Devices.Haptics.InputHapticsManager')
$result = [ordered]@{ ApiPresent = $apiPresent; IsSupported = $false; DevicePresent = $false }
if ($apiPresent) {
    $script:managerType = [Windows.Devices.Haptics.InputHapticsManager, Windows.Devices.Haptics, ContentType=WindowsRuntime]
    $result.IsSupported = $script:managerType::IsSupported()
    $result.DevicePresent = $script:managerType::IsHapticDevicePresent()
}
$result | ConvertTo-Json
if ($CheckOnly -or -not $result.IsSupported -or -not $result.DevicePresent) { return }
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with -STA.' }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Devices.Haptics.KnownSimpleHapticsControllerWaveforms, Windows.Devices.Haptics, ContentType=WindowsRuntime] | Out-Null
$script:probeManager = $null
$script:probeLog = Join-Path $PSScriptRoot 'windows-haptics-results.jsonl'
$script:probeForm = New-Object Windows.Forms.Form
$script:probeForm.Text = 'Windows haptics feasibility probe'
$script:probeForm.ClientSize = New-Object Drawing.Size(680, 430)
$script:probeForm.StartPosition = 'CenterScreen'
$panel = New-Object Windows.Forms.FlowLayoutPanel
$panel.Dock = 'Fill'
$panel.FlowDirection = 'TopDown'
$panel.WrapContents = $false
$script:probeForm.Controls.Add($panel)
$label = New-Object Windows.Forms.Label
$label.AutoSize = $true
$label.MaximumSize = New-Object Drawing.Size(650, 0)
$label.Text = 'Click using the MX Master 4. For the delayed test, switch to another app within 5 seconds. Each test sends one short effect. A true API result does not prove you felt a vibration.'
$panel.Controls.Add($label)
$intensityLabel = New-Object Windows.Forms.Label
$intensityLabel.Text = 'Intensity (0.0-1.0)'
$intensityLabel.AutoSize = $true
$panel.Controls.Add($intensityLabel)
$script:probeIntensity = New-Object Windows.Forms.NumericUpDown
$script:probeIntensity.DecimalPlaces = 1
$script:probeIntensity.Increment = [decimal]0.1
$script:probeIntensity.Minimum = 0
$script:probeIntensity.Maximum = 1
$script:probeIntensity.Value = [decimal]0.3
$panel.Controls.Add($script:probeIntensity)
$script:probeOutput = New-Object Windows.Forms.TextBox
$script:probeOutput.Multiline = $true
$script:probeOutput.ReadOnly = $true
$script:probeOutput.ScrollBars = 'Vertical'
$script:probeOutput.Size = New-Object Drawing.Size(650, 160)

function Write-ProbeResult([string]$mode, $sent, [string]$errorText) {
    $record = [ordered]@{
        Timestamp = [DateTime]::UtcNow.ToString('o')
        Mode = $mode
        ProbeHasFocus = $script:probeForm.ContainsFocus
        Intensity = [double]$script:probeIntensity.Value
        ApiReturned = $sent
        Error = $errorText
    }
    $json = $record | ConvertTo-Json -Compress
    Add-Content -LiteralPath $script:probeLog -Value $json -Encoding UTF8
    $script:probeOutput.AppendText($json + [Environment]::NewLine)
}
function Send-Probe([string]$mode) {
    try {
        # Both immediate and delayed calls remain on this form's UI thread.
        $script:probeManager = $script:managerType::GetForCurrentThread()
        $waveform = [Windows.Devices.Haptics.KnownSimpleHapticsControllerWaveforms]::Click
        $sent = $script:probeManager.TrySendHapticWaveformForDuration(
            [uint16]$waveform, [uint16]0, [double]$script:probeIntensity.Value,
            [TimeSpan]::FromMilliseconds(40))
        Write-ProbeResult $mode $sent ''
    } catch { Write-ProbeResult $mode $null $_.Exception.Message }
}
$immediate = New-Object Windows.Forms.Button
$immediate.Text = 'Play now (mouse click)'
$immediate.AutoSize = $true
$immediate.Add_Click({ Send-Probe 'immediate' })
$panel.Controls.Add($immediate)
$script:probeTimer = New-Object Windows.Forms.Timer
$script:probeTimer.Interval = 5000
$script:probeTimer.Add_Tick({ $script:probeTimer.Stop(); Send-Probe 'delayed-5s' })
$delayed = New-Object Windows.Forms.Button
$delayed.Text = 'Play in 5 seconds - switch apps'
$delayed.AutoSize = $true
$delayed.Add_Click({ $script:probeTimer.Stop(); $script:probeTimer.Start() })
$panel.Controls.Add($delayed)
$stop = New-Object Windows.Forms.Button
$stop.Text = 'Stop / cancel'
$stop.AutoSize = $true
$stop.Add_Click({
    $script:probeTimer.Stop()
    if ($script:probeManager) {
        try { Write-ProbeResult 'stop' ($script:probeManager.TryStopFeedback()) '' }
        catch { Write-ProbeResult 'stop' $null $_.Exception.Message }
    }
})
$panel.Controls.Add($stop)
$panel.Controls.Add($script:probeOutput)
$script:probeForm.Add_FormClosed({
    $script:probeTimer.Stop()
    $script:probeTimer.Dispose()
    if ($script:probeManager) { try { $null = $script:probeManager.TryStopFeedback() } catch {} }
})
try { [Windows.Forms.Application]::Run($script:probeForm) }
finally { $script:probeForm.Dispose() }
