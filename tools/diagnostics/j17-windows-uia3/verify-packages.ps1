param(
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$PackageRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
$verified = @{}
$signatureLog = Join-Path $EvidenceDirectory 'package-signatures.log'
foreach ($project in @('windows-host', 'windows-uia')) {
    $directory = Join-Path $PSScriptRoot $project
    $lock = Get-Content -Raw (Join-Path $directory 'packages.lock.json') | ConvertFrom-Json
    $inventory = Get-Content -Raw (Join-Path $directory 'dependency-inventory.json') | ConvertFrom-Json
    foreach ($target in $lock.dependencies.PSObject.Properties) {
        foreach ($entry in $target.Value.PSObject.Properties) {
            if ($entry.Value.type -notin @('Direct', 'Transitive')) { throw 'Probe graph must contain only its isolated packages.' }
            $id = $entry.Name.ToLowerInvariant()
            $version = ([string]$entry.Value.resolved).ToLowerInvariant()
            if ($id -notmatch '^[a-z0-9_.-]+$' -or $version -notmatch '^[a-z0-9.+-]+$') { throw 'Invalid package identity.' }
            $key = "$id/$version"
            $expected = @($inventory.packages | Where-Object { $_.package.ToLowerInvariant() -ceq $key })
            if ($expected.Count -ne 1 -or $expected[0].sha512 -cne $entry.Value.contentHash) { throw 'Resolved package differs from frozen evaluation inventory.' }
            if ($verified.ContainsKey($key)) { continue }
            $folder = Join-Path (Join-Path $PackageRoot $id) $version
            $package = Join-Path $folder "$id.$version.nupkg"
            $metadataPath = Join-Path $folder '.nupkg.metadata'
            if (-not (Test-Path -LiteralPath $package) -or -not (Test-Path -LiteralPath $metadataPath)) { throw 'Locked package identity is unavailable.' }
            # NuGet lock contentHash and a signed archive's byte hash are distinct.
            # Locked restore validates the former; retain the latter separately.
            $metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
            if ($metadata.contentHash -cne $entry.Value.contentHash -or
                $metadata.source -cne 'https://api.nuget.org/v3/index.json') { throw 'Restored NuGet content identity or source differs.' }
            & dotnet nuget verify $package --all *>> $signatureLog
            if ($LASTEXITCODE -ne 0) { throw 'Package signature verification did not pass.' }
            $license = $expected[0].nuspecLicense
            if ($license.kind -ceq 'expression') {
                if ($license.value -cne 'MIT') { throw 'Unexpected evaluation license expression.' }
            } elseif ($license.kind -ceq 'file') {
                $archive = [IO.Compression.ZipFile]::OpenRead($package)
                try {
                    $file = $archive.GetEntry([string]$license.value)
                    if ($null -eq $file) { throw 'Declared package license is absent.' }
                    $stream = $file.Open()
                    try { $licenseHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
                    finally { $stream.Dispose() }
                    if ($licenseHash -cne $expected[0].licenseFileSha256) { throw 'Evaluation license file differs.' }
                } finally { $archive.Dispose() }
            } else { throw 'Missing or unknown evaluation license declaration.' }
            $verified[$key] = @{
                package = $key; lockedContentHash = $entry.Value.contentHash
                archiveSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant()
                source = 'https://api.nuget.org/v3/index.json'; signatureVerification = 'PASS'
                licenseDeclaration = $license; productTierAdoption = 'NOT_ADOPTED'
            }
        }
    }
}
@{
    scope = 'ISOLATED_INVESTIGATION_PACKAGES'; packages = @($verified.Values | Sort-Object package)
    formalSignerPolicy = 'NOT_ADOPTED'; productDependencyAdoption = 'NOT_ADOPTED'
} | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 (Join-Path $EvidenceDirectory 'packages.json')
Write-Output 'Frozen package identities, license declarations and cryptographic package signatures verified; no product dependency adoption.'
