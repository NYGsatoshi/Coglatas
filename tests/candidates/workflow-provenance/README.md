# Executing workflow provenance for ProjectIDE advisories

Issue [#2089](https://github.com/NYGsatoshi/Coglatas/issues/2089) repairs the
provenance producer for five already merged independent qualification cohorts.
Their Product commits, candidate assertions and rosters, composition runners,
native-result admission controls and assurance limits stay independently pinned.

## Source identities

An explicit PR-head checkout identifies the Test source. It does not identify
the workflow definition GitHub executed. The jobs obtain `WORKFLOW_HEAD` from
`github.workflow_sha` and `WORKFLOW_REF` from `github.workflow_ref`, separately
from `TEST_HEAD` and the immutable Product commit. See GitHub's
[context reference](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts)
and [pull request event semantics](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request).

Before native execution, `verify_workflow_provenance.py`:

1. Requires canonical full commit identities and the exact repository, workflow
   path and `refs/pull/<number>/merge` reference. The checkout must still be the
   requested Test head and contain the event's exact Security base.
   Every Git subprocess removes inherited `GIT_*` environment variables
   case-insensitively and disables replacement objects, so environment-directed
   repositories, worktrees, indexes and config cannot mask the actual checkout.
2. Checks the pinned verifier and control blobs against both immutable Git
   objects and their checked-out bytes. Both files and the workflow must be
   regular Git mode `100644` blobs.
3. Reads the immutable executing workflow commit. If that object is absent,
   fetches only that SHA from the fixed repository URL, then checks its type and
   resolved commit identity. It never substitutes `HEAD` or a moving merge ref.
4. Requires exactly `[Security base, Test head]` as the workflow merge parents,
   the same workflow blob as the reviewed Test source, and equal complete
   workflow/Test trees. A changed base or other unreviewed tree content fails
   qualification. Update the Test branch to the new base and obtain a fresh run.
5. Extends only the new run's preliminary context after all checks pass. It
   refuses a context with existing workflow fields, a different Test source or
   a missing/promoted qualification flag. Refusal leaves its input bytes intact.

The context retains actual workflow SHA/ref/path/mode/blob/tree/parents, exact
base and Test tree, and verifier/control identities. This validates a source
relationship; it is not a signature or a formal reviewer approval. An independent
auditor must still compare original GitHub run/job/artifact metadata, original
ZIP bytes, raw native discovery/results and immutable Git objects.

The separate `provenance-controls.log` records 25 disposable-Git controls. They
exercise actual merge objects, changed base/tree/workflow content, absent paths,
executable mode, wrong source/ref/repository/PR, malformed identities, missing
immutable objects, exact-SHA fetch, changed verifier/control bytes, foreign Git
environment redirection with a wrong-checkout refusal and valid-checkout positive,
and refusal
to restamp old contexts. Fetch controls replace only the network transport with
a local bare repository or an explicit failure; they do not invent native
Product results. These 25 controls add **zero native execution credit**.

## Original evidence and fresh repair scope

| Original merged PR | Cohort | Original run | Original metadata limitation |
| --- | --- | --- | --- |
| #2078 | Regional host | [44240707784](https://github.com/NYGsatoshi/Coglatas/actions/runs/44240707784) | Executing workflow SHA/ref absent. |
| #2083 | Reference cache | [44243945057](https://github.com/NYGsatoshi/Coglatas/actions/runs/44243945057) | Executing workflow SHA/ref absent from all four corrected/historical jobs. |
| #2085 | Semantic index ViewModel | [44245857503](https://github.com/NYGsatoshi/Coglatas/actions/runs/44245857503) | Executing workflow SHA/ref absent. |
| #2086 | Semantic index Web | [44248802077](https://github.com/NYGsatoshi/Coglatas/actions/runs/44248802077) | `workflowHead` repeated Test head; workflow blob came from that checkout. |
| #2087 | Project Test Web | [44251725347](https://github.com/NYGsatoshi/Coglatas/actions/runs/44251725347) | Same Test-head alias and checkout-based blob. |

Original source-specific results, artifacts and hashes remain unchanged. Their
equal reviewed/virtual-merge trees corroborated content but did not capture the
missing historical workflow context. Fresh repaired runs establish their own
executing workflow identity and must not retroactively restamp those originals.

All five changed advisories require fresh Ubuntu 24.04 and Windows execution.
Reference cache retains both corrected and historical modes: 24 corrected
passing facts, and exactly six intended historical failures plus one separate
positive fact, on each OS. Regional host and ViewModel each retain 24 facts;
semantic Web retains 24 Angular cases; Project Test Web retains 18 Angular cases.
The existing cohort-specific controls and native admission remain unchanged.
Changes to this shared verifier directory trigger all five workflows.

Current source must pass its ordinary CI and independently audited original
evidence before merge. This README describes the contract; it does not claim
that a future source or configured workflow has executed successfully.

## Assurance holds

`requiredCheckQualified`, `independentReviewComplete`, `nativeUiQualified`,
`productionQualified`, `trustQualified` and `releaseQualified` remain false.
Keep broader #1927, #1930, #1938, #1945 and #1966 OPEN, #1144 BLOCKED,
#1128 SUSPENDED / NOT_EVALUATED and Release NO_GO. This bounded workflow-provenance
Issue may close after its own fresh source-specific acceptance is complete.
Runtime authorization, Native UI, durable storage, Artifact Trust and release
approval remain separate responsibilities.
