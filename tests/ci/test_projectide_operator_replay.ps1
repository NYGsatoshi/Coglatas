$ErrorActionPreference = 'Stop'
$replay = Join-Path $PSScriptRoot 'projectide_operator_replay.ps1'
$testRoot = New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('projectide-operator-evidence-' + [guid]::NewGuid().ToString('N')))
$cases = @{
    'operator-recordings-replayed.json' = 'Operator replay evidence already exists: operator-recordings-replayed.json.'
    'operator-replay.trx' = 'Operator replay evidence already exists: operator-replay.trx.'
    'operator-replay-receipt.json' = 'Operator replay evidence already exists: operator-replay-receipt.json.'
    'operator-replay.lock' = 'Unable to reserve the operator replay evidence directory.'
}

function Assert-ReplayRefused([string] $Name, [string] $FileName, [string] $Content, [string] $ExpectedError) {
    $evidence = New-Item -ItemType Directory -Path (Join-Path $testRoot.FullName $Name)
    $stage = Join-Path $evidence.FullName 'stage'
    $inputPath = Join-Path $evidence.FullName $FileName
    [IO.File]::WriteAllText($inputPath, $Content)
    try {
        $failure = $null
        try {
            & $replay -EvidenceDirectory $evidence.FullName -StagingDirectory $stage
        } catch {
            $failure = $_
        }

        $remaining = @(Get-ChildItem -LiteralPath $evidence.FullName -Force)
        if ($failure.Exception.Message -cne $ExpectedError -or $remaining.Count -ne 1 -or
            $remaining[0].FullName -ne $inputPath -or [IO.File]::ReadAllText($inputPath) -cne $Content) {
            throw "Replay did not refuse $Name and preserve its input. Evidence: $($evidence.FullName)."
        }
        Write-Output "Refused $Name and preserved its input."
    } finally {
        if (@(Get-ChildItem -LiteralPath $evidence.FullName -Force).Count -eq 1 -and (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
            Remove-Item -LiteralPath $inputPath
            Remove-Item -LiteralPath $evidence.FullName
        }
    }
}

try {
    foreach ($case in $cases.GetEnumerator()) {
        Assert-ReplayRefused $case.Key $case.Key 'existing replay evidence' $case.Value
    }

    $invalidRosters = @(
        @{ Name = 'failed original case'; Count = 13; LastOutcome = 'Failed'; RunOutcome = 'Completed' }
        @{ Name = 'skipped original case'; Count = 13; LastOutcome = 'NotExecuted'; RunOutcome = 'Completed' }
        @{ Name = 'truncated original roster'; Count = 12; LastOutcome = 'Passed'; RunOutcome = 'Completed' }
        @{ Name = 'incomplete original run'; Count = 13; LastOutcome = 'Passed'; RunOutcome = 'Aborted' }
        @{ Name = 'original case without an ID'; Count = 13; LastOutcome = 'Passed'; RunOutcome = 'Completed'; LastId = '' }
        @{ Name = 'malformed original test ID'; Count = 13; LastOutcome = 'Passed'; RunOutcome = 'Completed'; LastId = 'invalid' }
        @{ Name = 'duplicate original test ID'; Count = 13; LastOutcome = 'Passed'; RunOutcome = 'Completed'; LastId = '00000000-0000-0000-0000-000000000001' }
    )
    foreach ($case in $invalidRosters) {
        $results = foreach ($index in 1..$case.Count) {
            $outcome = if ($index -eq $case.Count) { $case.LastOutcome } else { 'Passed' }
            $id = '00000000-0000-0000-0000-{0:d12}' -f $index
            if ($index -eq $case.Count -and $case.ContainsKey('LastId')) {
                $id = $case.LastId
            }
            '<UnitTestResult testId="{0}" testName="WebOperatorRecordingContractTests.Case{1}" outcome="{2}" />' -f $id, $index, $outcome
        }
        $original = @"
<TestRun>
  <Results>$results</Results>
  <ResultSummary outcome="$($case.RunOutcome)" />
</TestRun>
"@
        Assert-ReplayRefused $case.Name 'projectide.trx' $original 'Operator replay requires thirteen distinct passing original contract test cases.'
    }
} finally {
    if (@(Get-ChildItem -LiteralPath $testRoot.FullName -Force).Count -eq 0) {
        Remove-Item -LiteralPath $testRoot.FullName
    }
}
