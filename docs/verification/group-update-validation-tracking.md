# Group update validation and tracked parent changes

`GroupService.UpdateAsync` used to assign a valid requested parent before
rejecting an empty or whitespace name. The service returned a failure without
saving or auditing, but the tracked Group retained the parent change. A later
independent save by the context owner could persist that rejected update.

The parent lookup and its error precedence remain before name validation.
The parent assignment now occurs after the existing name validation succeeds.
Successful metadata, name trimming, slug generation, authorization, audit and
save behavior remain unchanged. A null parent request still retains the parent.
No transaction ownership, role, RLS or public request contract changes.

Immutable test-only `c993de5a1abe26224da402e8289084e0e433e389` produced
three native EF Core InMemory failures and eleven passing controls. Each failing
case first observed a rejected name, no service save and no audit, then performed
an independent Workspace save and reloaded the Group to find its parent changed.
The original TRX, OpenCover, log and source/DLL-PDB bindings are retained outside
the repository. The complete original fourteen-case test body is preserved.

Controls retain missing, foreign-Workspace and self-parent error precedence,
denied and unauthenticated admission doubles, the admitted-missing path, optional
name behavior and successful reparent/name/slug/metadata/audit persistence.
These use real repositories with explicit current-user and authorization doubles.
They do not establish runtime authorization, PostgreSQL, HTTP, concurrent
isolation, all-table RLS, deployed or physical pilot acceptance. Hosted native
PostgreSQL qualification remains necessary before integration.

These Group row controls do not implement File/Artifact unknown-outcome or orphan
reconciliation and do not qualify successful caller-owned upload rollback.
No Overall Sonar improvement, original failure cause or Release GO is inferred.
