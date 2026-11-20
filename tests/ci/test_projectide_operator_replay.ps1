$ErrorActionPreference = 'Stop'
$replay = Join-Path $PSScriptRoot 'projectide_operator_replay.ps1'
$testRoot = New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('projectide-operator-evidence-' + [guid]::NewGuid().ToString('N')))
$cases = @{
    'operator-recordings-replayed.json' = 'Operator replay evidence already exists: operator-recordings-replayed.json.'
    'operator-replay.trx' = 'Operator replay evidence already exists: operator-replay.trx.'
    'operator-replay-receipt.json' = 'Operator replay evidence already exists: operator-replay-receipt.json.'
    'operator-replay.lock' = 'Unable to reserve the operator replay evidence directory.'
}

try {
    foreach ($case in $cases.GetEnumerator()) {
        $evidence = New-Item -ItemType Directory -Path (Join-Path $testRoot.FullName $case.Key)
        $stage = Join-Path $evidence.FullName 'stage'
        $occupiedPath = Join-Path $evidence.FullName $case.Key
        [IO.File]::WriteAllText($occupiedPath, 'existing replay evidence')
        try {
            $failure = $null
            try {
                & $replay -EvidenceDirectory $evidence.FullName -StagingDirectory $stage
            } catch {
                $failure = $_
            }

            $remaining = @(Get-ChildItem -LiteralPath $evidence.FullName -Force)
            if ($failure.Exception.Message -cne $case.Value -or $remaining.Count -ne 1 -or
                $remaining[0].FullName -ne $occupiedPath -or [IO.File]::ReadAllText($occupiedPath) -cne 'existing replay evidence') {
                throw "Replay did not preserve the occupied evidence slot $($case.Key). Evidence: $($evidence.FullName)."
            }
            Write-Output "Preserved existing $($case.Key)."
        } finally {
            if (@(Get-ChildItem -LiteralPath $evidence.FullName -Force).Count -eq 1 -and (Test-Path -LiteralPath $occupiedPath -PathType Leaf)) {
                Remove-Item -LiteralPath $occupiedPath
                Remove-Item -LiteralPath $evidence.FullName
            }
        }
    }
} finally {
    if (@(Get-ChildItem -LiteralPath $testRoot.FullName -Force).Count -eq 0) {
        Remove-Item -LiteralPath $testRoot.FullName
    }
}
