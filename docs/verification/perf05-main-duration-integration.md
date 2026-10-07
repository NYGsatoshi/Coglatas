# PERF-05 Main duration integration

PR #1046 adds the reusable PostgreSQL gate to Main CI's exact-source runtime
artifact producer. Producer failure, missing identity, structural failure,
unapproved or incompatible duration evidence and statistical failure remain
blocking. The current class selector chooses the approved Small and Medium
enrollments before inspecting values; it never searches for a passing CPU SKU.
Ordinary measurement jobs pin the declared Ubuntu 24.04 OS class instead of the
moving `ubuntu-latest` alias. This preserves an authorized hard attribute and
does not select CPU hardware. Historical campaign workflow bytes are unchanged.

The first synchronized candidate `6eccecc53af37db345b8cf43ea44e2831882a4f9`
failed Publication run `37654888330/1`: the runner trust registry did not yet
include the pinned `ubuntu-24.04` label. [GitHub's runner specification](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
identifies that exact label as a standard GitHub-hosted public-repository
x64 runner with four CPUs and 16 GB memory. The registry now records that official
OS label. No self-hosted label, runner group, dynamic expression, secret scope,
write permission, or trust boundary is added. Three regression tests require
the repository registration and preserve both untrusted and privileged boundaries;
the registration assertion fails before the correction. Existing negative
self-hosted, unknown runner, write-scope and secret fixtures remain enforced.
The full Ruby suite executes on its canonical Linux CI host; a local Windows
portable-Ruby run exposed existing temporary-path normalization failures in five
unrelated allowlist/reusable fixtures and is not credited as a passing full suite.

## Superseded implementation equivalence

The previous head `ff514cdfa0df7346858a198c685a59174042432e` introduced a legacy
exact-environment selector and initial-baseline ledger shape. That validator
failed on the independently approved successor Small campaign because it did
not understand the campaign approval ledger. It also cannot validate the new
class catalog. Keeping both active selectors would create conflicting policy.

| Retired behavior | Current enforcing implementation |
| --- | --- |
| Complete nine-document inventory per profile | `db-compare.py` class selector requires all nine scenarios and one unique enrollment |
| One source, fixture, run, artifact and sample group | Class selector binds one source/fixture/run/artifact/group and one hardware cohort; `db_class_baselines.py` regenerates every document |
| All ordered samples and their digest | Pinned campaign replay, exact five-sample streams and per-stream SHA-256; proposed/promoted documents differ only in approval |
| Approved Main history, never candidate-self | Live archive/run authentication, measured/declaration ancestry and candidate-self rejection in class enrollment validator |
| Approval and immutable introduced evidence | Existing Small owner approval preserved; Medium real conditional authorization; immutable class ledger and retained evidence validation |
| Fail closed for missing/incompatible baseline | Current class-only duration selector and unchanged comparator decisions; no legacy fallback |

The old selector, legacy introduction validator and tests for their retired
path/record format are replaced by Main's tested class/campaign implementations.
The EnvironmentClass suite covers incompatible architecture/OS/resources/
runtime/compiler/schema, complete catalog, approval, mixed hardware, altered
qualification and missing enrollment. Existing comparator/campaign/public-digest
tests retain source, fixture, sample, MAD and provenance failure guards.

The 27 historical duration documents and original
`performance/evidence/perf05-initial-duration-4c4d9802.json` are retained unchanged.
They are historical evidence outside the active EnvironmentClass catalog.
Their original introduction ledger is preserved in immutable PR history at
`ff514cdfa0df7346858a198c685a59174042432e:performance/baseline-updates.json`.
Exact Gitleaks suppressions already reviewed on the old commits refer only to
public environment SHA-256 metadata, not credentials; no blanket rule/path
suppression is introduced. Security checks and thresholds remain unchanged.
The original redacted report artifact 11310593713 was freshly authenticated:
ZIP SHA-256 `f5aa923df5456189b4e2b853197f88196b7dd9b5db6a22782c2294aac5617367`.
All 88 retained exact commit/path/rule/line entries match the 88 original report
findings; immutable Git source at each line contains only the declared public
environment SHA-256 field. No credential finding is dismissed by this audit.

The rejected #1046 API cohort with 77/78 PASS plus one UNSTABLE remains rejected.
Fresh exact-head API 78/78 with zero UNSTABLE, structural 28/28, approved Small
9/9 and Medium 9/9 duration results and protected acceptance are required before
normal merge. Exact new Main must qualify independently afterward.
