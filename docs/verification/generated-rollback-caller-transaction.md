# Generated rollback and caller transaction control

This additive PostgreSQL control preserves the historical data-retaining repair
migrations. It generates the entire latest-to-zero rollback SQL without outer
transaction commands, checks descending migration-history deletion identities,
and submits the unchanged script inside a transaction owned by the test.

The expected retained-object dependency refusal is PostgreSQL `2BP01`. A second
query must observe failed-transaction state `25P02`. The test explicitly rolls
back, then compares the selected full logical catalog, column ordinals, a populated
synthetic Tenant row and every migration-history row. Applied/pending migration
identities and the EF model must still match the original head.

This exercises actual script generation and a real database failure/rollback
boundary. It does not establish successful full downgrade, every Down SQL command,
the product migrator's transaction policy, concurrent application traffic, all
application data/sequence values, physical file identities, backup/restore,
File/Artifact bytes or unknown-outcome reconciliation. The selected Tenant row
is not a full populated-data recovery rehearsal. G09 remains incomplete.

The existing post-repair roundtrip test and its assertions are retained. Only its
class declaration becomes partial to share the existing catalog reader with the
new test. No product migration, retaining behavior, CASCADE policy, authorization,
RLS, source/import scope, baseline, threshold or budget is changed. Native exact
source execution, independent review, hosted checks and the original PR Gate are
separate requirements. Coverage changes require resulting-Main measurement and
are not inferred from SQL generation or local test counts.
