# PERF-05 public compatibility digest naming

Manifest schema 2 uses `environmentCompatibilityDigest` for a public SHA-256
over the unchanged canonical environment compatibility payload. Schema 1's
legacy field remains readable; existing declarations, canonical digests,
authorization references, expiries and evidence are never rewritten. Only one
identity field is allowed. New schema-2 baseline documents and their provenance
use the public name; the comparator reads either representation and rejects
dual identities. This changes the data description, not the hash algorithm,
environment classification or measurement decision.

Gitleaks 8.24.3's generic-api-key finding in #1095 came from a public hash named
as a key. Its original addition remains in that branch history and is not
removed, ignored or rewritten. The original archive and production fingerprint
recomputation prove the value's public origin. Scanner rules, entropy,
publication coverage and secret policy are unchanged by this repair.

## Small declaration disposition

Keep #1095 as the original publication-invalid, unmerged declaration. No
campaign capture has started and no sample can be enrolled retrospectively.
The original campaign ID, canonical digest, environment and expiry stay fixed.

The authenticated metadata from original artifact 11355843120 and ordinary
Main848 artifact 11389351487 differ in exactly one canonical compatibility
payload field: cpuModel changed from AMD EPYC 9V74 to AMD EPYC 7763. Toolchain,
fixture and production runtime compatibility payloads are otherwise equal.
This is host drift, not a demonstrated tool/product change. The old declaration
cannot supply a compatible baseline for the observed current small environment.
Searching or rerunning to find the old favorable host is forbidden.

After this additive schema repair reaches Main, collect ordinary production
metadata without benchmarking on the normally assigned runner, then predeclare
a new small campaign before any campaign measurement. Pin its current earlier
Main source, metadata digest, ID, canonical digest, expiry and independent
authorization reference. Document that it supersedes the unmerged #1095
declaration because publication was invalid and its environment is incompatible.
This is an initial declaration for the new observed scope, not a retry or a
replacement of a rejected measurement group. No statistical result determines
the environment choice. If assigned campaign hardware differs, preserve its
ineligible evidence rather than hunting for a matching host.

## Compatibility and governance

The nine ordered scenarios, five ordered samples, maximum three serial groups,
MAD <= 0.20, early-stop and earliest eligible stable complete selection remain
unchanged. Version-1 medium declarations retain their exact original identity
and independent approval requirement. Validation and selection use each
manifest's pinned source contracts, so an additive current reader cannot bind
historical declarations to newer comparator bytes. No approved baseline is
created by schema validation or capture success.
