# Announcement current-authority boundary

Audience options and scope authorization evaluate the existing permissions of
the supplied actor against persisted state. A missing, inactive or soft-deleted
account receives an empty successful option list or a successful denial before
membership, management or elevated-role fallbacks. Invalid authentication and
Tenant contexts retain their existing errors. Explicit server-owned author
authorization requires Tenant context without requiring a browser principal.

`AnnouncementRepository.ReadCurrentAuthorizationAsync` temporarily uses
`QueryTrackingBehavior.NoTracking` for the read-only Audience operation. The
production repositories and nested Workspace, Group and Channel authorizers
share that scoped `AppDbContext`. Their queries therefore cannot substitute an
earlier tracked User role, membership or parent for a newly committed value.
The repository restores the previous mode in `finally`, including exceptions
and cancellation; it does not save, detach, reload or roll back caller changes.
Non-EF adapters retain the interface's callback default and must supply current
authority data themselves.

Existing ordinary management grants and Teacher/Admin visibility fallbacks
remain intact. `SystemAdmin` and `PlatformAdmin` remain aliases of persisted
value `5`; neither an old cookie claim nor a retained tracked role grants current
authority. The durable processor rechecks the persisted author during validation
and before publication. Denial retains the existing Scheduled/deferral behavior
and does not publish an Announcement or emit its invalidation.

Account and role regressions use actual Application services, authorizers and
repositories, including a separately committed writer while retaining older
tracked entities. Explicit PostgreSQL controls exercise account revocation,
Workspace/Group role demotion between processor checks, legitimate elevated
publication, and caller-owned pending writes, transactions and rollback. The
read-scope controls also cover all three prior tracking modes, exceptions and
cancellation. Contract doubles do not establish persistence freshness.

This boundary does not lock the authority graph or make revocation atomic after
the last authorization read. It does not qualify lease concurrency, hosted-worker
operation, deployed roles/RLS, delivery transport, or release approval.
