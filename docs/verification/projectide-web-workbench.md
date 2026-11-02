# Angular ProjectIDE Workbench preview

This candidate implements a read-only synthetic vertical slice at
`/app/projectide/preview`: context selection, Source, fixed Compiler artifact
loading, Problems, Inspector and stable-ID source navigation. It introduces no
runtime Compiler API, production Project Source access, editing, Proposal or
Merge operation. Runtime Compile is disabled with an explanation.

## Audited baseline and authority

The repository checkout is kept separate. Work uses independent
`.codex/worktrees/projectide-web-*` directories; the original checkout
and existing worktrees are retained. Remote observations on 2026-11-01:

| Reference | Observed SHA |
| --- | --- |
| Main | eb18fb3c5b82c05ca4cdc8b68b59276732eb2fef |
| ProjectIDE integration | 158ab153551184cede99c85ce6476dd6e87b6767 |
| Core Product #1628 | ddb456dbf2eca1b005a8aac5ffb1ca8861b73b5d |
| Core Test #1629 | 5bd53a614b9cfe27de53cfa2b7901a18b95aa36d |
| SecurityTestImplimation | b18939f9104d52ae33e32a7f9b15a6e1b52892f1 |

No existing ProjectIDE Web workbench branch/PR was found in the initial remote
search. Product branches from the exact Core Product SHA, never its Combined
commit; its Draft PR targets #1628's branch. Independent Tests extend #1629's
SecurityTestImplimation-derived chain. Core Slices 2–8 remain unmerged/review
pending. Their historical 707/12 results qualify only their recorded old pins.

The task reviewed #798, #888, #905, #782, #814, #766, #906, #908, #1030,
#793, #794, #795, #1144 and #1128. Spec PR #300 was observed **merged** at
16066f182beef0cd7ce57aa23849922f634d82c7, unlike historical Issue wording.
Parallel Web/Windows direction does not satisfy Windows' independent gate.

## Existing Angular and API seams

`frontend` is active. Package declarations say Angular 22.2.4; the existing
lockfile installation resolves Angular Core 22.2.4. No package or lockfile was
changed. Routing uses an authenticated AppShell and lazy feature components.
`/projects`, `/workspaces/:workspaceId/projects` and `/projects/:projectId`
remain unchanged; the authorized Project detail adds only a flag-controlled link
labelled synthetic preview. Table/Kanban/Schedule components and commands remain
owned by their existing facades.

HTTP uses Angular HttpClient and the existing auth/CSRF/error interceptors;
Workspace selection, auth session, tenant state and RealtimeFacade own existing
boundaries. Feature-local signals/facades coexist with NgRx and existing vendor
adapters. Standard controls, canonical theme tokens, the existing I18nService
English/Japanese catalogs, Vitest's Angular builder, Playwright/axe and Storybook
remain in use. No editor dependency was added.

No ProjectIDE Compiler artifact endpoint was found in current Web controllers.
Slice 8's ServerCompilerHost requires an injected transport; it is not an approved
HTTP/auth/jobs implementation. This preview therefore never calls it or invents
an endpoint. Public static fixtures contain synthetic data only. Preview opt-in
does not confer production data access or any server capability.

## Contract ownership

Core `SourceRevisionContext.Branch` owns TenantId/ProjectId/BranchId; committed
context owns RevisionId. CandidateRevisionId and ScenarioId belong to their
distinct candidate/scenario variants, unsupported by this initial committed-only
preview and displayed as not applicable. WorkspaceId belongs to the Web fixture
host context; it is not invented as a Compiler manifest field. CompilerInputId,
input/manifest digest and Source digest come from CompilerInputManifest; result
identity/digest, evidence and diagnostic identity come from CompilerResult.
Display names never substitute for typed identity.

`tools/ProjectIde.Preview` exports Core Data envelopes from deterministic synthetic
Sources using actual PureCoreCompiler and Core declaration readers. Cases include
Complete Pass/Fail, Unknown, Unsupported, NotRun, cancellation, timeout, stale
Pass, another Branch and another Project. NotRun/cancelled/timeout are explicitly
constructed Core envelope fixtures, not reported host executions. The tool uses
the existing Domain project and BCL only. Reproduce with:

```text
dotnet run --project tools/ProjectIde.Preview -c Release -- frontend/public/assets/projectide/preview-v1.json
```

The browser performs view/binding checks, never predicate evaluation or Compiler
semantics. It rejects future contract axes, scope substitutions and inconsistent
result/SourceMap bindings. SourceMap artifacts must belong to the producing result;
unexecuted fixtures carry no produced map. Locations use Core DocumentId,
EntityId/RelationId, RFC 6901 property path and exact captured revision. Optional
spans use UTF-16 offsets and reject split surrogate pairs or invalid ranges. The
viewer shows the canonical read-only buffer and the selected semantic record;
no editor line/column or missing span is fabricated. When no IR/SourceMap was
produced, retained Core semantic locations can still identify Source records;
they do not imply executable semantic closure.

## WorkSurface and safety

The component owns panel visibility, filters and focus. The store owns the frozen
context, result selection and request generation. Inspector follows selection;
closing it clears selection and returns focus to its navigation control. Panel
changes preserve selection; scope/context changes clear results and all targets.
Async reads use AbortSignal and discard late cancelled/obsolete generations.
Session, actor, tenant and active Workspace transitions invalidate state. Existing
RealtimeFacade protected-state invalidation clears it synchronously, including
same-identity authorization loss. It does not retain protected real user data.

`projectide.syntheticPreview` uses the existing runtime flag service, default
false in all builds. It must be explicitly set by a local/browser test harness:

```js
window.__COGLATAS_FEATURE_FLAGS__ = { 'projectide.syntheticPreview': true };
```

The route still requires the existing authenticated session; the fixture consumer
also requires an available Tenant and existing projects:view UI capability.
Those checks are presentation gates only. A future production adapter must
reauthorize exact Tenant/Workspace/Project/Revision server-side and clear data
on invalidation. This task does not certify that future adapter.

Zero Problems is never a success inference. The view independently displays
execution/coverage/verdict/freshness and evidence kind. Only a current, complete,
executed Pass with complete evidence receives the limited PASS-fixture copy.
Stale results remain labelled and cannot navigate onto the desired current Source.

## Verification and limits

New tests are maintained exclusively in the independent Test PR. The Test-side
`projectide_web_isolated.py` composes immutable Product and Test candidates in a
fresh detached worktree, retains all Core tests, rejects ownership violations and
records Product/Test/Combined commits and trees. Supplemental hosted verification
does not replace ordinary required PR checks. Full commands, inventories, outcome
counts, hashes, screenshots and run URLs belong to candidate-bound receipts/PRs;
no old result is evidence for a changed HEAD. Windows screenshots are diagnostic;
Linux hosted artifacts supply the additional browser evidence, without approving
new visual baselines. Failures, skips and review limits remain explicit.

The current Web route inventory explicitly registers the synthetic preview and
its component/store owners (44 current routes). The pinned 38-route Avalonia
snapshot, freeze and target map remain unchanged. The existing internal inventory
test follows the six-route addendum and current 44-route total. This minimal
Product maintenance keeps every assertion and the fixed 38-route snapshots intact.
The independent Test PR retains additional component/owner negative controls;
those are not copied into Product. The existing realpath-escape internal test uses
a directory junction on Windows so its negative assertions execute without elevated
file-symlink privileges; Unix exercises the corresponding directory symlink.
The composer accepts only exact internal inventory/portability maintenance against
its pinned Core baseline and rejects additional Product test edits. Ordinary Product CI must execute and qualify this
change separately; supplemental success does not waive it.

Current bounded status: WEB_WORKBENCH_PREVIEW_IMPLEMENTED candidate; review and
acceptance pending. Runtime Compiler/API/jobs/capability negotiation, real Project
Source authorization, editing/Quick Fix, Table/Board/Gantt semantic Revision
integration, Graph layout and History/Diff projection are not implemented.
Relation records are available read-only in the Source outline and Inspector.
No production Source/API contract is inferred from synthetic fixtures.

Main/integration/SecurityTestImplimation are not pushed or merged by this work.
Core and Web PRs remain open, Draft and unmerged; only their existing feature/test
heads receive authorized continuation commits. No Required Check, protection,
package dependency, DB/RLS,
Shadow/Enforce, Student Council release judgment or #1128 performance assurance
change. Windows/Avalonia real UI remains BLOCKED under #1144. No shared dual-client,
runtime Compiler, security-gate, release or CODEOWNER acceptance is claimed.
