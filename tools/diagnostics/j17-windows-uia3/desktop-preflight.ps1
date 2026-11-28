param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[System.IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
$source = (& git rev-parse HEAD).Trim()
$tree = (& git rev-parse 'HEAD^{tree}').Trim()
if ($source -notmatch '^[0-9a-f]{40}$' -or $source -ne $env:EXPECTED_SOURCE_SHA) { throw 'Diagnostic source identity mismatch.' }
$manifest = @{
    sourceSha = $source; treeSha = $tree; runId = $env:GITHUB_RUN_ID; attempt = $env:GITHUB_RUN_ATTEMPT
    scope = 'ISOLATED_SYNTHETIC_WINDOWS_UIA3_POC'; productClientAcceptance = 'BLOCKED'; release = 'NO_GO'
}
$manifest | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'source.json')
$ready = $false
$preflightErrorType = $null
try {
    if ($IsWindows) {
        Add-Type -Path (Join-Path $PSScriptRoot 'NativeDesktop.cs')
        $desktop = [J17.WindowsProbe.NativeDesktop]::Capture()
        $desktop | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'desktop.json')
        $ready = $desktop.Available
    }
} catch { $preflightErrorType = $_.Exception.GetType().Name }
if (-not $ready) {
    $qualification = if ($null -ne $preflightErrorType) { 'FAIL' } else { 'NOT_RUN' }
    $reason = if ($null -ne $preflightErrorType) { 'DesktopProbeError' } elseif (-not $IsWindows) { 'WindowsRequired' } else { 'InteractiveDesktopUnavailable' }
    @{
        scope = 'ISOLATED_SYNTHETIC_WINDOWS_UIA3_POC'; qualification = $qualification
        reason = $reason; errorType = $preflightErrorType; actualWindowsUiAutomation = 'NOT_RUN'
        imeComposition = 'NOT_RUN'; screenReaderAcceptance = 'NOT_RUN'; productClientAcceptance = 'BLOCKED'
        release = 'NO_GO'; cleanupSucceeded = $true
    } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'receipt.json')
    Write-Output "$qualification`: an unlocked interactive Windows input desktop was not established ($reason)."
    if ($qualification -ceq 'FAIL') { exit 1 } else { exit 3 }
}
Write-Output 'Interactive input desktop established; actual UI automation remains unexecuted.'
