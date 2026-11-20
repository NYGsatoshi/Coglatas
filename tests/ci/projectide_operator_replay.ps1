[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $EvidenceDirectory,
    [Parameter(Mandatory)] [string] $StagingDirectory
)

$ErrorActionPreference = 'Stop'
$testProject = 'tests/Coglatas.Tests/Coglatas.Tests.csproj'
$previewProject = 'tools/ProjectIde.Preview/ProjectIde.Preview.csproj'
function Get-ReleaseAssembly([string] $Project) {
    $path = dotnet msbuild $Project -p:Configuration=Release -getProperty:TargetPath
    if ($LASTEXITCODE -ne 0 -or @($path).Count -ne 1 -or !(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The already compiled Release assembly for $Project is required."
    }
    return $path
}

function Get-OperatorInvocationIds($Fixture) {
    foreach ($case in $Fixture.cases) {
        foreach ($kind in @('original', 'diagnostic', 'budget', 'cancelled', 'retry')) {
            $frame = $case.operatorRecordings.$kind
            ($frame.manifest.text | ConvertFrom-Json -Depth 128).runId
            ($frame.response.text | ConvertFrom-Json -Depth 128).attemptId
        }
    }
}

$evidence = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
if (Test-Path -LiteralPath $StagingDirectory) {
    throw 'Operator replay requires a fresh staging directory.'
}
$replayAsset = Join-Path $evidence 'operator-recordings-replayed.json'
$replayTrx = Join-Path $evidence 'operator-replay.trx'
$receiptPath = Join-Path $evidence 'operator-replay-receipt.json'
$lockPath = Join-Path $evidence 'operator-replay.lock'
try {
    $evidenceLock = [IO.FileStream]::new($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
        [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
} catch [IO.IOException] {
    throw [IO.IOException]::new('Unable to reserve the operator replay evidence directory.', $_.Exception)
}

try {
    foreach ($outputPath in @($replayAsset, $replayTrx, $receiptPath)) {
        if (Test-Path -LiteralPath $outputPath) {
            throw "Operator replay evidence already exists: $(Split-Path -Leaf $outputPath)."
        }
    }

    $stage = New-Item -ItemType Directory -Path $StagingDirectory
    $assembly = Get-ReleaseAssembly $testProject
    $preview = Get-ReleaseAssembly $previewProject
    $stagedBin = Join-Path $stage.FullName 'bin'
    Copy-Item -LiteralPath (Split-Path -Parent $assembly) -Destination $stagedBin -Recurse
    $testAssembly = Join-Path $stagedBin (Split-Path -Leaf $assembly)
    $fixtureDirectory = New-Item -ItemType Directory -Path (Join-Path $stage.FullName 'frontend/public/assets/projectide')
    $freshFixture = Join-Path $fixtureDirectory.FullName 'operator-recordings-v1.json'
    $retainedFixture = 'frontend/public/assets/projectide/operator-recordings-v1.json'

    dotnet $preview $freshFixture --operator-recordings
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $freshFixture -PathType Leaf)) {
        throw 'The current operator exporter failed to produce a replay fixture.'
    }
    $freshHash = (Get-FileHash -LiteralPath $freshFixture -Algorithm SHA256).Hash.ToLowerInvariant()
    $retainedHash = (Get-FileHash -LiteralPath $retainedFixture -Algorithm SHA256).Hash.ToLowerInvariant()
    Copy-Item -LiteralPath $freshFixture -Destination $replayAsset

    $replay = Get-Content -LiteralPath $freshFixture -Raw | ConvertFrom-Json -Depth 128
    $retained = Get-Content -LiteralPath $retainedFixture -Raw | ConvertFrom-Json -Depth 128
    $freshIds = [Collections.Generic.HashSet[string]]::new([string[]] @(Get-OperatorInvocationIds $replay), [StringComparer]::OrdinalIgnoreCase)
    $retainedIds = [Collections.Generic.HashSet[string]]::new([string[]] @(Get-OperatorInvocationIds $retained), [StringComparer]::OrdinalIgnoreCase)
    if ($freshIds.Count -ne 110 -or $retainedIds.Count -ne 110 -or $freshIds.Overlaps($retainedIds)) {
        throw 'The 55 operator invocations must have distinct fresh run and attempt identities.'
    }
    $producer = $replay.operatorRecordingProducer
    $platform = if ($IsWindows) { 'Windows' } else { 'Linux' }
    $domainHash = (Get-FileHash -LiteralPath (Join-Path $stagedBin 'Coglatas.Domain.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    $applicationHash = (Get-FileHash -LiteralPath (Join-Path $stagedBin 'Coglatas.Application.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($producer.observedPlatform -cne $platform -or $producer.domainAssemblySha256 -cne $domainHash -or
        $producer.applicationAssemblySha256 -cne $applicationHash) {
        throw 'The fresh operator fixture has different producer assemblies or platform.'
    }

    # Keep the checked-out fixture intact. The copied test assembly discovers the fresh
    # fixture through the same relative path used by the full native test suite.
    $filter = 'FullyQualifiedName~WebOperatorRecordingContractTests'
    dotnet vstest $testAssembly "/TestCaseFilter:$filter" '/Logger:trx;LogFileName=operator-replay.trx' "/ResultsDirectory:$evidence"
    if ($LASTEXITCODE -ne 0) {
        throw 'The existing operator contract tests failed on the fresh export.'
    }

    [xml]$original = Get-Content -LiteralPath (Join-Path $evidence 'projectide.trx') -Raw
    [xml]$replayed = Get-Content -LiteralPath $replayTrx -Raw
    $originalCases = @($original.TestRun.Results.UnitTestResult | Where-Object testName -Like '*WebOperatorRecordingContractTests*')
    $replayedCases = @($replayed.TestRun.Results.UnitTestResult)
    $replayedNames = @($replayedCases.testName | Sort-Object -CaseSensitive -Unique)
    if ($originalCases.Count -ne 13 -or $replayedCases.Count -ne 13 -or $replayedNames.Count -ne 13 -or
        @($replayedCases | Where-Object outcome -ne 'Passed').Count -ne 0 -or
        @(Compare-Object $originalCases.testName $replayedNames -CaseSensitive).Count -ne 0 -or
        $replayed.TestRun.ResultSummary.outcome -ne 'Completed') {
        throw 'The fresh operator export must pass all thirteen original contract test cases.'
    }
    $head = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -cnotmatch '^[0-9a-f]{40}$') {
        throw 'The exact operator replay source commit is required.'
    }
    @{
        candidateHead = $head
        recordedFixtureSha256 = $retainedHash
        liveReplaySha256 = $freshHash
        projectIdeTrxSha256 = (Get-FileHash -LiteralPath (Join-Path $evidence 'projectide.trx') -Algorithm SHA256).Hash.ToLowerInvariant()
        operatorTrxSha256 = (Get-FileHash -LiteralPath $replayTrx -Algorithm SHA256).Hash.ToLowerInvariant()
        domainAssemblySha256 = $domainHash
        applicationAssemblySha256 = $applicationHash
        platform = $platform
        passingContractCases = $replayedNames.Count
        replayedInvocations = $freshIds.Count / 2
        executionScope = 'SYNTHETIC_FIXED_SOURCE_ONLY'
    } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8
} finally {
    $evidenceLock.Dispose()
}
