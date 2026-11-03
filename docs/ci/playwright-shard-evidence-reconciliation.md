# Playwright two-shard evidence reconciliation — static trial

This experiment does not start a shard or replace protected `frontend-test`. 
The immutable Angular build producer and the existing full test/check execution
remain unchanged. It tests a future shard receipt/manifest data contract.

An independent source-and-producer-bound roster supplies the exact set of
privacy-safe SHA256 test identities and expected PASS/SKIP statuses. Both
shard receipts must have the same exact tested merged SHA, run ID, attempt,
immutable Angular artifact ID and roster digest. Shards 1/2 and 2/2 must
contain nonempty strictly ordered IDs with disjoint complete union. Unknown,
repeated, missing, skipped-drift, failed, interrupted or retried cases
are rejected, not counted as a green required gate. Output contains only
counts, hashes and identity metadata, never paths, names, HTTP data or secrets.

The manifest verifier intentionally reports `EXTERNAL_VERIFICATION_REQUIRED`
because JSON metadata does not prove that a GitHub artifact ID exists or belongs
to a successful same-run producer. A future CI gate must independently query
the GitHub artifact/run/job APIs and verify uploaded bytes before publishing a
protected `frontend-test` PASS. The complete original keyboard/search evidence
retention and all required checks remain unchanged.

No speedup or sharding activation is claimed. Only `SecurityTestImplimation`
may be modified, never Main.
