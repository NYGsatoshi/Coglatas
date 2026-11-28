function Complete-OwnedSyntheticChild {
    param([string]$OwnershipPath, [string]$ExpectedExecutable, [string]$ExpectedHash)
    $result = @{ succeeded = $false; state = 'NOT_ESTABLISHED' }
    if (-not (Test-Path -LiteralPath $OwnershipPath)) { return $result }
    $child = $null
    try {
        $owned = Get-Content -Raw -LiteralPath $OwnershipPath | ConvertFrom-Json
        $child = Get-Process -Id ([int]$owned.processId) -ErrorAction SilentlyContinue
        if ($null -eq $child) { return @{ succeeded = $true; state = 'ABSENT' } }
        # Retain this process handle before identity reads. Kill must use the
        # same handle even if the original PID exits and is subsequently reused.
        $null = $child.Handle
        if ($child.StartTime.ToUniversalTime().Ticks -ne [long]$owned.startTimeUtcTicks -or
            [IO.Path]::GetFullPath($child.Path) -ne $ExpectedExecutable -or
            [IO.Path]::GetFullPath([string]$owned.executablePath) -ne $ExpectedExecutable -or
            [string]$owned.executableSha256 -ne $ExpectedHash) {
            return @{ succeeded = $false; state = 'IDENTITY_MISMATCH' }
        }
        $child.Kill()
        if ($child.WaitForExit(5000)) { return @{ succeeded = $true; state = 'TERMINATED' } }
        return @{ succeeded = $false; state = 'TERMINATION_NOT_ESTABLISHED' }
    } catch { return @{ succeeded = $false; state = 'NOT_ESTABLISHED' } }
    finally { if ($null -ne $child) { $child.Dispose() } }
}

function Complete-ProbeOutput {
    param([Threading.Tasks.Task]$StandardOutput, [Threading.Tasks.Task]$StandardError, [string]$EvidenceDirectory)
    if ($null -eq $StandardOutput -or $null -eq $StandardError) { return $false }
    try {
        # A surviving child can inherit the pipe. Never wait indefinitely for EOF;
        # owned-process cleanup must already have run before calling this helper.
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($StandardOutput, $StandardError), 5000)) { return $false }
        $StandardOutput.GetAwaiter().GetResult() | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'probe-output.json')
        $StandardError.GetAwaiter().GetResult() | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'probe-errors.log')
        return $true
    } catch { return $false }
}
