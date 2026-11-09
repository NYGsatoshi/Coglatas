# Milestone validation preserves the caller's selected state

The active Project milestone service rejects missing or blank create titles,
missing create dates, nonpositive update versions, blank update titles and
activation of an undated milestone. Eleven additional Application-level
cases exercise these boundaries with an authorized manager in the existing
Project fixture. All original test methods, fixtures and assertions remain.

Each denial retains the selected existing milestone identity, parent, title,
description, date, status, sort order, version and lifecycle timestamps. It
leaves the milestone inventory unchanged and emits no audit or invalidation,
command save, generic save or command-tracking clear. A subsequent valid
command in the same fixture must succeed, trim the requested fields, use the
command unit of work once, and emit the scoped audit and Project invalidation.
The create control also retains the original milestone unchanged; the update
control advances its existing version exactly once.

These are Application tests with repository, authorization, audit,
invalidation and unit-of-work doubles. They do not establish PostgreSQL
transaction durability, concurrent authorization, HTTP binding of invalid
DTOs, all-table RLS, delivery, complete graph preservation, persistence-error
atomicity, File/Artifact reconciliation or full Recovery. The null-title
input is supplied directly to the service and is not an HTTP acceptance claim.
The fixture's existing operational Project setup is not a product activation,
source promotion or Owner approval.

The change adds tests and this scope document only. It changes no product,
schema, authorization, coverage subjects, imports, thresholds or baselines.
Supported hosted compilation and named execution are required; local .NET
execution is not used. Any recognized PR coverage must be distinguished from
the resulting Main's separately measured denominator and actual covered-line
and condition delta. Historical failures, Owner HOLDs and RELEASE_NO_GO remain.
