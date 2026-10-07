# Licensed Task source-policy refresh repair

## Authenticated failure

Exact Main `0c25009f9241d2c035dac8b14083746d3ce68dca`, Main CI
[37608948643/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/37608948643/attempts/1),
[job 112752436419](https://github.com/NYGsatoshi/Coglatas/actions/runs/37608948643/job/112752436419),
failed `FUNC-TASK-001 / STEP-06`. Its awaited request is
`PUT /api/tasks/{taskId}/execution-scope-override`, after selecting a complete
Task override with ProjectFile Allow and Web/WebSite/ConnectedApp Exclude.
The owner timeout remains 180000ms.

The downloaded [artifact 11477276161](https://github.com/NYGsatoshi/Coglatas/actions/runs/37608948643/artifacts/11477276161)
has SHA-256 `bad3e43b396ba7460d77ca0fa49e3ad27bb5f529f17290033900a9ebcee1947a`,
matching both GitHub metadata and the upload log. It contains the owner JUnit
result and error-context snapshot. Original bytes and logs are retained in the
executor's evidence directory. No trace, HAR, request chronology or browser
observation is present in this archive.

## Causal table

| Stage | Expected | Observed | Evidence | Classification |
|---|---|---|---|---|
| UI action | Select override and ProjectFile Allow, then save | The test reached the response wait after clicking save; final UI shows inherit | Owner STEP-06; JUnit; error snapshot | Wrong-method hypothesis supported; exact event timing UNKNOWN |
| Angular dispatch | HttpClient PUT with complete policy | Snapshot shows feedback written exclusively by a validated DELETE success callback | `saveTaskScope` and snapshot | DELETE completion inferred from the exclusive writer |
| Authoritative refresh | Refresh policy while retaining unchanged-version unsaved intent | `applyScope` overwrites all editor fields and changes override to inherit | Actual-component regressions reproduce DELETE instead of PUT for both TaskChanged and ProjectChanged | SOURCE_DEFECT confirmed |
| Route/store/reload | Preserve selected Task and Project after full navigation and Task restoration | Same Task and Project remain mounted; matching invalidations call `loadScope` | Owner STEP-04/05; snapshot; component source | Specific historical initiating event UNKNOWN |
| Backend endpoint | Versioned authorized PUT persists the override | DELETE and PUT have separate controller/use-case routes; the snapshot feedback requires a mapped successful DELETE response | `TaskExecutionController`; `TaskExecutionScopeService`; component callback | No demonstrated backend failure |
| Auth/capability/context | Current authenticated tenant and authorized Workspace/Project/Task with canManage | Earlier authority checks completed; management controls remain visible | STEP-02 and error snapshot | No denial demonstrated; original request headers unavailable |
| File-source state | Complete override enables ProjectFile only | Effective Project-default state remains Exclude in the failure snapshot | Error snapshot | Unsaved selection was not the final effective policy |
| SignalR | Matching metadata invalidations trigger fresh reads | Connected UI; Task/Project handlers invoke scope reload and reset the draft | Snapshot; handler; deterministic tests | Proven source path; exact delivered historical event UNKNOWN |
| Cancellation/abort | Preserve security/route cancellation | No original abort evidence; regression tests verify cancellation at authorization/protected-scope boundaries | Archive inventory; focused tests | Historical abort UNKNOWN; intentional cancellation preserved |
| Promise lifecycle | The awaited PUT response resolves and is asserted | No matching PUT response resolves before timeout; component can instead dispatch DELETE after refresh | JUnit and red tests | Reproducible wrong-method dispatch; timeout is not classified transient |

The original workflow remains FAIL. The exact historical invalidation event
cannot be recovered from the retained archive. The source defect and repair
are independently demonstrated; new hosted Licensed acceptance is required.

## Repair and regression proof

Only routine realtime refreshes may retain the Task editor, and only when the
authoritative origin, Project-default version, Task-override version,
management permission and authorized source inventory are unchanged. The
server projection still refreshes. Policy/inventory/permission changes replace
the editor; explicit reload, identity changes and successful saves initialize
it authoritatively. Security/protected-scope clearing cancels requests and
clears the draft. No backend authorization, persistence or version contract
changes.

Two actual-component UI regressions select the override radio and ProjectFile
Allow, deliver a same-policy Task/Project invalidation, and submit the form.
Before the source fix both dispatch DELETE where PUT is required. Two boundary
regressions also demonstrate the previously retained latent draft after clear.
After the repair the focused suite passes 23/23, with zero skips, including
policy-version changes, management revocation and dispatched-request
cancellation. Full Angular tests pass 1144/1144 in 122 files. The final focused
suite also verifies the form submit after removing test non-null assertions.

The local Windows production build process exited with `0xC0000005` before a
result, so it is not a build PASS. Hosted build, static analysis, backend,
protected checks, Functional and Licensed acceptance on the new SHA remain
required before normal merge. No hosted unchanged failure was rerun.

The live `frontend-static-analysis.yml` explicitly defers legacy JS/TS lint
enforcement and emits a stable audit marker. That marker is not credited as
executed lint. Local complete inspection and a fresh final changed-file replay
retain the original debt ceilings, with no new active ESLint debt and no fatal
parser errors; unchanged Stylelint has no debt-ceiling regression. Test-only
helper definitions are ordered before callers, and setup/assertion helpers keep
the new regressions within the existing statement ceiling. No lint rule,
baseline or suppression directive is changed.

## Independent PR and performance boundaries

#1108 changes only MessagingFacade catch-up settlement and Message tests. The
Task scope component owns separate subscriptions/generations and calls
HttpClient directly. No shared primitive proves #1108 relevant to STEP-06;
it is not credited as this repair and remains for final audited disposition.

The consumed Medium assignment campaign and its manifest remain immutable.
No capture, replacement declaration, approval, baseline enrollment, threshold,
required check, ruleset or CODEOWNERS change is included. Avalonia/ProjectIDE
implementation remains deferred.
