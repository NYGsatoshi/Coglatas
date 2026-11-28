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
try {
    if ($IsWindows) {
        Add-Type -Path (Join-Path $PSScriptRoot 'NativeDesktop.cs')
        $desktop = [J17.WindowsProbe.NativeDesktop]::Capture()
        $desktop | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'desktop.json')
        $ready = $desktop.Available
    }
} catch { $ready = $false }
if (-not $ready) {
    @{
        scope = 'ISOLATED_SYNTHETIC_WINDOWS_UIA3_POC'; qualification = 'NOT_RUN'
        reason = 'InteractiveDesktopUnavailable'; actualWindowsUiAutomation = 'NOT_RUN'
        imeComposition = 'NOT_RUN'; screenReaderAcceptance = 'NOT_RUN'; productClientAcceptance = 'BLOCKED'
        release = 'NO_GO'; cleanupSucceeded = $true
    } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'receipt.json')
    Write-Output 'NOT_RUN: an unlocked interactive Windows input desktop was not established.'
    exit 3
}
Write-Output 'Interactive input desktop established; actual UI automation remains unexecuted.'
