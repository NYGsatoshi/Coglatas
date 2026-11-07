# Native child Web shutdown qualification

The actual Web fixture previously killed its owned child process at disposal.
An exact-Main Linux run of the existing legacy Post mutation HTTP/PostgreSQL
test passed, while the original parent OpenCover report contained zero visits
for all 237 ChannelService sequence points. The equivalent Windows observation
is retained separately. A functional pass alone does not establish child coverage.

The fixture now attempts normal termination of its own Linux child through the
fixed absolute `/bin/kill` executable and that Process object's ID, with separate
bounded waits for the signal command and host exit. Missing utility, failure or
timeout retains the original process-tree kill fallback. Other platforms retain
the original behavior. Authentication, Hub/dispatcher services, process startup,
credential/environment isolation and test assertions are unchanged.

Qualification must compare exact-source before/after actual HTTP tests on the
same pinned Linux SDK/PostgreSQL and original native collector. Report bytes,
source ranges, zeros, executed DLL/PDB identities and the raw imported report
must remain inspectable. No report editing, synthetic hits, new imports, narrowed
source scope, baseline or threshold changes are allowed. A local result does not
establish resulting-Main coverage; measure the resulting Main independently.

This is test process cleanup and native collection infrastructure, not a product
shutdown endpoint, production operation, authorization/RLS change, deployment
acceptance, security disposition or Release GO. A graceful-stop attempt is not
proof that every child exits gracefully or that a previously unknown failure's
initiating cause is resolved.
