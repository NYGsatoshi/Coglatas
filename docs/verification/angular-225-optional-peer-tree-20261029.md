# Angular 22.2.5 build dependency admission

Current non-held Renovate PR #1441, HEAD3ea22b79e2fe65e658707246c76df0508c68f4f0,
failed first PR CI40776148150/1. Active frontend producer123126131178 and npm
install policy123125983045 reject the installed dependency tree. The separate
scaffold123125847718 reproduces the peer failure. Their existing second install
attempts also fail; no retry-success or browser execution is claimed.

The current lock comparison shows @angular/build22.2.4 has only lmdb as an
optional dependency. Version22.2.5 adds @oxc-parser/binding-wasm32-wasi0.150.0.
That binding pins @emnapi/core/runtime2.0.0-alpha.3 and selects
@napi-rs/wasm-runtime1.2.6, whose declared peers accept ^1.7.1 or2.0.0-beta.2,
not the selected alpha. CI's strict npm ls reports the wasm binding invalid;
the scaffold independently reports both alpha peers invalid. This evidence is
more specific than the older npm CLI/qs regeneration failures.

Exclude exactly22.2.5 for @angular/build and @angular-devkit/build-angular,
which owns the transitive build package. Keep22.2.4, future22.2.6/22.3/23 releases,
all other packages and existing security-alert discovery eligible under their
existing review/major policies. This does not freeze Angular runtime packages,
remove WASM dependencies from a package, approve scripts or skip native profiles.
No dependency or lockfile is edited here; bot-owned artifact generation remains
on the provider. Historical #1441 failures and strict npm ci/tree validation stay
binding. A regenerated candidate needs its own complete checks before merge.

The narrow negated version regex uses the documented Renovate allowedVersions
syntax: https://docs.renovatebot.com/configuration-options/#allowedversions .
Policy mutation controls reject reallowing this exact broken release, freezing
future fixes, applying the exclusion to unrelated packages or disabling build
updates. Local regex boundary controls run with the bundled Node runtime;
Ruby policy controls and provider behavior require hosted verification because
local Ruby is unavailable and Docker is unresponsive after local disk exhaustion.
Configuration alone is not proof that Renovate has regenerated a working PR.

No Product behavior, formal security/Functional contract, required gate,
threshold, Owner hold or release approval changes. Student Council RELEASE_NO_GO.
