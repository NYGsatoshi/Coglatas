# Legacy Channel lifecycle persistence controls

The additive PostgreSQL control uses the actual Web host, HTTP authentication,
Channel application service and repositories in an owned synthetic database.
It exercises creation, update, confidential viewing, member addition/removal and
archive, checking persisted Tenant/Workspace/Group identity, creator membership,
returned projections, audit actors/actions and a retained sibling Channel.

Each selected denied HTTP operation compares newly loaded Channel, membership,
Post and Channel-audit scalar snapshots before and after the request. Cases cover
non-manager creation, blank names, confidential visibility before admission and
after removal, duplicate/missing/foreign-parent target membership, ReadOnly
mutation/posting, foreign Tenant archive and repeated member removal. This is
not an exhaustive current-authority, concurrent revocation or transaction test.

Fresh contexts establish committed observations after each HTTP operation; they
do not prove multi-query admission atomicity, all-table RLS, actual Realtime
delivery, physical-device/HTTPS acceptance, File bytes or orphan reconciliation.
The existing legacy HTTP failure status is preserved. No product, existing test,
authorization contract, source scope, baseline, budget or threshold is changed.

Native exact-source execution, hosted checks, independent review and the original
PR Gate must be recorded separately. Main coverage requires an actual resulting
Main measurement; local coverage is not an additive credit or release approval.

Initial authoring commit `908d47da0e5caa491e9bfd40cbb7809364ba1b08` ran
the native control and failed at the first member-list DTO equality assertion.
The immediate response can retain .NET 100ns ticks while the persisted PostgreSQL
timestamp has microsecond precision, as documented by
[Npgsql](https://www.npgsql.org/doc/types/datetime.html). The corrected new test
requires UTC and exact microsecond truncation for both the listed DTO and the
freshly loaded row; all other DTO fields still compare exactly. This changes no
product timestamp behavior or existing Main assertion. Later assertions were
not reached in the failed first run and receive no verification credit from it.

The original failed source, TRX, log, provider receipt, raw report and seven
executed DLL/PDB pairs are retained: receipt SHA256
`6501afcf71d0ce36e12d5845a6c3638096afdb932d3ed861f03a41c8ab39e4cf`,
source archive
`111fa574b31bbc529536c58aa5e15af500a5a69ee14ec5a7f3a99f1e841acbea`.
