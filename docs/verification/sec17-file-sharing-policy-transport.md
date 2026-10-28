# SEC-17 current File sharing policy transport candidate

This separate, stacked candidate covers a direct Workspace File's current
read audience changing through the existing sharing API. It does not modify
or reuse held private-channel, account-suspension or Thread candidates.

Source starts from unheld SEC-17 head `09d45d5226158f82aaa1740d195bf81f92733d0e`.
That head's eight-file cohort remains frozen. Initial PR execution on this
stack is source-specific candidate evidence only. The later combined Main
requires normal integration, independent review and fresh qualification.

The exact native owner is
`SecurityArchitectureFilePolicyTransportTests.ActualWorkspaceFileSharingPolicyReauthorizesEstablishedAndFreshTransportAndReplay`.

## Current implemented contract

The ordinary `PUT /api/files/{fileObjectId}/sharing` command takes
`shareWithWorkspace` and the current `expectedSharingVersion`.
For a direct Workspace Attachment it changes FileSharingPolicy between
Workspace and Private, advances SharingVersion, audits and stages
`Files.FileChanged.v1` in the same save. The event's ordering token is
separate from SharingVersion. Its Workspace route does not itself authorize
the File: the actual dispatcher also checks current Attachment visibility.

This is an implemented audience-policy transition. It does not relocate a File
between Tenants, Workspaces or Projects, create a new policy, or impose the
direct-Workspace sharing rule on Task/Message-owned attachments.

## Native positive and negative controls

The real Web entry point, migrated disposable PostgreSQL, cookie/CSRF HTTP,
Outbox dispatcher and authenticated WebSockets are retained without service
replacement or a group-membership bypass.

An owner uploads a direct Workspace File through HTTP. The member has active,
unchanged Tenant/Workspace membership and the same valid session, ordinary
Member/User roles, no target uploader/owner identity and no FileAccessGrant.
These persisted facts are checked before and after the transition and replay.
FileAccessGrant is the explicit sharing grant, distinct from download grants.

After the actual Workspace policy command, owner/member receive the exact
HTTP-produced File event; member File and Attachment HTTP detail reads succeed.
Owner physical bytes remain a positive throughout. The actual Private command
must persist its next version and produce the exact native event and route.
The owner receives it and the Outbox reaches Delivered, while the member's
current File and Attachment HTTP reads return the existing generic HTTP 400
contracts. Workspace subscriptions remain legitimately allowed.

Both the established and fresh member sockets receive an unrelated authorized
File event produced by that member's own real upload. This independent File
never grants target access. They must remain open while the existing bounded
15-second helper observes exclusion of the private target event.

The exact actual sharing event is then requeued through the existing disposable
repository/dispatcher path after the fresh socket connects. Its immutable
envelope/routing context must remain byte-identical, owner receipt must advance
and fresh Delivered time must be observed. Both member sockets receive another
unrelated File positive before bounded replay exclusion. This does not qualify
a production replay endpoint, operator authority, total dispatch ordering,
global exactly-once delivery or capacity.

A real Workspace policy restoration must restore member HTTP reads and a new
native File event on all receivers. The three target sharing-success audits
and final persisted version are checked. Denial audits remain legitimate;
the test does not require all audit state to be unchanged.

## Evidence and execution ownership

The dedicated public backend-only workflow selects exactly this named owner
with PostgreSQL 18.6, read-only contents permission, no protected environment,
secret context, persistent runner or write token. The existing closed TRX
parser and six-assembly/loaded-copy binding infrastructure are reused through
a separate exact-method verifier. The original Direct deep verifier remains
unchanged and retains its original single accepted owner. A future-candidate-only
fixture accessor checks actual received type/schema/count plus the existing
immutable typed Tenant/aggregate header. Wrong-header and metadata-without-live-
delivery controls cannot qualify a positive. No raw event body is retained;
the frozen base and 22-file cohort keep their exact original fixture bytes.

Missing, skipped, duplicate, stale, failed or counter-inconsistent execution
cannot qualify. New selector/privacy controls include failed native output and
exception canaries, actual Git/build/source/loaded-copy mutations, exact stdout
and saved-JSON equality, and closed field checks. The existing Direct controls
and catalogue controls still run. Only closed JSON is published and printed;
raw TRX remains private to the ephemeral runner with a 20-minute job bound.

Bodies, storage keys and resource/session identities are transient assertion
inputs. Fixed boolean diagnostics prevent their assertion-value publication.
No raw event payload, token, Cookie, routing key or HTTP body is evidence output.

The closed catalogue adds one exact method, changing 352 to 353 only in this
future candidate. The frozen 352 source is untouched. No parser admits unknown
cases or infers full SEC-17 acceptance. Finding normalization under #582 and
its approved inventory/suppression/Medium-ratchet policy are separate work;
a passing native test or execution digest is not a normalized finding.

## Qualification status

Source preparation is not execution. Initial exact source/build/native
TRX/transport/image identities, independent review and hosted checks must be
recorded from the candidate's own runs. Initial stacked evidence cannot
replace later combined- or resulting-Main results. #690 and all applicable
Owner/SEC-ARCH/RLS/deployment/release holds remain unchanged.
