# AQ roster transport regression for #2304

The [original ordinary security run](https://github.com/NYGsatoshi/Coglatas/actions/runs/46096278762/job/140828838297) reported a protected roster import-preview HTTP 500. The subsequent [full ordinary CI security lane](https://github.com/NYGsatoshi/Coglatas/actions/runs/46100672879/job/140842817777) succeeded for all three roles using different generated inputs. That is not a reproduction or resolution of #2304.

Add a hosted regression with ordinary bounded synthetic malformed form fixtures (inconsistent framing, incomplete form and missing required file), a valid CSV control before and after, original authenticated and CSRF-protected route, expected HTTP 400 and durable no-write/version invariants. Use the actual existing host and PostgreSQL scenario. Do not copy the original scanner request, credentials or internal exception details. No production exception mapping, logging/redaction, request-size limit, authz, fuzz seed, scanner or assertion policy changes.

Route the changed hosted test into the full native .NET baseline and four-shard proof. If the fixture exposes a failure, investigate the precise input parsing or controller boundary before a narrowly scoped correction; if it passes, do not claim the original intermittent 500 was fixed. Keep #2304 open until root cause and exact acceptance evidence. No Main merge.
