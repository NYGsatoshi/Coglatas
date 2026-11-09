# Legacy Channel/Post edit, soft-delete, pin/unpin persistence — Issue #538

Status: **Option A implementation candidate / source review + exact-head CI required / Pilot RELEASE NO_GO**.

## Decision and defect

Keep the existing public legacy `/api/posts/{id}` PATCH/DELETE and
`/api/posts/{id}/pin` POST/DELETE endpoints as a **supported, legacy**
surface. The newer canonical Conversation/Thread Messaging flow remains
independent. Retiring existing routes without migration would create a
compatibility gap, so this change follows Issue #538 **Option A**.

Before this candidate, `ChannelRepository.GetPostByIdAsync` loaded Posts
with `AsNoTracking()`. `ChannelService.UpdatePostAsync`,
`DeletePostAsync` and `SetPinnedAsync` mutated those detached objects,
then called the unit of work. The audit row could be saved even when the
Post's body/soft-delete/pin state was not. Successful API responses did not
establish persisted state.

## Bounded fix

- Preserve `GetPostByIdAsync` as an explicit **read-only** query for
  ordinary GET, Thread/authorization reads.
- Add `IChannelRepository.GetPostForUpdateAsync` with explicit
  `AsTracking()`, preserving the author projection required by
  `PostResponse`.
- Call the tracked read only from `UpdatePostAsync`, `DeletePostAsync`
  and `SetPinnedAsync` (shared by pin/unpin).
- Revalidate a stored author's current Channel read or management authority
  before edit or delete, returning the existing masked `Post not found.`
  error when both have been revoked. Preserve the existing Group/Channel
  management authority for non-authors and managers of inactive channels.
- Retain the existing denial/error mapping (HTTP 400 for the supported legacy
  controller), soft-delete policy, UTC time source, audit identifiers and
  **single** `SaveChangesAsync` for the Post mutation and Audit row.

No new API, status mapping, schema/Migration, event type, RLS policy,
role/grant, background worker, UI route, performance budget, scanner
threshold, retry, skip or timeout is added.

## Real PostgreSQL test contract

`LegacyChannelPostMutationPersistencePostgreSqlTests.EditDeletePinAndUnpinPersistWithCommittedAuditsAndCurrentDenials`
uses the existing disposable security-fixture ASP.NET Core host and actual
PostgreSQL migrations. The test seeds a unique synthetic legacy Group,
Channel and Post under an existing synthetic Workspace, authenticates
Owner, Member and distinct-Tenant sessions with existing cookies/CSRF,
and verifies:

1. Member edit/pin and distinct-Tenant delete return **HTTP 400**,
   never a generic non-200 that could accept HTTP 500, with unchanged
   fresh-DbContext Post and no mutation audit. A separate synthetic member-
   authored Post without current Group/Channel visibility also rejects
   GET/PATCH/DELETE with HTTP 400 and leaves the Post/audit unchanged.
2. Owner edit returns the trimmed body and **persists** `Body` and
   `EditedAt` across a new DbContext with one `PostEdited` audit.
3. Owner pin persists `PinnedAt`, `PinnedByUserId`, and the pinned
   listing with one `PostPinned` audit.
4. Owner unpin clears both pin fields across a new DbContext and records
   exactly one `PostUnpinned` audit.
5. Owner delete persists the existing soft-delete timestamp, excludes
   the Post from listing and records exactly one `PostDeleted` audit.

The test **must actually execute**, rather than early-return because
`POSTGRES_TEST_CONNECTION_STRING` is absent. Its named xUnit result,
nonzero duration, checked source and PostgreSQL version must be read from
the exact-head backend CI log. Keep the historical bug open if a current
source run fails and do not suppress its evidence.

## Boundaries / future work

This is the legacy `Post` database aggregate, **not** the canonical
`ConversationMessage` storage used by the student-council Teams-like
channel and DM pilot. It prevents a real integrity bug in retained routes
but does not complete current pilot SEC-16/17, message reliability
#1360/#1365, RLS activation, or external HTTPS admission.

Future optimistically concurrent modification/version semantics would
require a separate approved contract and real PostgreSQL race proof.
This patch does **not** claim such a guarantee.

Pilot staging and formal SEC-ARCH/RLS **Owner HOLDs** remain intact.
No new school-user access, credential publication, migration, production
database operation or release GO follows from this fix.
