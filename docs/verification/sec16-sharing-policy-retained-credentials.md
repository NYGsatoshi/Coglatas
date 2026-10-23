# SEC-16 sharing-policy PUT and retained download credentials

This bounded test-only follow-up to #1349 uses the current contract. It does not
claim that sharing-policy PUT previously lacked tests. Existing InMemory,
selected-policy composed-PostgreSQL and management/version controls remain
credited separately. The new gap is retained opaque credentials across actual
policy changes in the ordinary Web host with real PostgreSQL and physical bytes.

`FileSharingPolicyRetainedCredentialHostedHttpTests.cs` extends the existing
partial `FileDownloadGrantLifecycleHostedHttpTests` class without editing its
original nine lifecycle facts or the four Attachment actor/resource facts from
#1350. It reuses their actual compiled Web entry point, independent production
cookie sessions, current-actor observations, CSRF, synthetic direct Workspace
upload, physical-byte, protected-state and safe-envelope helpers. It adds no
service replacement, test-only RLS policy or intake configuration.

## Two reader families

- `FileObjectPolicyPutInvalidatesRetainedMemberAndOwnerCredentialsAcrossRestoration`
  uses FileObject issuance and credential consumption.
- `AttachmentPolicyPutInvalidatesRetainedMemberAndOwnerCredentialsAcrossRestoration`
  uses Attachment issuance and consumption. This family has a meaningful masking
  distinction: denied fresh issuance returns 404 with
  `FILE_DOWNLOAD_GRANT_NOT_FOUND`, while FileObject issuance and both consumption
  and direct-download families retain their current 400 contracts.

Each fact starts from a newly uploaded private attachment. The owner performs
an actual policy PUT to Workspace, and independently authenticated Member and
Owner credentials both read the exact physical bytes. No explicit recipient
grant exists in either the HTTP projection or persisted table, so recipient
authority cannot mask a policy transition.

A separate missing-CSRF PUT returns 403 and preserves the selected protected
state. The actual authorized Workspace-to-Private PUT advances SharingVersion
once. The old Member credential fails under current authorization; the old Owner
credential fails under its old policy stamp even though the Owner remains a
current reader. Fresh Member issuance and direct download fail under their
route-specific contracts. The Member's session remains valid. A freshly issued
Owner credential still reads the physical bytes while the file is private.

After the authorized Private-to-Workspace PUT advances the version once more,
both original credentials remain invalid. Newly issued Member and Owner
credentials read the same bytes, and the new Member credential is reusable.
These opaque POST-body credentials are neither one-use tokens nor signed URLs.
No new sharing, role, revocation or URL contract is introduced.

Every denied operation preserves the selected File/Attachment/sharing/download-
grant state, including successful-use counts. The existing digest comparison
does not print the protected snapshot. Denial envelopes remain bounded and
exclude protected markers, redirects and download filenames. Member denial
audit requires `current_authorization_failed` then `policy_changed`; both old
Owner attempts require `policy_changed`. Exactly three authorized
`FileSharingChanged` policy mutations are observed. Audit checks exclude token,
content and storage details. CSRF denial is not counted as authority evidence.

## Qualification and limits

Future qualification must record exact committed source and a fresh Release
build; all seven canonical/test-loaded assembly hashes and PE source versions;
the original thirteen plus two new named facts, with fifteen executed/passed
and zero failed/not-executed; real PostgreSQL version, effective diagnostic
privacy settings and loopback-only disposable target; and owned cleanup.
Initial diagnostic results remain distinct. Configured tests are not execution
evidence, and local candidate execution is not hosted or resulting-Main proof.

This slice does not add a role matrix, placement continuity, historical version
references, selection-snapshot batch coverage or pre-commit concurrency claims.
Those residual current-contract cells remain independent. It does not enable
pilot intake, approve formal mapping/RLS, change held #1320/#1326, resolve
#1342/#1346, waive either Owner HOLD or establish release readiness. Public
pilot verdict remains NO_GO.
