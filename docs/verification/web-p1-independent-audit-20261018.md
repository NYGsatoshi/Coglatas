# Angular Web P1 independent source audit — 2026-10-18

## Authority and evidence

- Product source baseline: `NYGsatoshi/Coglatas` Main
  `17b112fa6e3f53e8044fdda4e277b3e761380d88`.
- Reviewed implementation: [Penpot Web PR #1234](https://github.com/NYGsatoshi/Coglatas/pull/1234),
  changed-file list and selected source patches; source originally pinned to
  `05cc6095225ca8dc7271bf395c90ba18f23624db`.
- Confirmed the earlier [PR #1234 post-merge Main CI run](https://github.com/NYGsatoshi/Coglatas/actions/runs/39034748592)
  succeeded, and [PR #1236 post-merge Main CI run](https://github.com/NYGsatoshi/Coglatas/actions/runs/39046562957)
  completed SUCCESS on `17b112fa6e3f53e8044fdda4e277b3e761380d88` (45 successful jobs, 1 skipped).
- This is a **source-level independent review with targeted automated CI coverage
  proposed below**, not a full manual review of every changed line, a fresh
  live-browser run, or a production security/release certification.

## Review matrix and observations

| Slice | Source-reviewed surface | Finding / disposition |
| --- | --- | --- |
| i18n dictionary shape | Projects, Project Feedback, Files, Messaging, Shell catalogues | Parsed 1,457 English / 1,457 Japanese entries. No missing/opposite keys, duplicate same-locale keys, or mismatched `{{placeholder}}` identifiers in these five feature catalogs. This structural observation is not linguistic equivalence or execution evidence. |
| Locale/UI display | `I18nService`, `projectFeedbackView`, Files `FileMessageSource`, `MessagingFacade` projections | Source differentiates application-owned presentation copy from opaque user/server strings. Retained result is bounded; further live navigation/language-toggle checks belong to #482. |
| Workspace/Tenant boundary | Existing Angular Facades / protected scope clearers | No schema/API/authorization change in PR #1234. Previously passing E2E is retained, but this audit does not requalify security after future backend changes. |
| Message composer | `MessageComposerComponent` | **Source-confirmed stale metadata defect:** `ngOnChanges` previously revalidated `selectedMentionUserIds` only for `draft` changes. A changed authorized `mentionCandidates` list with unchanged text left the old ID in the outgoing UI event. Existing backend permission checks must remain authoritative. |
| Files / Artifact | `FilesFacade`, `FilesPageComponent`, `FileMessageSource`, Report Reader | Presentation-only source review completed; the full Project Files, Artifact Repository and target-picker workflows remain explicitly deferred by PR #1234 and require separate verified API/business contracts. |
| Project / My Tasks | `project-detail.facade`, `my-tasks.facade`, `projectFeedbackView` | No independently proven Critical/High regression in reviewed slices. Large-Project server paging and advanced ProjectIDE functions remain separate, not implicitly accepted. |

### Targeted P1 remediation

In the mounted Message composer, membership/eligible-mention changes need not
change the draft text or destroy the component. The currently selected mention
identity must be compared to the **latest** eligible candidates, not only to
a visible `@name` substring or a previous candidate list.

This patch revalidates selected IDs on changes to either `draft` **or**
`mentionCandidates`. Two new component tests model (1) candidate removal
with unchanged draft and (2) a different user ID with the same display name.
The outgoing event must contain no stale selected ID. The draft text remains
unchanged, and no new endpoint/permission is introduced.

These tests describe executable cases but are **NOT_EXECUTED locally** in this
review environment. CI results for the exact changed SHA must be checked before
claiming their PASS.

### Residual P1 audit gates

- [ ] Complete live cross-screen matrix on an exact candidate:
  320 / 768 / 1366 / 1536 widths and Dark/Light themes.
- [ ] Exercise keyboard, focus restoration, IME, language switching with an
  unsaved draft and repeated route changes on real Angular.
- [ ] Exercise actual current backend authorization/tenant/logout/SignalR/file
  grant denials and async cancellation. UI route guards are not authority.
- [ ] Record the exact SHA, CI run/attempt and test artifacts; distinguish
  SKIPPED, NOT_EXECUTED and FAIL from PASS.
- [ ] Independently reconcile unsupported Penpot-only features against API
  contracts before enabling File Hub v2 / dedicated multi-thread workflows.

## Security / change scope

No backend, PostgreSQL, RLS, migration, user authorization, Realtime protocol,
new dependencies, licensing, CI/ruleset, Penpot design, or SEC-ARCH activation
changes. The normal CI/review policy applies. This UI correction does not
establish that any protected mention is authorized to send; the server still
performs current authorization.

Related: [cross-screen regression #482](https://github.com/NYGsatoshi/Coglatas/issues/482),
[Chat UI #785](https://github.com/NYGsatoshi/Coglatas/issues/785),
[Web platform policy](../frontend/platform-policy.md).
