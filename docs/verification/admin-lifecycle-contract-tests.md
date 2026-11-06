# Administrative profile and setting contract tests

The additional AdminService tests use the existing repository, clock, audit,
authorization-change and unit-of-work doubles. Original assertions remain
unchanged. One existing project-archive assertion loop uses foreach instead of
an assertion-only Assert.All callback, retaining each of its four field checks
for every invalidation item. The fixture separately exposes the existing
session stub and records revocation arguments while preserving its original
successful zero-session result.

The controls cover persisted administrator eligibility, bounded pagination,
profile validation and normalization, sensitive setting response masks,
setting patch preservation, optional creation defaults, audit metadata and save
counts. A suspended profile requests session revocation for the exact subject,
actor, reason and cancellation token; an active profile does not. Denials leave
the observable audit, authorization-change, user-wide session-revocation call
and save outputs empty.

These are Application contract tests. They do not prove PostgreSQL commits,
HTTP admission, deployed RLS, real session invalidation, atomic authority through
commit, outbound delivery or pilot acceptance. Local native coverage remains
diagnostic until a separately qualified resulting-Main official analysis.
