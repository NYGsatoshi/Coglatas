# Forms answer admission and normalization tests

This test-only extension covers the existing Application FormService under
the Student Council pilot quality work in #615. It does not replace the
previous Forms authority work in #1678 or change product behavior.

The 45 added cases exercise typed text, Boolean, date, number and choice
normalization through SubmitResponseAsync; malformed or foreign/duplicate
question admission; optional empty answers; inclusive open/close boundaries;
and anonymous DTO masking with duplicate-response rejection. They inspect
the stored answer values and returned DTO, and count audit/save calls to
distinguish successful submission from admission rejection.

All nine original FormServiceTests test bodies and assertions are preserved.
The class becomes partial to share its existing fixture. Its audit and unit
of work doubles retain their completed-task behavior and save result of 1;
the only added behavior is counting calls. No product source, policy,
coverage scope, report producer, threshold or assertion is changed.

The synthetic repository and real Application authorization/service instances
provide unit-level evidence only. These tests do not establish PostgreSQL
transactions, HTTP binding, RLS, delivery or deployed pilot acceptance. An
anonymous response masks identity in its DTO; the existing stored respondent
identity is intentionally retained for duplicate admission. This is not a
claim of database anonymization.

Initial local implementation: 50 PASS, 0 FAIL, 0 SKIP (nine original plus
41 added cases), .NET10.0.403. Committed-source baseline/current collector
comparisons, exact-head CI, official PR Gate and independent review must be
recorded before integration. Local coverage counters are not a projected or
additive Main Sonar improvement. Original report bytes remain unchanged.

Specification baseline: 850a5d9376db11e75235cf55e03ebed4de9c0510.
No Owner HOLD, finding disposition or Release GO is changed.

Independent source inspection6674980499 found no blocking defect and proposed
four test improvements. The follow-up retains every existing assertion, adds
the exact expected validation error in all 16 invalid-answer cases, adds two
dates crossing UTC day boundaries, verifies a non-anonymous identity control,
and rejects a malformed later answer without staging the earlier valid answer.
The expanded 54-case cohort requires a fresh committed-source execution; the
initial 50-case results above remain historical evidence.
