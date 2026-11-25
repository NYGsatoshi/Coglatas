# ProjectIDE Draft Rename development Runtime

Issue [#1930](https://github.com/NYGsatoshi/Coglatas/issues/1930) tracks the
development and independent acceptance of server Draft Rename.

The Application service separates preview and apply authority through the
shared ProjectIDE viewer and operation authorization boundary. The server
loads the admitted Draft and materializations and computes the existing
typed Rename plan for the requested location, name and exact Source/input.
Apply recomputes the confirmed plan under a candidate row lock, compares
its original digest, and reads the current target independently of the Draft.
Unavailable, incomplete, stale, denied and unconfirmed
plans return the same unavailable outcome. The save receipt carries only the
new candidate and digest identities.

The PostgreSQL store retains exact canonical bytes for each immutable
candidate and constrains successor lineage to the same Tenant, Project,
Branch and Proposal. The idempotency transaction stages the new row, private
audit and Outbox invalidation. The coordinator revalidates current authority
and target after flushing those writes and before committing. Replaying an identical request
loads its exact candidate behind current authority. Changing the name or
other bound request facts under the same key returns a request mismatch.
The audit adapter receives only the current actor and Tenant.

A development host supplies the operation policy, admitted Draft
materialization source, current target reader, private audit visibility and
Outbox routing. Positive fixture policies and fixture events qualify the
local transaction boundary. Operational policy and routing, actual Web and Native clients, independently authored
qualification and canonical integration have separate acceptance.
