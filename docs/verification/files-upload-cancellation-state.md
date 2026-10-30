# Files upload cancellation state and scope invalidation

This is an isolated adapter repair while the production Files upload surface
remains disabled. Cancelling a pending upload previously kept canUpload=false
because eligibility was computed from its old pending presentation state.
Cancellation also failed to reconcile a File change deferred during that upload.
Forced inventory reauthorization advanced the generation without aborting or
releasing pending upload operations, which could then suppress future refreshes.

Eligibility now follows remaining pending operations and the current page's
Workspace. Cancellation reconciles deferred refresh only for the captured upload
Workspace while it is still current. Page invalidation aborts pending requests,
releases their Files and queue presentation, and clears obsolete refresh timers;
the fresh authorized inventory owns subsequent presentation. There is no automatic
POST replay and cancelled items do not become explicit-retry candidates.

Nine new regression cases exercise pending/progress cancellation and next
selection, deferred File-change refresh, a changed-Workspace negative control,
future refresh after invalidation, and pending/progress reauthorization in both
the same and a different Workspace. On Mainc9c16772, unchanged tests produce
40 passes and eight failures; the progress-cancellation positive control passes.
The repaired Files suite passes 202 assertions across 19 spec files, zero pending.

The repository-pinned Linux Node image e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413
(Node24.22.2), npm11.17.0 and unchanged lockfile passed the ordinary production
build and theme sanitizer. Existing bundle/style budget warnings remain; no budget
was relaxed. Two earlier Windows Node24.22.1 build attempts terminated after
Building: exit1 without a diagnostic, then captured native exit -1073741819
(0xC0000005). Their root cause is UNKNOWN; Linux success does not waive them.

HttpTestingController cancellation is adapter evidence, not proof that an already
committed server upload was rolled back. There is no actual browser UI upload,
scanner qualification, persistence idempotency or Pilot B acceptance claim.
The page component/template are unchanged, including uploadsAvailable=false and
no enabled file input. FileSharing and all formal Owner HOLDs remain unchanged.
Published-head CI, official Sonar gate and independent review are separate gates.
STUDENT_COUNCIL_PILOT=RELEASE_NO_GO.