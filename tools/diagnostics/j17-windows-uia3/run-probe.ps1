param(
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$PrivateDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'process-lifecycle.ps1')
[System.IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($PrivateDirectory) | Out-Null
$hostExecutable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'windows-host/bin/Release/net11.0/WindowsHost.exe'))
$harness = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'windows-uia/bin/Release/net11.0-windows7.0/WindowsUiProbe.exe'))
$receiptPath = Join-Path $EvidenceDirectory 'receipt.json'
$ownershipPath = Join-Path $PrivateDirectory 'owned-child.json'
$expectedHostHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $hostExecutable).Hash.ToLowerInvariant()
$harnessProcess = $null
$stdout = $null
$stderr = $null
$probeExit = 1
$wrapperCleanup = @{ succeeded = $false; state = 'NOT_ESTABLISHED' }
$harnessCleanup = $false
$outputDrained = $false
try {
    $start = [Diagnostics.ProcessStartInfo]::new($harness)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($hostExecutable, $receiptPath, $ownershipPath)) { $start.ArgumentList.Add($argument) }
    $harnessProcess = [Diagnostics.Process]::Start($start)
    if ($null -eq $harnessProcess) { throw 'Synthetic probe process did not start.' }
    $stdout = $harnessProcess.StandardOutput.ReadToEndAsync()
    $stderr = $harnessProcess.StandardError.ReadToEndAsync()
    if (-not $harnessProcess.WaitForExit(90000)) {
        $harnessProcess.Kill()
        [void]$harnessProcess.WaitForExit(5000)
        throw 'Synthetic UI probe exceeded its bounded runtime.'
    }
    $probeExit = $harnessProcess.ExitCode
} catch {
    $probeExit = 1
    if (-not (Test-Path -LiteralPath $receiptPath)) {
        @{
            scope = 'ISOLATED_SYNTHETIC_WINDOWS_UIA3_POC'; qualification = 'FAIL'; stage = 'HarnessExecution'
            actualWindowsUiAutomation = 'NOT_ESTABLISHED'; imeComposition = 'NOT_RUN'
            screenReaderAcceptance = 'NOT_RUN'; productClientAcceptance = 'BLOCKED'; release = 'NO_GO'
        } | ConvertTo-Json | Set-Content -Encoding utf8 $receiptPath
    }
} finally {
    if ($null -ne $harnessProcess) {
        try {
            if (-not $harnessProcess.HasExited) { $harnessProcess.Kill() }
            $harnessCleanup = $harnessProcess.WaitForExit(5000)
        } catch { $harnessCleanup = $false }
    }
    # A timeout/crash may prevent managed finally from running. No ownership
    # journal means cleanup is unproven, including the launch-to-journal gap.
    $wrapperCleanup = Complete-OwnedSyntheticChild $ownershipPath $hostExecutable $expectedHostHash
    # Cleanup precedes bounded pipe draining, since a surviving child can keep
    # an inherited pipe open after the harness itself has exited.
    $outputDrained = Complete-ProbeOutput $stdout $stderr $EvidenceDirectory
    if ($null -ne $harnessProcess) {
        $harnessProcess.StandardOutput.Dispose()
        $harnessProcess.StandardError.Dispose()
        $harnessProcess.Dispose()
    }
    @{
        ownedSyntheticChildCleanupSucceeded = $wrapperCleanup.succeeded; childCleanupState = $wrapperCleanup.state
        ownedHarnessCleanupSucceeded = $harnessCleanup; outputDrainSucceeded = $outputDrained
    } |
        ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'cleanup.json')
}
if ($probeExit -ne 0 -or -not $wrapperCleanup.succeeded -or -not $harnessCleanup -or -not $outputDrained) {
    throw 'Synthetic Windows probe failed or did not run; inspect its finite receipt.'
}
$receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
. (Join-Path $PSScriptRoot 'verify-receipt.ps1')
Assert-SyntheticWindowsReceipt $receipt
$binaryHashes = @{}
foreach ($project in @('windows-host', 'windows-uia')) {
    $directory = Join-Path $PSScriptRoot "$project/bin/Release"
    Get-ChildItem -LiteralPath $directory -Recurse -File |
        Where-Object { $_.Extension -in @('.exe', '.dll', '.json') } |
        ForEach-Object { $binaryHashes[[IO.Path]::GetRelativePath($PSScriptRoot, $_.FullName)] = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
}
$binaryHashes | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'binary-hashes.json')
Write-Output 'PASS: bounded synthetic actual Windows UIA3 observations. Product, IME, screen-reader and Release acceptance remain unestablished.'
