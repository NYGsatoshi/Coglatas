# Group roster immediate boundary keys

## Scope and historical limits

[Issue #1346](https://github.com/NYGsatoshi/Coglatas/issues/1346) remains open.
Its retained first-attempt mobile Home failures, later retry successes and
UNKNOWN historical initiating cause are unchanged. The earlier unpublished
smooth-scroll candidate remains BLOCKED; this change does not reuse it.

This correction gives the directly focused roster an immediate Home/End
boundary operation, cancelling the browser's default animated boundary scroll.
An opposite key therefore does not depend on completion of a previous native
animation. Child controls, modified keys, IME composition and other navigation
keys retain their native behavior. No focus movement, membership permission,
protected-state retention or server command changes.

## Local source-bound comparison

Baseline product source is exact Main
`42ba000d9d4fc682165cb65de0a1bb61ad407481`. Pinned clean dependencies were
installed with npm 11.17.0; Node 24.19.0, Vitest 5.0.5, Playwright 1.64.1 and
its Chromium 156.0.8078.4/revision 1248 were used.

The new native-browser boundary control ran before modifying product source:
both desktop and Pixel 5 profiles failed on attempt 0, with zero retries. The
first completed End key left `scrollTop` at 165/228 respectively, with maximum
1894. This demonstrates the pending native animation. Since the assertion
failed at that first End, this RED **does not reproduce the complete historical
Home reversal failure** or establish its initiating cause. The finite native
scroll experiments already retained in #1346 remain separate evidence.

After the correction:

- Production build passed, with existing style/bundle warnings.
- Focused Angular Group and keyboard-scope units: 13/13 PASS, no skips/retries.
- Native desktop/mobile browser selection: 6/6 PASS, zero retries. This includes
  the unchanged ordinary ReadOnly keyboard owner and axe checks plus four rapid
  native End/Home keys per profile under the new immediate boundary rule.

```sh
npm --prefix frontend test -- \
  --include=src/app/features/messaging/group-member-panel/group-member-panel.component.spec.ts \
  --include=src/app/features/messaging/group-dm.spec.ts
npm --prefix frontend run build
PLAYWRIGHT_SKIP_BUILD=1 npx playwright test tests/ui/student-council-group-dm.spec.ts \
  --grep 'honors rapid focused roster|ordinary ReadOnly long roster' \
  --workers=1 --retries=0 --reporter=list
```

Removing only the new browser test reconstructs the entire existing owner file
byte for byte (SHA-256
`8c25002f148c4b9e6a70e47752d50cbc874c94dabbe06411cabe70dac755b22b`).
The original exact-zero Home assertion, five-second deadline and configured
suite retry policy are preserved. No retry was used for local qualification.

The local comparison is mocked-HTTP frontend evidence, not PostgreSQL,
real-backend, physical-device, Japanese IME or complete release acceptance.
Independent review, current-head PR CI and resulting-Main qualification remain
required. No Owner HOLD, security/RLS activation, deployment, school-user
admission or RELEASE_NO_GO disposition is changed.
