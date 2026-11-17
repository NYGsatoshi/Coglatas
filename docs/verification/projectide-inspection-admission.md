# Synthetic Workbench inspection admission

Existing #1030 requires explicit safe low-resource behavior. A completed local
Compiler response can retain a valid semantic model whose inspection projection
exceeds the existing 4 MiB canonical JSON processing bound. The pure synthetic
ViewModel now refuses that display with false and the terminal state
InspectionUnavailable rather than throwing and remaining Waiting. The actual
Compiler execution, verdict, result, Source and recording remain unchanged.

All four existing admission protocols use the same display preparation boundary:
plain compilation, predicate arrays, observed operators and initial-root passes.
The owned request/manifest/response and current Source checks still run first.
Only FormatException from constructing this optional display is handled here.
It is not relabelled as a Compiler ResourceExhausted result or proof of the
precise reason for every possible projection failure. Other exceptions retain
their existing behavior. The existing processing limit is unchanged.

A refusal publishes no Result, Inspection, selection, navigation or recording
metadata. Source remains available in the same context. A strictly newer
generation can start normally. Notifications may reset, dispose or begin a new
waiting/ready/refused context; the older admission returns false and performs
no write after that notification.

Product regressions execute the actual local hosts and sessions with 128 valid
Tasks and 32 optional Boolean input ports per Task: Source is below 1 MiB,
Compiler completes with PASS and IR, and the model has 4,225 symbols. The
projection exceeds its own existing bound. The same original Compiler result
and Source must remain unchanged on refusal. Small controls retain all four
protocols. This is pure ViewModel preparation, not actual Avalonia UI, full
Language Service completion, performance assurance, current access authority
or independent Security acceptance. Existing P0 #1930 owns independent/runtime
authority qualification; no Security/shared CI source is changed. #1144 remains
BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and all Trust/Eligibility/RLS/Native/
Production/Release HOLDs remain.
