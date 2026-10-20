# Files paging and feedback

Files supplies an authorized server page to the shared grid. Its outer Previous
and Next controls remain responsible for inventory and search pagination. Files
opts out of the adapters' additional local pager; other consumers keep the
existing default. This changes no request, response, selection or access rule.

Inventory denial and transient failure retain their neutral alert text and the
existing Retry action. Their explanation now precedes the intake notice, search
and browse controls so narrow screens can show it immediately. The alert does
not take focus from an active control; Retry remains a native keyboard action.
The upload restriction and current-access notice remain visible.

The Details panel describes its read-only fields in user-facing language. This
does not declare other Files operations read-only or change server authority.

Focused component and browser checks cover the server pager's second response,
row replacement, the adapters' unchanged paging defaults, denial before long
controls, visible narrow-screen feedback and preview focus return. Mocked
browser responses establish UI behavior only. They do not qualify backend
authorization, storage, scanner safety, deployed intake or complete design and
accessibility parity. Execution results must be recorded separately against the
exact source and build used.
