# Existing LegacyNameGuard controls in native Python measurement

The common pre-credential Sonar Python producer also runs the complete existing
`scripts/ci/test-coglatas-rename.py` suite. Its unchanged operational subject,
`scripts/ci/check-coglatas-rename.py`, is added explicitly to the existing subject
set and its exact-scope assertion. All previous subjects and assertions remain.

The existing process-aware bootstrap, pinned coverage tool, branch/parallel
settings, raw native shards, single combine and single XML import remain in use.
Test bodies remain outside measurement. Source bytes, zero-hit lines and native
branch accounting are retained; no report rewriting, exclusion or threshold
change is introduced. Legacy naming policy and the separate workflow that runs
these controls are unchanged. This does not promote held governance, Performance
coverage, repository-Git hardening or finding disposition work.

Configuration and local controls do not establish hosted collection or an
Overall Coverage improvement. Exact-head CI, original official PR Gate and
independent review are required before integration. Qualify resulting Main,
original reports/source mapping, denominator and actual line/condition delta
separately in #615. No production, physical acceptance or Release GO is granted.
