# Administrative bulk invitation validation staging

Bulk invitation creation validates every distinct candidate before adding any
invitation to the shared repository/unit of work. Existing SystemAdmin admission,
email, Workspace, Tenant and expiration checks and error order are preserved.
Valid batches keep their case-insensitive deduplication, scoped hashes, response
order, one bulk audit and one SaveChanges call.

Original supported head9c91c7e6 produced63 scoped cases:62PASS/1FAIL. The invalid
last candidate left one earlier invitation staged instead of zero. Original
source/tree/run42541031355/1/job128916422737 and log949099e5 are preserved in
Issue615 evidence, independent receipt0b20aafbc9f1907856408d4ae13b666b1440dd6732ce971ea0b4c60222a479d6.
The invalid-first and valid dedup/hash/scope controls passed. All original17
account lifecycle cases and both test files remained unchanged through that
product repair. Its supported build job subsequently passed all63 cases.

The first repair head5ac7e196 failed the independent ReSharper PR fast lane:
`ParameterOnlyUsedForPreconditionCheck.Local` on the new account test's
`Assert.All` callback. The follow-up uses a `foreach` over the same four results,
retaining both assertions for every result. The original failing run42541597729/1,
job128918345548, raw logdbaea952 and original SARIF ZIP29142db5 are retained.
No inspection budget or assertion was relaxed.

These controls use existing Application repository doubles. Supported fresh
repair execution, full CI, original PR Gate and independent review are required.
Repository/persistence exceptions and caller-owned transactions retain existing
unit-of-work ownership and need separate qualification. Unit queues/save counts
provide no real PostgreSQL commit, session persistence, token delivery, deployed
role or production acceptance. No real user invitation is sent.

Source subjects, coverage imports, thresholds, original assertions, Owner HOLDs
and formal finding dispositions remain unchanged. Requalify resulting Main
metrics and actual line/condition delta separately in Issue615; no projected gain.
