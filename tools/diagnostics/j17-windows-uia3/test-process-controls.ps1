$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'process-lifecycle.ps1')
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('j17-owned-process-controls-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$journal = Join-Path $scratch 'owned-child.json'
$child = $null
try {
    $executable = [IO.Path]::GetFullPath((Get-Process -Id $PID).Path)
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $executable).Hash.ToLowerInvariant()
    $missing = Complete-OwnedSyntheticChild $journal $executable $hash
    if ($missing.succeeded -or $missing.state -ne 'NOT_ESTABLISHED') { throw 'Missing ownership was credited as cleanup.' }
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 30')) { $start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::Start($start)
    if ($null -eq $child) { throw 'Owned lifecycle control did not start.' }
    $null = $child.Handle
    $owned = @{
        processId = $child.Id; startTimeUtcTicks = $child.StartTime.ToUniversalTime().Ticks
        executablePath = $executable; executableSha256 = 'deliberately-wrong-identity'
    }
    $owned | ConvertTo-Json | Set-Content -Encoding utf8 $journal
    $mismatch = Complete-OwnedSyntheticChild $journal $executable $hash
    if ($mismatch.succeeded -or $mismatch.state -ne 'IDENTITY_MISMATCH' -or $child.HasExited) { throw 'An identity mismatch was not fenced.' }
    $owned.executableSha256 = $hash
    $owned | ConvertTo-Json | Set-Content -Encoding utf8 $journal
    $terminated = Complete-OwnedSyntheticChild $journal $executable $hash
    if (-not $terminated.succeeded -or $terminated.state -ne 'TERMINATED' -or -not $child.WaitForExit(5000)) { throw 'Owned-process termination was not established.' }
    $absent = Complete-OwnedSyntheticChild $journal $executable $hash
    if (-not $absent.succeeded -or $absent.state -ne 'ABSENT') { throw 'Journal-bound process absence was not established.' }
    $stdout = [Threading.Tasks.TaskCompletionSource[string]]::new()
    $stderr = [Threading.Tasks.TaskCompletionSource[string]]::new()
    $stdout.SetResult('{"control":true}')
    $stderr.SetResult('')
    if (-not (Complete-ProbeOutput $stdout.Task $stderr.Task $scratch)) { throw 'Completed output did not drain.' }
    $pending = [Threading.Tasks.TaskCompletionSource[string]]::new()
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $timedOut = Complete-ProbeOutput $pending.Task $stderr.Task $scratch
    $clock.Stop()
    $pending.SetResult('')
    if ($timedOut -or $clock.ElapsedMilliseconds -lt 4500 -or $clock.ElapsedMilliseconds -gt 10000) { throw 'An unfinished output pipe was not refused within its finite bound.' }
    Write-Output 'Process controls passed: missing journal, identity fence, owned termination, recorded absence, complete drain and bounded incomplete drain. This is not Windows UI execution.'
} finally {
    if ($null -ne $child) {
        if (-not $child.HasExited) { $child.Kill(); [void]$child.WaitForExit(5000) }
        $child.Dispose()
    }
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
