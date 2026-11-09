# Advanced search opener render readiness

## Immutable failure and scope

PR #1427 source `e7f32f4bc407b85d05baa76819b047dac06fd816`, tested merge
`04cb51106ea7991908ff27dc97e1c7bec8d6da41`, run `40277486775/1`, frontend job
`121424350214`, reported success with 418 clean passes, one flaky case and 13
configured skips. The advanced mobile search owner failed its initial attempt
at the unchanged 5-second drawer assertion; configured retry 1 passed. This is
not clean first-attempt qualification.

The retained finite artifact `12631796186` has ZIP SHA-256
`823f0e807b3d359dcfcc2b3cc01520112a8aa4b1ece61b0d0eae93891fec4d0d`.
Its initial 13 events are untruncated: the same connected host remains present,
both Enter sequences target the mobile toggle, and no opener focus/event is
observed. The drawer remains closed. These observations do not establish a
component replacement, routine catch-up, or the initiating cause. The separate
reviewed catch-up product candidate is excluded from this change.

## Controlled mechanism

The fixed two-case browser control uses actual unchanged EA7 production source
`ea7be66d3322bf2fc1ad608ad24e22bfba3efc90`. Its frontend tree
`c016252362f6e2ea1d6e3eb29c52909dbc8d1cca` equals the PR source frontend tree.
The synthetic static HTTP fixture establishes no real backend qualification.

Playwright's clock is installed only after initial readiness. Pausing scheduled
rendering after the first native toggle Enter leaves DOM `aria-expanded=false`
and the opener hidden. The original empty-input-value assertion nevertheless
passes. Focusing the hidden opener leaves native focus on the toggle; pressing
Enter through that locator dispatches a second trusted toggle Enter/click. The
same connected component stays collapsed after clock release.

The positive case releases the controlled clock, requires expanded toggle and
visible opener, focuses the opener and verifies focus before pressing Enter.
The second trusted Enter/click reaches the opener, the drawer appears, and its
author input receives focus. There are no sleeps, timer increases, rescue
clicks, retries, or timing phase searches. A reachable controlled mechanism does
not retrospectively prove the hosted initiating cause.

## Correction

The existing owner gains exactly three readiness assertions: toggle
`aria-expanded=true`, opener visibility, and opener focus after its existing
focus action. Every previous action, privacy/keyboard/request assertion and
assertion deadline remains. The UI-owner correction changes no production, dependency, authorization,
workflow or retry configuration; the combined CI repair separately selects the
official Trivy database source. Removing the three added
assertions reconstructs the complete EA7 owner byte for byte (SHA-256 recorded
in the local preservation receipt).

## Validation

On matching installed frontend dependencies (Node 24.19.0, pinned npm 11.17.0,
Angular core 22.2.3, build 22.2.4, fast-uri 3.1.9), the EA7 production build
passed. The fixed browser control's initial execution passed both cases. Its
closed receipt SHA-256 is
`9da44ec68577fbd373b8a56b7a7b0f36fe1796962f789d3d90d27cc8a4b74fef`.
That initial producer preceded additive source/script identity and explicit
truncation/fixed-error receipt hardening; final producer controls require their
own execution record rather than inheriting that receipt.

The strengthened complete search owner passed 8 cases with 4 existing profile
skips, zero failures and zero retries on Chromium 156 / Playwright 1.64.1.
Its existing finite mobile observation completed with 84 untruncated events,
classification UNKNOWN, SHA-256
`bf8bc661460dc6deb7a911337c8430837cc091015a6ca453bc1e1e66f075a868`.
All 10 existing search observation Node controls passed. Script syntax and diff
checks passed. The standalone control is not selected automatically as an owner
and must be explicitly invoked against a local build:

```sh
PLAYWRIGHT_BROWSERS_PATH=/workspace/native-scroll-browser \
SEARCH_OPENER_CONTROL_OUTPUT=/absolute/unique/control-receipt.json \
node tests/ui/search-opener-render-readiness.browser-control.mjs
```

The control accepts loopback HTTP only, emits at most 32 events per case and
16 KiB total, and records only structural booleans/enums and source digests.
No user text, query, URLs, cookies, response bodies or business identities are
recorded. No PostgreSQL, real transport, physical device, Japanese IME, release
approval or held-candidate acceptance credit is claimed.

## Producer and served-build provenance

The receipt labels the EA7 source/tree constants as `expectedSubjectSourceSha`
and `expectedSubjectFrontendTree`; they are expectations, not measurements of
HTTP-served assets. The script derives its Git working directory from its own
file location, requires the producer's frontend tree to equal the expected
snapshot, and rejects tracked dirty files or a changed HEAD/script during the
run. These checks bind the control producer and source tree only. The caller
must independently bind the served build to that source (for this preparation,
the fresh EA7 build and uniquely owned local server); no served-binary
attestation is inferred from the constants. The two browser control bodies are
unchanged by this provenance-only follow-up.
