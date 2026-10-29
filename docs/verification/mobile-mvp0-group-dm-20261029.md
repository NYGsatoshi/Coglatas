# Independent actual-backend MVP0 Mobile qualification

Tracks #1513. Base Main: `13574a971b10cf10a41716141cfe821fb6f68c04`.

The existing mandatory MVP0 test exercises the DM UI and the real Group creation,
messages, retained history, four synthetic actors, read/unread isolation, removal,
re-entry, first admission, leave and Workspace revocation. Its required Desktop
execution and all existing assertions remain unchanged. It is not sufficient
proof that these flows work at a narrow native Mobile viewport: the existing
Group helper created its other actor contexts with Desktop defaults.

The opt-in Licensed Real Backend Acceptance suite `mobile-mvp0` runs exactly that
one unchanged scenario in a separate Chromium 320x800 touch project, one worker,
retry zero, with the existing 120-second scenario timeout. All four Group actors
use separate authentication contexts with the same device geometry; each reads
and asserts its actual viewport, screen, device scale and touch points. A closed
one-case manifest and JUnit verification reject skipped, failed, missing or extra
cases. The normal Desktop guard, canonical Mobile four-owner/23-step contract,
FCI06 Default C and thirteen configured skips remain intact.

The wrapper requires a clean exact TARGET_SHA, attempt one and a dedicated build.
Only source/tree/run/attempt, image digest, case count and four actor geometry
receipts are uploaded. Trace/video/screenshots and generic failure artifact upload
are disabled only for this new opt-in profile. No protected message body, cookie
or authentication value is included in its published receipts. Post-build image
inspection is not claimed as an independent before/after continuity receipt.

Local Node guards cover valid geometry, missing opt-in, different/canonical
projects, retry, wider/non-touch/nonmobile contexts, and absent/extra/Desktop
actor receipts. Local Docker execution is NOT RUN because the available local
Docker/disk environment is unqualified. Actual hosted first-attempt results,
independent review and Main validation remain pending until recorded in #1513
and #615. No feature, permission, schema, formal security or release contract is
changed. No physical-device/IME or HTTPS/student admission claim is made.

`STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.
