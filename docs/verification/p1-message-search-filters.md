# Issue #359 Message search and quick-filter verification

Status: implementation candidate on `feat/359-message-search-filters`.

## Implemented slice

- The Messages landing page now keeps Message search and Unread / `@Me`
  conversation filters visible on desktop.
- A 640-pixel mobile boundary replaces the expanded controls with one native
  disclosure button, focuses search on open, and returns focus on Escape.
- Text search uses only the existing bounded `/api/search?type=Message`
  projection. It validates type, UUID identity, and the internal Conversation
  route before rendering a title, bounded snippet, or author name.
- Conversation chips use only the authorized list projection's `unreadCount`
  and `hasMention`. They combine independently, expose `aria-pressed`, and can
  be removed individually or together.
- Active query and conversation conditions reuse the canonical shared
  `CoglatasFilterChipComponent`; its native remove button keeps the condition label
  and value in the accessible name.
- Search and local conversation filtering are labeled as separate scopes. This
  #359 record predates the additive #367 advanced-filter implementation in
  `docs/verification/p2-message-advanced-filters.md`.
- Search-empty and conversation-filter-empty states both offer an immediate
  adjustment path. Server failures use fixed redacted text.
- No attachment control or attachment interpretation was added by #359. The
  later #367 facet recognizes only safe canonical pre-existing links and does
  not resolve the still-open BE-004 upload/persistence defect.

The exact contract and non-goals are recorded in
`docs/contracts/message-search-filters-v1.md`.

## Authorization evidence

This change does not alter a backend contract. Message matches remain behind
the existing Search service's shared recursive readable-Conversation relation,
including tenant, participant, Project, ancestry-integrity, and depth checks.
The browser neither treats visibility as authorization nor broadens the query
to `All`. Quick-filter input is already produced after the Conversation list's
final readable-ID check.

The component rejects non-Message result types, malformed identities, unsafe
routes, and duplicate Message identities. It renders no API error detail and
uses no server `totalCount`, so invalid or unavailable records do not add a
visible count. Existing PostgreSQL and HTTP negative authorization coverage is
the backend evidence; no duplicate frontend-driven authorization path was
introduced.

## Local verification record

- application Angular TypeScript compilation: passed;
- frontend spec TypeScript compilation: passed;
- focused Angular component suite: 4 passed, 0 failed;
- frontend architecture check and its four-rule Node suite: passed;
- Angular production build: passed; the new component is below the component
  style budget, while the repository's existing initial-bundle and unrelated
  component-style warnings remain;
- focused Chromium browser coverage: 2 passed, 0 failed, with the 320-pixel
  keyboard/axe scenario in the mobile project and always-expanded controls in
  the desktop project; 2 opposite-project cases were intentionally skipped.

The focused existing Messaging regressions passed 30 tests across the main UI
and Conversation list files, and the Message-thread route suite passed 23
tests. A local full Angular attempt completed 952 of 957 assertions; five files
hit the shared five-second setup timeout under parallel host load, including
four unrelated Project/Announcement files and one Messaging test that passes
in the focused run. A local full static Playwright attempt passed 120 tests
(including #359), skipped 9 project-specific cases, and retained three
unrelated failures: one pre-existing Project request-count race and the two
diagnostic Windows screenshot comparisons. Per repository policy, Windows
screenshots are not approval evidence. These attempts are recorded as broad
diagnostics, not full-suite pass claims; exact-head Linux CI remains required.

The focused browser route is mocked and therefore does not replace the
existing backend authorization and PostgreSQL coverage. Exact-head CI supplies
the authoritative full Angular, Storybook, Linux Playwright, backend, and
PostgreSQL gates.
