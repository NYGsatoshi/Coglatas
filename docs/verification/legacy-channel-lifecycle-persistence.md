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
