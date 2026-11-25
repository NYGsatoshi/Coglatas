# ProjectIDE Draft Rename development Runtime

Issue [#1930](https://github.com/NYGsatoshi/Coglatas/issues/1930) tracks the
development and independent acceptance of server Draft Rename.

The Application service separates preview and apply authority through the
shared ProjectIDE viewer and operation authorization boundary. The server
loads the admitted Draft and materializations and computes the existing
typed Rename plan for the requested location, name and exact Source/input.
Apply recomputes the confirmed plan under a candidate row lock and compares
its original digest. Unavailable, incomplete, stale, denied and unconfirmed
plans return the same unavailable outcome. The save receipt carries only the
new candidate and digest identities.

The PostgreSQL store retains exact canonical bytes for each immutable
candidate and constrains successor lineage to the same Tenant, Project,
Branch and Proposal. The idempotency transaction stages the new row, audit
and the host's private Outbox invalidation. Replaying an identical request
loads its exact candidate behind current authority. Changing the name or
other bound request facts under the same key returns a request mismatch.
The persisted audit summary excludes the old and new names.

A development host supplies the operation policy, admitted Draft
materialization source and private Outbox routing. Positive fixture policies
and fixture events qualify the local transaction boundary only. Operational
policy and routing, actual Web and Native clients, independently authored
qualification and canonical integration have separate acceptance.
