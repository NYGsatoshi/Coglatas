# SEC-06 historical attribution disposition (#1080)

Recommended disposition: `HISTORICAL_ATTRIBUTION_NON_RECONSTRUCTIBLE`.
Security-owner acceptance is required; this record does not close #1080 or
classify the original finding as a false positive.

## Retained original evidence

- Source: `143d9b29139b425fb5d7de2db4782aaa8fae720a`.
- Main run `37263420338/1`, Security job `111615953395`.
- Artifact `11325754508`; original ZIP SHA-256
  `0626d63f60237751a521846a446f59e43d4b1e20c5335712cf35a8589e0c6330`.
- ZAP 2.17.0, pinned image
  `zaproxy/zap-stable:2.17.0@sha256:781a2bdaea47324e7bab583e2263f21d257b0aee61ed51521a5be45f5f5081ef`.
- Rule 10062 / PII Disclosure / High, alpha-owner, GET `/api/files`;
  retained query names are page, pageSize and workspaceId.

The downloaded archive digest was verified. The closed sanitizer projection
retains method, path and parameter names but discards matched literal, JSON
property, raw response, scanner message/session and database state. Original
workflow/job logs contain no matched-field attribution. The fixture provisions
some fixed users, but resource identities and timestamps are generated at
runtime; replaying the same source is not replaying that database/session.
No retained database/session snapshot or random resource-identity state is
available. Multiple response fields/values can map to the same retained alert
projection, so that projection has no unique inverse.

## Bounded reproduction already retained

Diagnostic `37306902788/1`, artifact `11343694196`, SHA-256
`b4f89259c9882a6051398b62b74cf728268ccecdd8a41e469a8b906aa32b2543`,
uses the original source and pinned scanner with an isolated synthetic
UUID-only probe. Its own metadata identifies a UUID-substring observation in
fileObjectId. It explicitly records historical1080Disposition UNDETERMINED
and qualifiesMain false. This demonstrates a possible scanner false positive,
not identity with the historical alert. Further fresh captures cannot recreate
the missing original field/instance binding and receive no historical credit.

## Forward control

After passive scanning, a read-only standalone script inspects each High rule
10062 alert's own ZAP HTTP message inside the ephemeral scanner JVM. It exports
only OpenAPI-owned property names, wildcard array positions, finite value
categories, match kind and explicit unavailable/limit statuses. Dynamic keys,
matched literals, request/response bodies, credentials and tokens are omitted.
The host sanitizer enforces a closed schema and complete High-instance
inventory. Missing/unsafe metadata fails closed. Existing High blocking,
scanner exit handling, rule thresholds, filter scope and all three roles remain
unchanged. The script cannot mark a finding false positive or alter an alert.

## Current validation and residual risk

Exact Main `8481919402f151ad3c5daa2f4690b8478fbc39fb` Security
`37399270068/1` passed all three roles with zero ZAP High. That evidence is
historical after #1097 merged. New Main
`32a17bde8f7f21ab8670265ae75d21ed081e27b9` is being validated by
`37414550908/1`; only completed fresh results can qualify it. Forward-control
candidate validation must be recorded in the PR and Issue before acceptance.

The original High remains a retained unresolved historical event. Neither a
production disclosure nor its absence is established by the lost attribution.
Recommended owner decision: accept the demonstrated non-reconstructibility,
retained event, verified forward attribution control and fresh clean exact-Main
security evidence as the historical investigation disposition. The human
Security owner must decide whether this suffices or whether additional risk
action is required. No automatic risk acceptance or Issue closure is authorized.
