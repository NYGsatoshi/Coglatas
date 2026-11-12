# Task deadline digest hosted lifecycle controls

Existing Worker tests exercise one processing cycle. These additional controls
start and stop the actual BackgroundService, with a controlled scope-acquisition
failure at the cycle boundary. Ordinary failures, timeouts and cancellation that
was not requested by the stopping token produce only the existing safe error
code and leave the worker alive until stopped. Shutdown explicitly rejects a
faulted execution task, even when StopAsync suppresses the original fault.
A repository gate holds a real DI scope inside the active cycle until the test
calls StopAsync; its observed token must cancel, the cycle must exit normally,
and no later scope or error log may appear. Cancellation before startup opens no scope
and leaves the .NET10 BackgroundService execution task cancelled.

Every observation is asserted outside the worker's exception handlers. A log
arrival signal coordinates shutdown without sleeping for the polling period;
five-second bounds detect hangs. The configured poll interval is longer than
the test bound, and no extra cycle may run during shutdown. Original processing,
claim and privacy tests remain unchanged.

The first native run retained9PASS/1FAIL: the new pre-cancelled-start control
incorrectly expected a normally completed execution task. Its scope/log
requirements are unchanged; it now asserts the observed framework cancellation
contract. This was a new-test authoring error, not a Product defect.

Independent review also identified that a completed task could conceal a fault
after shutdown, and that cancelling the StartAsync token did not exercise an
active StopAsync boundary. Both new-test gaps are strengthened above; the
earlier ten-pass run remains historical evidence with those limits.

No Product, authorization, schema, dependency, workflow, coverage import or
quality-policy change is included. These in-process hosted lifecycle controls
do not establish transport delivery, durable recovery, PostgreSQL claims, RLS
applicability or actual Pilot acceptance. Local native counters are cohort
evidence, not an additive prediction of Main coverage.
