# Proposed medium Main32 public-digest declaration

Draft proposal only. First independently review and merge #1099 to Main, then
retarget this stacked declaration to Main and obtain fresh applicable checks.
Do not merge into the schema topic branch. No campaign capture is authorized.

- Campaign: `perf05-medium-20261006-main32-intel8370c-publicdigest`.
- Canonical manifest SHA-256: `0bc9a6442350949d0c5e4b2c46058f9e178d6de4be6e5d70327e3e7083100bf5`.
- Earlier approved Main source: `32a17bde8f7f21ab8670265ae75d21ed081e27b9`.
- Fixed environment digest: `e9c07b1d9fcc3bf7cdc9dd282f44c138d57ce0a0e14ab82db7ca1e5d4ea57443`.
- Created: `2026-10-06T06:00:49Z`; expires: `2026-10-08T06:00:49Z`.
- Fixed authorization allocation: https://github.com/NYGsatoshi/Coglatas/issues/606#issuecomment-6010339834.
  This remains NOT approval; the executor must not approve it.

## Metadata provenance and current scope

Ordinary exact-Main acceptance `37417705008/1`, artifact `11391863479`,
archive SHA-256 `d292a8771e082685a89b788bd5831eefd5d97b8fd63b9e95f14a0a14bce53110`, independently authenticated.
Use only its environment.json identity for declaration: normally assigned
Intel Xeon Platinum 8370C, source-pinned DB fixture v2 and production runtime.
The environment stage precedes DB probing in with-environment.sh. The ordinary
archive also retains its measurements and INVALID duration gate; those remain
ordinary evidence and are never retroactively enrolled into this campaign.
No timing/stability outcome chose this environment; no alternative host was
searched. This declaration fixes the newest ordinary scope before its first
campaign measurement, not before unrelated historical ordinary CI measurements.

Original #1078 targets AMD7763 environment ffc5b0ec7038bc4b1b0c5465ac25f676017d9b849eeab0cfc2f30c0cce781697.
Its ID/digest/reference/expiry/history stay immutable. No campaign was merged
or captured there; no failed/rejected group exists to replace. This separate
initial scope addresses the latest observed environment mismatch, not a rescue.
A human owner chooses and authorizes scope before introduction. Assigned
hardware must independently match the fixed target; all incompatible/partial
groups remain retained and ineligible. Never seek a favorable matching host.

## Bounds and acceptance

Keep nine ordered scenarios, five ordered samples, MAD <= 0.20, maximum three
serial groups, earlyStopPolicy never and earliest eligible stable complete
selection. Contract/comparator/tool identities are unchanged exact source Git
bytes. Capture requires Main schema rollout, valid publication/security,
independent fixed-reference exact-ID/digest authorization and an unexpired first
Main-push declaration. Successful capture still needs a separate independently
reviewed baseline/evidence PR. No baseline approval or product implementation
occurs here. #1046 integration still waits for compatible approved baselines.
