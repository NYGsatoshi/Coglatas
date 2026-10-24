# Pilot SEC-16 / SEC-17 / Thread reauthorization integration

Status: **REVIEW CANDIDATE / NEW COMBINED HEAD NOT YET QUALIFIED / RELEASE NO_GO**.
This is not an authorization to admit student accounts or deploy to a school HTTPS target.

## Bound source and product scope

Base Product Main: `0ad713dad8bb783420e7fdc5cae0b5dbc24e317d`,
normally merged Dependabot security patch #1385 after the offline synthetic
FK-order advisor #1369. The current candidate preserves the ancestry of two
independently reviewed source revisions:

- [#1381](https://github.com/NYGsatoshi/Coglatas/pull/1381) HEAD
  `5ff029c794640e53708066eb5266d7813ae1c614`:
  three *actual PostgreSQL-hosted* test methods from source candidates
  #1368 (Direct self-leave HTTP and established/reconnecting SignalR),
  #1370 (persisted File eligibility and retained actor-bound grants),
  #1371 (Workspace folder placement and retained grant/byte/scope checks),
  plus two source-bound verification documents.
- [#1366](https://github.com/NYGsatoshi/Coglatas/pull/1366) HEAD
  `f2c69b75d35c91848e6d9794d17e536cf4b86f24`:
  one Angular `HttpTestingController` pending-GET / catch-up / fresh
  authorization Thread test and its original verification document.
- The current Main already includes source-unchanged production code from
  #1380 and the no-security-assertion-relaxation Qodana test repairs from
  #1382. These are **not introduced again** by this integration.

The expected diff is **eight files**: four original PostgreSQL/Angular
test sources and four evidence/verification Markdown files. No product
runtime code, API DTO, auth policy, database DDL, migration, RLS, fixture
admission, scanner threshold, test timeout/retry, CI workflow or package
manifest is modified. Original Git PR parents remain reachable in the
merge commit DAG; each relevant added file blob is unchanged.

## Qualification and open failures

The predecessor SEC-16/17 [PR CI #40087309712](https://github.com/NYGsatoshi/Coglatas/actions/runs/40087309712)
and Thread [PR CI #40087448073](https://github.com/NYGsatoshi/Coglatas/actions/runs/40087448073)
both completed first-attempt **16/16 PASS** on their distinct original
sources. Separate predecessor #1381 execution
[PR CI #40077918899](https://github.com/NYGsatoshi/Coglatas/actions/runs/40077918899)
explicitly ran all three current-contract PostgreSQL/HTTP/SignalR facts
with nonzero durations (28s/10s/6s). Earlier success does **not** qualify
this new combined source or resulting Main.

- [ ] Inspect exact current integration diff/tree/parents and no protected bytes.
- [ ] Current-head ordinary PR CI, exact source identity, first attempt and all
      six strict Main required checks. Existing masked/skipped jobs and any
      source-specific failure must remain visible.
- [ ] Confirm each of the three actual PostgreSQL+HTTP/SignalR facts is
      executed and passed (not skipped by an absent DB fixture).
- [ ] Confirm the new Angular authorized Thread revalidation test executes and
      passes with the real current test suite. This is not hosted intermittent
      root-cause evidence.
- [ ] Independent review before normal merge and, separately, first-attempt
      resulting-Main Full Functional, Security, Qodana Cloud and Community.
- [ ] Keep the original Fast collaboration draft-loss first failure [#1360](https://github.com/NYGsatoshi/Coglatas/issues/1360)
      and intermittent authorized Thread [#1365](https://github.com/NYGsatoshi/Coglatas/issues/1365)
      OPEN until the causes, not just a later PASS, are established.

Main `ba7bb...` / `aac4...` Qodana Cloud and Community historical
failures are retained. The two-line File privacy nested-pattern syntax fix
in [#1384](https://github.com/NYGsatoshi/Coglatas/pull/1384) is **not**
part of this candidate: the original #1384 Fast Messaging first attempt
failed at the second-root empty draft; a diagnostic job rerun cannot erase it.

## Owner and public-release boundaries

Held #1320 (cross-session), #1326 (Realtime authority), #1361 (local composer
identity), #1367 (Functional timing), #1378 (diagnostic-only integration),
#1379 (physical backup strategy), private SEC-ARCH formal role/grant/policy
mapping and product RLS activation, approved stable school HTTPS/SSH origin,
strict restored-app/rollback gate, physical IME, school operator/privacy
approval and real 17-flow HTTPS acceptance remain separately unqualified.

**Owner HOLD BOTH / RELEASE NO_GO.** This candidate neither changes a
formal security decision nor approves target use, school accounts or release.
