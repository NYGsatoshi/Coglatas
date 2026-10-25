# Files download operation lifetime — 2026-10-26 JST

Status: **IMPLEMENTED_NOT_VERIFIED** for hosted qualification. Student Council
release remains **NO_GO**; existing Owner holds remain in force.

## Demonstrated mechanism

Base Product Main: `f31b15f2b42d3b83b5c1ffae5ea304922e10cc16`.
This is a new deterministic Files control, not an attribution of the historical
Thread, Composer, Channel dialog or ReadOnly keyboard failures.

`FilesFacade.downloadFile()` previously used the displayed row's `pending`
state to suppress a duplicate click. A fresh authorized inventory/search
response maps the row back to `idle`, while its earlier grant or Blob request
can still be pending. A second explicit click could then start another grant
request and replace the same FileObject's operation-map entry. The earlier
operation's cleanup could remove the newer entry, leaving its completion
unobservable to the existing current-operation guard.

Separately, explicit inventory reauthorization advances the page generation
and cancels registered page reads. Download operations were not registered in
that lifecycle. Their old callbacks were ignored, but their network requests
and operation-map entries survived until protected-state clearing.

## Focused correction

- Use the existing live download operation map as an additional duplicate guard.
- Register each operation with the existing page-request lifecycle, so inventory
  reauthorization cancels its grant/Blob request and releases its map entry.
- Retain the original grant FileObject identity validation, server grant/bytes
  authorization, denial messages and explicit manual action for a new download.

No server API, grant persistence/expiry, audit contract, authorization decision,
upload intake policy, RLS, migration, deployment or design token changes. Client
cancellation does not claim that a server-issued grant was revoked or removed.

## Source-bound local controls

The test-only RED commit is
`c59177ac1f1eaebf0b2cfcd5cc81921f9d7437c9`, on the unchanged product Main.
The owning Angular command reported **23 PASS / 4 FAIL**: two duplicate-grant
controls (pending grant and pending Blob) and two uncancelled inventory
reauthorization controls. No timeout, retry or existing assertion changed.
An earlier test draft had a teardown cascade after its two expected failures;
it was corrected before this isolated four-failure result and supplies no
additional product-failure count.

With the focused production correction, those controls and the unchanged
owning cases passed **27/27**. Two additional negative controls retain the
original 403 at each stage after a fresh search response, assert zero saved
bytes/history updates, and require a new explicit grant request for retry.
Their first run had a test-only English message mismatch (`Download denied.`);
the product denial behavior was correct and the expectation was corrected.

Validation commands:

```sh
npm --prefix frontend test -- --include='src/app/features/files/**/*.spec.ts'
npm --prefix frontend run build
npm --prefix frontend run check:architecture
git diff --check
```

The final Files selection passed **169/169 tests across 18 files**, including
all 29 owning facade controls, with no skipped cases. A subsequent test-only
cleanup replaced a broad cleanup match with an exact expected inventory
request; the owning **29/29** controls passed again. The local production
build and architecture/diff checks passed. Existing
warning budgets were unchanged; the production build retained initial-bundle
and component-style warnings. These checks use the environment's existing
dependencies (reported Vitest 5.0.3); a clean exact-head hosted dependency
installation, required CI and independent review remain separate qualification.
Angular HTTP controls are not real ASP.NET Core/PostgreSQL download execution,
public HTTPS, physical-device, keyboard or Japanese IME acceptance.

## Pilot gate impact

The correction strengthens the Files interruption/refresh behavior in G01.
It does not pass G01 in full, qualify Main G02/G03, or satisfy target security,
backup/restore, cross-session, physical-device or school-admission gates.
The Files page's intentionally unavailable upload chooser and existing tenant
FileSharing intake contract remain unchanged. Historical P0s #1391 and #1346
remain open: their current positive controls do not establish the original
hosted initiating cause.
