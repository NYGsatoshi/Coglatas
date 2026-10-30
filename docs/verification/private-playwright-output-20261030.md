# Private default Playwright output
The Linux defaults in the mobile and thread-investigation configs now use a stable per-user cache location keyed by canonical repository path and config kind. The parent outside Playwright's cleaned output child must be owned by the current UID with mode 0700. Ancestors reject symlinks, unexpected owners, and nonsticky group/world-writable directories. Existing unsafe paths are rejected without permission repair or deletion. The stable child leaves cleanup and .last-run.json entirely to pinned Playwright 1.64.1: repeated --last-failed, explicit history precedence, and same-repository concurrent cleanup retain SDK behavior. Concurrent invocations still share output and last-finisher history; this is not per-invocation isolation.

An absolute XDG_CACHE_HOME is respected; otherwise the location is below the user's .cache. Verification uses synthetic HOME/XDG paths only. The new Linux setup requires a writable safe cache for default output, including a normal run with only an explicit history file. A trusted explicit --output avoids default-cache initialization; discovery avoids it when no default history is consumed. Empty CLI overrides retain SDK fallback semantics. Pinned worker/loader config reload computes the stable default without cache initialization, and serialized CLI output overrides remain SDK-authoritative. The bootstrap-path check describes supported SDK processes, not an authentication boundary against arbitrary same-UID code. Read-only source and working directories are supported with a writable cache.

Old shared /tmp history is deliberately not imported. This one-time security migration resets the default history; SDK cold or corrupt-history behavior remains unchanged. Non-Linux paths retain their original literals and behavior; Windows ACL security is outside this Linux fix. This is not a universal backwards-compatibility claim. No product admission, scanner, authorization, RLS, quality thresholds, retry, test selection, or reporter artifact identities change.

Reference: [XDG Base Directory Specification](https://specifications.freedesktop.org/basedir/latest/).

Local verification on the source candidate used pinned Playwright 1.64.1. Linux Node 24.22.2 completed 8 unit controls and 7 actual config/CLI/import controls without skips; a test-only cold/corrupt-history extension then passed the targeted history control for both configs. Native helper coverage was 100% lines/functions and 91.89% branches (37 branches, 3 uncovered); the existing LCOV validator passed. Windows Node 24.22.1 completed 37 current/adjacent controls with synthetic work-local HOME, USERPROFILE, TMP/TEMP, XDG, transform cache and LOCALAPPDATA. An earlier Windows invocation inherited OS temp and is retained as separate historical evidence.

A two-UID disposable Linux control reproduced c9c16772d3f5326753408e53ff8b4ed3fb2f390b failures after another UID precreated the actual shared mobile symlink and thread directory. The candidate ran each browser-free test successfully, preserved both foreign canaries/type/ownership, and denied foreign reads of private history. A fresh independent reviewer also verified foreign ownership/precreation, nonsticky writable ancestors and empty CLI precedence. This establishes a bounded local tooling issue; no Product tenant exploit, arbitrary deletion or data disclosure is claimed.

The cache compatibility change was tested explicitly: with unavailable cache, original c9 normal runs passed and this candidate rejected setup for both configs; an effective trusted --output still passed. Canonical Mobile uses the writable-rootfs Playwright image without a user/HOME/XDG/output override. A filesystem-only probe of its locally resolved image digest sha256:06a9939e57531807f8d5fd76ce44b53165ffb7d7501d87ab10e285c20b1e971f observed Node 24.21.0, UID 0, HOME=/root, no XDG override, and successful mode-0700 default initialization below the disposable container's /root/.cache. It did not execute browsers or Product journeys. The candidate still requires actual canonical Mobile workflow qualification (4 owners / 23 steps), current protected checks, official Quality Gate and latest-Main verification before merge.

Follow-up quality repair (local head 2aa57c626f9f2498349bfae4f6f2f4762ba0ca10) removes the unused import and extracts only the owned-private UID/mode predicate. The ancestry checks, short-circuit order, filesystem behavior and visible legacy literals are preserved. Fresh independent coordinator review found no source blocker. The canonical Git helper SHA-256 is 13b2ebdf546beda96213df808502e174901de1e51df35cca8f7392d16925e1b0. Current local qualification: Linux 50 PASS and Windows 37 PASS, zero failures/skips, covering private helper, actual browser-free CLI/import controls and adjacent mobile/thread controls. Main 2fad6bb4412fc5241a68a904e8dbbea77cb6f317 was normally merged afterward; both reviewed repair code blobs remained unchanged.

The original 7e9e796b49554d53b09c0b4aec01e2cac45f4820 Mobile run 41035779245/1 independently passed all 4 owners and 23 steps. Its official PR Quality Gate nevertheless failed (96.2% new coverage, Security D); analysis execution success is separate from that Gate. These remain historical exact-source results. Fresh actual canonical Mobile and current checks/Gate are pending for the follow-up source. No provider finding disposition, exception, rule, threshold, literal placement, admission policy or release authority changed. Remaining non-Linux risk and individual assertion-literal assessments stay visible. RELEASE_NO_GO remains.

The a92d3f10b5417d5c32616d30c83d28e6b7289485 follow-up still failed its official
Quality Gate (Security D, new coverage 97.2%); S3776
AaGMTXd4tMkMz78eEZbK still reported privateDirectory complexity16 above15.
The earlier owned-private leaf-predicate extraction did not resolve that finding.
A separate minimal follow-up moves only the full existing unsafe-ancestor boolean
expression verbatim into a private pure predicate, called from the same if.
Expression order, short-circuit stat access, mkdir/lstat order, leaf UID/mode,
CLI/OS/history behavior and error boundaries remain unchanged. The byte guard
reverses the extraction to every original a92 helper byte (guard SHA-256
ce66cb1fa50cd84810a161dc4ddd37679a1baf4a6f393947a23f710bdc8181d7).
Existing Windows37 controls pass on pinned portable Node24.22.2/Playwright1.64.1,
zero failures/skips, with synthetic work-local HOME/TEMP/cache. Windows does not
execute the Linux ancestor checks. Linux50, fresh canonical Mobile and the
official current-source Gate remain UNVERIFIED for this follow-up; earlier
results do not transfer. No new test, policy, literal, workflow, provider
disposition, exclusion or threshold change is included. RELEASE_NO_GO remains.
