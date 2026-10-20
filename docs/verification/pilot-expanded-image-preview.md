# Pilot expanded image preview

Scope: an optional large image surface on the existing Angular Files page.
The existing inspector remains the default. No file or authorization contract
is added by this presentation change.

## Design source and adaptation

The connected read-only Penpot file was `CoglatasUI`,
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`, revision 441, confirmed at
2026-10-19T16:27:23.399Z. Board
`9a59a7a6-1369-80f7-8008-cf261fd20680`,
`FHV09 / File Preview & Transfer Queue — Approved Dark · 2026-10-18`,
was read at 2026-10-19T16:24:25.328Z. Its primary viewer is 901 × 807;
the image canvas is 867 × 546, the controls strip is 867 × 47, and the
viewer/canvas corners are 7 pixels. The existing Approved Dark semantic
surface, border, text, action and focus variables are reused.

The large surface adapts that wide canvas to a modal inspector: up to 1184
pixels wide with 16-pixel outer gutters, a 60dvh/34rem image viewport, and
native scrolling. Below 600 pixels it uses the full viewport width with no
outer gutter; controls wrap and remain keyboard accessible. This is a
responsive adaptation, not a claim that the entire standalone board matches.
Transfer queues, scan/Safe open guarantees, version labels and unrelated
Task associations remain outside the qualified pilot contract.

## Ownership and lifecycle

Expansion changes the existing inspector's layout and modal state. The same
`FileImageViewportComponent`, image element and object URL remain mounted.
The parent alone obtains the existing grant-backed Blob, validates MIME,
rejects obsolete generations, owns object URLs and clears protected content.
Expansion does not request another grant or copy a token/Blob into another
state store. Existing PDF, text and video renderer paths are retained.

Only a parent-authorized ready image on the Preview tab offers expansion.
The expanded header exposes the existing authorized Download operation.
Escape or the backdrop returns to the inspector and restores focus to its
expand button. Full Close uses the existing cleanup and row-focus return.
The modal focus trap contains keyboard navigation. File replacement,
Workspace/session changes, denial and URL revocation reset expansion before
another image may be displayed. Selecting another inspector tab also leaves
the expanded image surface.

The image scroll region explicitly marks the CDK focus-region end. This keeps
reverse wrapping on the last image control surface while preserving the
existing focusable Preview tabpanel and its keyboard semantics. Without that
marker, the installed CDK's ancestor-first last-tabbable search selects the
tabpanel before inspecting its descendants; the retained first browser run
observed that boundary mismatch in all ten width/profile combinations.
The pinned CDK22.2.3 implementation supports this camelCase spelling and
explicitly deprecates the dashed `cdk-focus-region-end` spelling.

## Verification

Local verification on 2026-10-20 JST used the pinned Linux
`mcr.microsoft.com/playwright:v1.64.0-noble` image, existing read-only dependency
volumes, no package installation or network, and fresh Vitest/Vite/Angular
caches. The installed Playwright package was 1.64.1, Node was 24.21.0, and
Angular/CDK were 22.2.3.

- Final focused component selection: 36 passed across two files, zero skips.
- Final production Angular build and architecture source check passed. The
  existing bundle/style warnings remain visible. The 9 kB component error
  budget is unchanged; the first two builds exceeded it by 220 and 40 bytes.
  Redundant declarations/selectors were consolidated before successful builds.
- Final browser selection: 34 passed, one worker, zero retries, in both normal
  Chromium desktop/mobile profiles at 1536/1366/768/390/320 CSS pixels.
  This includes 12 new expansion/download cases and 22 existing image/Files
  regressions. Decodable synthetic wide/portrait PNGs exercise geometry.
- The initial browser result was 24 passed and 10 failed at the explicit reverse
  focus boundary. Both directions and all original assertions pass after the
  supported CDK marker repair. A two-case focused smoke overlaps the final
  34 cases and is not additional unique coverage.
- All 20 final desktop expanded Fit/rotated-Fit frames, five mobile rotated
  portrait frames, and five default text-preview frames were opened and
  reviewed against the retained FHV09 reference and the live board geometry.

At 320 pixels the image controls wrap, and the modal pane can require vertical
scrolling. Keyboard focus may scroll its title out of view while its controls
remain reachable; collapsing and full Close return focus as tested. Larger
widths use the wider stage. These adaptations preserve access rather than
forcing the desktop board's fixed dimensions into a narrow viewport.

The immutable source, screenshots, logs and hashes are recorded in the handoff
receipt. Browser HTTP responses are mocked; these checks establish frontend
geometry, keyboard and lifecycle behavior only. They do not qualify backend
authorization, PostgreSQL persistence, malware scanning, a deployed tenant's
upload controls or public HTTPS. No screenshot golden, dependency, license,
authorization, upload flag, workflow or deployment configuration is changed.
