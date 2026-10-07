# EnvironmentClass and hardware evidence

Active performance comparison uses `performance-environment-class-v1`.
Exact Git SHA, artifact SHA-256, fixture identity and benchmark identity remain
mandatory. GitHub-hosted CPU SKU equality is not an eligibility condition.

`EnvironmentClass` binds provider and runner class, OS family/version class,
architecture, assigned logical CPUs, provisioned memory class, exact runtime
versions and runtime/container configuration, compiler/Node/browser major
versions, Playwright version, benchmark configuration/workload/implementation
file digests and collector/result/fixture schema. Ubuntu 24.04, x86_64, standard
GitHub-hosted, four vCPUs and 16 GiB form the current runner resource class.
Available memory can reserve up to one GiB from the provisioned class; two,
eight or thirty-two vCPU/memory classes never silently compare to four/16 GiB.
Ordinary API/DB collection, candidate production DB collection and environment
smoke jobs pin `ubuntu-24.04` to preserve this declared hard OS class when
`ubuntu-latest` migrates. CPU SKU and physical host assignment remain unrestricted.
Historical campaign capture workflow bytes remain unchanged.

`HardwareFingerprint` separately retains CPU model/SKU, observed microcode,
kernel/image, physical host/generation information when available and exact
memory bytes. Missing historical observations stay null, never fabricated.
The full original fingerprint also retains all container/build identities.
CPU SKU, microcode, kernel patch, host identity/generation and Azure host model
do not reject an otherwise compatible class. OS version, architecture,
provider/class, resource class, runtime, compiler, benchmark schema and workload
changes remain incompatible. No performance/security threshold is relaxed.

Compatibility is `HARD_COMPATIBLE`, `HARDWARE_VARIANT` or `INCOMPATIBLE`.
The first two can compare; the latter cannot. Every canonical duration baseline
contains one original ordered five-sample stream from one selected group.
Hardware cohorts are never pooled. API sample aggregation rejects mixed hardware
within either its current or baseline cohort, while separate cohorts may report
`HARDWARE_VARIANT`. Median/MAD and all existing hard ceilings remain unchanged;
variance alone never becomes product-regression evidence or an automatic PASS.

## Historical evidence and normalized enrollment

The schema-1 through schema-4 campaign manifests, fixed approvals, original
results and workflow conclusions remain immutable. Their legacy exact hardware
digest is used only to replay their original decisions. It is not the active
duration selector. The legacy 9V45 target is historical fingerprint evidence,
not a requirement to wait for that SKU or repeat a host lottery.

`db_class_baselines.py` authenticates the original GitHub archive/run, replays
structure and MAD from the pinned measured source, verifies the declared target
fingerprint's original digest and compares the new hard classes. It admits only
the removed CPU-model constraint; other original failures are not rescued.
The current benchmark contract must still match the measured contract. All
groups/samples remain unchanged and the earliest eligible stable complete group
is selected under the new model. An original `BASELINE_UNAVAILABLE` result and
failed workflow remain historical; `environment-class-qualification.json` is
a separate, explicitly versioned reevaluation. It never rewrites a workflow.

Normalized enrollments live in `performance/environment-class-baselines.json`.
Their nine canonical documents live under
`performance/baselines/db/environment-class/<profile>/<class digest>/`.
The old approved Small ledger/catalog and all original evidence stay byte-for-byte
unchanged. Normalization preserves that owner's approval. Medium promotion needs
the owner's actual authorization; the current owner's conditional instruction
authorizes promotion only when all class/structure/stream/MAD/integrity/schema/
sample/group criteria pass. The executor records this instruction's origin and
its satisfied conditions, without pretending it was a native GitHub review.
No new capture is needed when already authenticated evidence meets those criteria.

Introduced enrollments authenticate the archive bytes against live GitHub metadata.
Later validation replays retained evidence, regenerates all nine documents, checks
the approval record and preserves previously enrolled bytes. Missing or ambiguous
catalogs, source changes, altered samples, unapproved enrollment, candidate-self
baselines, schema incompatibility and structural failures still fail closed.
