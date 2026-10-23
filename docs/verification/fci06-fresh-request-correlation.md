# FCI-06 fresh-request authorization probe

PR #1327's original Functional collaboration run `39932755002` remains FAIL.
At test merge `5017b0e9ca342ea943988a5fda636efb2b2c00d8`, the restricted Thread
probe expected HTTP 400 but observed 200. The notification owner passed; the
message owner failed once with zero retries/skips.

Sanitized diagnostics show overlapping Thread requests. An earlier authorized
request's response can satisfy the previous method/path-only response waiter.
That is a demonstrated correlation weakness, not proof that it caused this
particular 200. The retained evidence excludes native traces and API-login
traffic; actual request actor binding cannot be reconstructed from it.

The repair verifies that login returns a distinct restricted actor and that the
shared browser/API cookie session currently resolves to that actor. It then
observes a newly emitted matching Request before clicking the retained Thread
tab, and awaits that exact Request's response. An older in-flight response cannot
substitute. Missing/aborted responses fail; fresh HTTP 200 still fails the existing
400 assertion. All exact denial, protected-body, draft/cache and UI-purge
assertions remain unchanged. No response mocking, retries or timeout changes.

## Local validation

- Four deterministic controls pass: late old 200 versus new restricted response,
  unchanged fresh 200/no retry, absent response, and request/action failure.
- The helper passes strict TypeScript checking against actual Playwright 1.64.1
  declarations using locked TypeScript 6.0.3. The complete owner selection retains
  six pre-existing diagnostics also reproduced on Main, with zero new diagnostics;
  this is not a claim of a globally clean owner typecheck.
- Owner TypeScript syntax passes. The initial broader Node helper run had 52
  passes and one Bash-resolution failure on Windows. The failed FCI-07 selection
  subsequently passes all three controls with Git Bash explicitly selected;
  no source assertion was changed for that environment correction.

Fresh real-backend Functional collaboration on this candidate must still prove
the restricted request receives 400 and clears private UI. The old failure and
uncertain cause remain retained until that stronger probe executes. Local helper
controls alone do not qualify the product, resulting Main or pilot release.
