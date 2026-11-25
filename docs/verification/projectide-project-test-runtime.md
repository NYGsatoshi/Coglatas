# Project Test development runtime

Issue #1945 / Owner decision D14.

A development host can compose the shared ProjectIDE viewer and operation
authorization services, a server-owned immutable input resolver, and the
PostgreSQL Project Test job store. The host supplies the identity of its loaded
typed evaluator. The runtime's evaluator identity helper uses the same artifact
ID, assembly version and binary hash domain as the local Test CLI.

The start operation binds a job ID to the current actor, immutable Source
context, Source and Compiler input digests, exact materialization binding and
evaluator identity. The input resolver receives the current viewer and returns
the same compiler host request used to verify those pins. A private fixture may
remain unavailable. Reuse of a job ID inside a Tenant returns the original job
only for the same binding and current authorization. It does not restart a
terminal job.

The runtime executes the existing bounded typed Test plan. The application
coordinator supports a later authorized request by the owning actor, including
after a process restart. Caller cancellation before the terminal transaction
leaves the job pending and retryable. Explicit durable cancellation and result
publication compete for one terminal transition. A PostgreSQL row lock and
constraints preserve the winning transition and immutable input identity.
Concurrent execution requests can repeat bounded pure evaluation before either
request reaches the lock. A durable cancellation prevents result publication
when it wins that lock. An evaluation already in progress stops through its
caller cancellation token or completes within the typed plan's bounds.
Execution requires the host's evaluator to match the pinned identity.

Separate operation policy checks apply at start, read, cancellation and export.
The runtime revalidates persisted viewer authority after asynchronous source
resolution, after a write lock is acquired, and before returning a write
receipt. Lookup first selects metadata by the active Tenant and exact
Project, Branch, and Job. The shared operation policy checks that metadata's
actual Source identity and digest before the response is available.
Stored result metadata retains the original result and plan digests,
and distinguishes execution, coverage, verdict, freshness and evidence kind.
Conditional Core outcomes remain conditional in the runtime. Start and
cancellation return lifecycle status without disclosing Test outcomes. The
authorized export contains selected identities and outcome axes, with no
Source bodies, private values, names, paths or assertion details.

Development composition requires a host-controlled implementation of the
shared operation policy and the immutable Source resolver. Activation of live
permissions, production endpoint and client adoption, artifact eligibility,
required-suite admission, shared Merge use and Release qualification have
separate approval and evidence requirements.
