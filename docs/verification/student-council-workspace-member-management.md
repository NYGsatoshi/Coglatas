# Workspace member-management presentation

Implementation status: candidate, not qualified for school-user use. The Owner
authorized the bounded management-options/candidate contract and qualification
of existing command authority on 2026-10-20. Ordinary member self-leave remains
outside this change. No new role hierarchy or last-Owner rule is introduced.

## UI and data boundary

The ordinary directory/detail remains the source of currently visible members.
Two separate, server-owned management reads determine whether an add action or
a particular target operation is available. Role choices come from the current
response; the browser neither infers authority from a displayed role nor chooses
Owner by default. Every write uses the existing command and reauthorizes on the
server. A previous options response grants no write authority.

The add dialog searches eligible existing users by an explicit display-name
prefix of 2–100 Unicode scalars. It requests twenty rows per page and preserves
the prefix on opaque-ID continuation. Candidate state copies only display name,
opaque identity and allowed roles; it copies no email, account/Tenant role or
total count. The user must select a role and confirm the displayed identity.
The action is labeled **Add member**, because it sends no email invitation.

Role changes send only `{ role }`, omitting membership status. Removal uses the
existing access-revocation command. The UI does not invent an audit-reason API
or present a simulated completion. Pending writes disable duplicate submission.
A rejected or uncertain write drops sensitive editor state and refreshes the
directory; recovery rechecks with GET and never automatically repeats a command.

Management state is memory-only and owned by the current Workspace and request
generation. Central session, Tenant, Workspace and authorization invalidation
clears options, candidate identities and editor input and cancels subscriptions.
Late responses cannot populate the replacement scope. Reconnect/catch-up reads
current options without restoring an old editor. Unknown roles, malformed
responses and mismatched Workspace/target identities fail closed.

## Design source

Read-only Penpot `CoglatasUI`, file
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`, revision **441**, fetched
2026-10-20T01:12:42.089Z. Approved Dark W10 board
`e1d76e7e-ebca-80a9-8008-d017f5414675` supplies the header action treatment,
directory and 376px desktop inspector. The existing dark theme tokens and
shared Coglatas dialog provide surfaces, controls, focus trap and responsive
layout. The invitation-design label is deliberately adapted to the actual
existing-user operation. Illustrative Penpot roles/capability decisions are
never substituted for server values.

Mobile layouts stack the inspector and wrap controls. The shared dialog keeps
its header and actions visible while its content scrolls inside the viewport,
including a 320×420 viewport. Search Enter ignores IME composition/key229. Mutations require an
explicit dialog action. The shared dialog now ignores IME Escape while retaining
its existing focus trap, return focus and busy-state behavior.

## Verification limits

The first narrow Linux run passed 55 tests; two expanded runs each passed 57
across the management facade, member page and shared dialog. The latest run
includes the removal of obsolete simulated management UI and the dialog fix.
These include explicit
roles/identity, single submission, scope clearing, stale-request cancellation,
uncertain-result GET recovery, Unicode bounds and IME keyboard controls.
They use Angular HTTP test responses and do **not** establish backend/API or
PostgreSQL compatibility.

Two production builds passed, the latter after the responsive dialog fix;
the earlier build pipeline also passed architecture policy, all 59 architecture
tests and all four license guard tests. These local builds did not perform
licensed activation. Existing style-budget warnings remain, including the
member page at 5.88kB against a 4kB warning budget; the unchanged error limit
was not relaxed.

The first focused Linux browser run passed nine cases. After visual review
found a scrollable confirmation footer at 320px, the shared-dialog fix passed
ten focused cases with zero skips or retries. Fresh original captures cover
1536/1366/768/390/320px plus selected and uncertain states at 320×420. The
selected short-viewport case checks the action's actual hit target before
submitting. Axe and synthetic IME events are bounded diagnostics, not physical
IME or full WCAG qualification. W10 parity is partial: the existing ordinary
API does not supply the reference KPI/activity composition, and no values or
permissions were invented to fill it.

Real-backend browser integration, final hosted CI and resulting-Main
qualification remain required. The separate backend workstream owns actual PostgreSQL/HTTP
projection/command correspondence, denial and revocation controls. Passing
either workstream alone does not qualify the combined feature or public pilot.
