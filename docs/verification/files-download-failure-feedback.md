# Files download failure feedback

Related: Student Council Pilot #615. Existing download grants and authorization
remain authoritative; UI intake remains disabled.

Malformed UTF-8 `filename*` parameters previously threw in the HTTP success
callback, leaving the download operation pending. Decode failures now use the
ordinary filename or existing localized fallback. Files and Task attachment
callbacks also report failure when no Blob can be saved, release the operation
and permit a fresh authorized retry. Previously they reported success despite
`saveBlob` returning false. Failed saves never enter Continue Working history.

Three malformed encoding cases and both download surfaces have deterministic
regressions. On the original source all five new cases failed. The corrected
Windows runner reported all 43 owning tests passed but did not terminate; that
runner was stopped and its log retained rather than claiming process success.
Independent pinned Linux execution and current-head hosted CI remain required.

No response body, cookie, token, grant or private file content is logged. No API,
policy, schema, scanner, FCI acceptance, timeout, retry or Owner HOLD changes.
Mock HTTP unit tests do not establish actual-backend byte or UI acceptance.
