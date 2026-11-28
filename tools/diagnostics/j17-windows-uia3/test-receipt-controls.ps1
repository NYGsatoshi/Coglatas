$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'verify-receipt.ps1')
$positive = @{
    scope = 'ISOLATED_SYNTHETIC_WINDOWS_UIA3_POC'; qualification = 'PASS'; actualWindowsUiAutomation = 'PASS'
    stage = 'window-and-process-close'; errorType = 'None'; cleanupSucceeded = $true
    imeComposition = 'NOT_RUN'; screenReaderAcceptance = 'NOT_RUN'; productClientAcceptance = 'BLOCKED'
    dependencyAdoption = 'NOT_ADOPTED'; release = 'NO_GO'
    checks = @{
        interactiveDesktop = $true; ownedProcessAndAutomationIds = $true; uiaAccessibleName = $true
        actualKeyboardText = $true; tabFocusTraversal = $true; keyboardActivation = $true; closedWindowAndProcess = $true
    }
} | ConvertTo-Json -Depth 5
Assert-SyntheticWindowsReceipt ($positive | ConvertFrom-Json)
$mutations = @(
    { param($r) $r.scope = 'HEADLESS_COMPONENT_TEST' },
    { param($r) $r.actualWindowsUiAutomation = 'NOT_RUN' },
    { param($r) $r.cleanupSucceeded = $false },
    { param($r) $r.cleanupSucceeded = 'true' },
    { param($r) $r.checks.PSObject.Properties.Remove('actualKeyboardText') },
    { param($r) $r.checks.tabFocusTraversal = 'true' },
    { param($r) $r.imeComposition = 'PASS' },
    { param($r) $r.productClientAcceptance = 'PASS' },
    { param($r) $r.dependencyAdoption = 'ADOPTED' },
    { param($r) $r.qualification = 'FAIL' }
)
foreach ($mutation in $mutations) {
    $candidate = $positive | ConvertFrom-Json
    & $mutation $candidate
    $refused = $false
    try { Assert-SyntheticWindowsReceipt $candidate } catch { $refused = $true }
    if (-not $refused) { throw 'A false-positive Windows UI receipt was admitted.' }
}
Write-Output 'Receipt controls passed: one structural positive and ten false-positive refusals. This is not Windows UI execution.'
