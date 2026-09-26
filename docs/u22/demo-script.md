# U-22 2026 three-minute demo script

This script demonstrates the bounded U-22 vertical slice truthfully. Rehearse
it only against the frozen submission SHA. If the test-only demo fixture is
used, follow [demo-data.md](demo-data.md) and say that its Activity is
synthetic presentation data.

## Before recording

- Start the chosen test-only or approved review environment and verify the
  login page, Workspace list, Project list, and task route once.
- Use a clean browser profile or sign out first so the selected Workspace and
  Project are clear to viewers.
- Prepare one active Project. If showing the pre-seeded reference Task, make
  its test-only status visible before opening it.
- Keep source-policy terminology precise: `Web enabled` and `Project files
  enabled` are eligibility settings, not completed retrievals.

## Timed narrative

| Time | Screen and action | What to say |
| --- | --- | --- |
| 0:00-0:20 | Open the login or already-authenticated landing page. | "Coglatas is a tenant-aware workspace and project portal. This demo follows one authenticated path from Workspace context to a task whose current state is visible." |
| 0:20-0:45 | Select an existing Workspace, or use the authorized Create Workspace flow. | "Workspace context is selected from server-authorized data. The create action is capability-gated in the UI and enforced again by the server." |
| 0:45-1:10 | Open Projects. Create a Project if the prepared environment permits it, then activate its Draft; otherwise open the prepared active Project. | "Project creation uses server-projected choices. Creation and activation are separate steps, so the Project context is explicit before work starts." |
| 1:10-1:35 | Choose New Task. Point out the Project name, title/metadata, named choices where available, and the Task Brief fields. | "This Task belongs to the Project currently shown. Goal, Deliverable, and Constraints are structured optional Brief fields. The form does not ask for raw internal IDs." |
| 1:35-1:55 | Enter or show Goal, Deliverable, and Constraints. Open the quality checklist and use one missing-item focus action if appropriate. | "The checklist is advisory. It identifies missing optional Brief information and moves keyboard focus to the matching field, but it does not turn optional context into a blocker." |
| 1:55-2:10 | Show source scope. If permitted, show inherited Project policy or a complete Task override. Create the Task once. | "This is a server-authorized source policy for Web and Project-file eligibility. It is configuration only in this baseline: no Web search, provider, file read, runtime, or source content is being claimed." |
| 2:10-2:35 | Open the new Task detail. Show current Task state and current phase. Open Activity. If using the reference fixture, label its one Activity record as synthetic. | "The detail shows the configured current phase and authorized Activity records. It does not invent percentage progress, durable transition history, Failed state, or execution output. A newly created Task may honestly have no Activity yet." |
| 2:35-3:00 | Return to the task policy/state panel or a concise architecture slide. | "Angular presents the flow, ASP.NET Core application services enforce resource authorization, and PostgreSQL persists the data. The scope chain is Tenant, Workspace, Project, Task. Policy settings do not grant access or cause external egress." |

## Presenter guardrails

- Do not call a policy a search result, source retrieval, provider run, file
  read, or AI execution.
- Do not call the configured phase a percentage, a completion forecast, or a
  durable phase history.
- Do not call an empty Activity panel an error, and do not call synthetic
  fixture Activity a real user event.
- Do not present test credentials, loopback Compose, or deterministic seed
  data as a production deployment.
- Do not claim full canonical-spec completion while Issues #357, #369, and
  #410 remain open.

## Suggested recovery wording

If a prepared value is unavailable, stay within the implemented behavior:

- Missing create capability: "This account does not currently have the
  server-authorized create capability; the UI fails closed."
- Empty Activity: "No Activity record exists for this new Task yet; the
  current phase remains visible independently."
- Scope policy unavailable after authorization loss: "The client cleared the
  protected projection and did not infer a policy from stale state."

Do not work around an authorization, validation, or service failure during the
recording by changing server data in the browser console or by presenting a
mocked response as the live system.
