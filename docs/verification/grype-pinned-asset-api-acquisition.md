# Pinned Grype asset API acquisition

Main `0b3dc2e371a2b8cf386a33575770717daef0e9e1` failed its image SBOM job
`129036142960` in `42579572740/1` while installing Grype. The original browser
release route returned HTTP 504 on all four transfer attempts. The checksum,
Grype scan and image vulnerability gate were not reached; they receive no
acceptance credit. The original failed attempt remains retained.

The official immutable asset API returned the existing 0.118.0 Linux archive
with SHA-256 `1d444c5e7360471815f7158f71935fcecc68a3c417d85c7344f770854300bba2`.
The installer now tries that public, token-free API first and retains the same
browser route as a transfer-failure fallback. Version, archive hash, HTTPS/TLS,
redirect limits, trusted final hosts, checksum-before-extraction and installed
version checks remain. A successful transfer with invalid bytes or an untrusted
host fails closed without trying another route. No environment override replaces
the release identity. No scanner findings, thresholds or exceptions change.

Native shell refusal controls check failed transfers, empty or incorrect bytes,
API metadata, final-host/HTTPS rejection and ambient version/hash overrides while
preserving an existing executable and leaving the PATH file empty. Hosted Linux
controls additionally use the original authenticated archive, real checksum,
extraction, installation and version execution for API success and browser
fallback. A valid archive from an untrusted effective host is still refused.
The test archive acquisition uses the same transfer-only API/browser fallback
and trusted-final-host/checksum checks; integrity rejection does not fall back.
Seven additional controls execute the actual workflow fixture-acquisition shell\nblock: API success, browser fallback, failed transfers, checksum/host/HTTPS\nrefusal and stalled-transfer deadlines. Each route has an external100-second\ntimeout with a five-second kill grace, bounding both routes to210seconds and\nleaving90seconds within the five-minute job. The stalled fixture records those\nactual arguments and scales its native timeout to0.2seconds for each route;\nit verifies both stalled transfers terminate and fallback is reached. Curl\nRetry-After delays cannot extend the external deadline. The installer retains\nits existing production transfer budgets.\nThese controls do not establish a complete SBOM scan or image acceptance.

Actual branch and resulting-Main image scans, required checks, original PR Gate,
independent review and complete source/run/attempt/artifact qualification remain
necessary. A local archive download and hash comparison are acquisition evidence;
the Linux binary is not executed locally. Original failures and historical unknown
causes remain separate, with no waiver, formal finding disposition, held promotion,
production operation or Release GO.
