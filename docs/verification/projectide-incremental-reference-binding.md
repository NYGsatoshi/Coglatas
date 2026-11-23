# Incremental ProjectIDE reference binding

Existing #1030 / #905 work adds an optional caller-owned
`ProjectReferenceBindingCache` to ordinary compilation, regional closure binding
and the local .NET host. It reuses the immutable resolution of an original
structural entity/port reference or a known typed value reference. The offline
fixture exporter opts into this session alongside symbol reuse and retains its
comparison with uncached Compiler results. Defaults remain uncached.

## Dependencies and fresh processing

Every invocation still enumerates and admits the actual supplied dependencies,
checks current supplied authorization flags, pins, conflicts, coverage, supported
Source and logical/numeric limits, and builds its own scope and symbol lookups.
Only then can a reference consult retained resolution. A retained entry never
supplies a snapshot, permission, declaration or complete model.

Keys preserve exact length-framed reference, origin and Source context text,
current denied/conflicting scope state, admitted target-scope multiplicity and
declaration coverage, and every matching target symbol's identity, kind, status
and location. Structural port references also bind their exact Port identity.
Typed values additionally bind reference kind/expected kind, matching original
manifest pins and Source digests, and an applicable document's original data.
The key does not use a digest as a substitute for equality.

An unrelated declaration edit can retain a structural reference's resolution
when its actual lookup dependencies remain equal. Changed Port availability,
ambiguity, unsupported kind, coverage, denial, missing supply, context or manifest
cannot borrow an earlier outcome. Global rules, descriptors, packages, units,
calendars, observations, diagnostics, semantic validation and execution plans
remain fresh. Logical charges remain identical to uncached compilation.

Diagnostics are constructed again with the current actual input. A document
reference republishes its derived document symbol into each new model, even if
the reference that first introduced it in an earlier model has disappeared.
Each returned model, regional certificate, full index and query cursor belongs
to its new invocation; retained edges do not transfer query ownership.

## Lifecycle and bounds

The explicit session has LRU entry and exact dependency-key byte limits, Clear,
cancellation checks and synchronized local-host use. The byte counter measures
retained key text, not managed heap usage or a performance SLA. An oversized key
uses fresh resolution without retention or a truncated key. A different root
Tenant/Project/Branch clears retained entries even when the new input contains
no references. Closure regions use their actual root owner.

A binding attempt captures its session identity, owner and invalidation epoch.
Clear or a different owner invalidates that attempt: later references bind fresh
without consulting or repopulating the cleared session. Noncanonical internal
model metadata carries this lightweight token into later dependency regions and
the full Compile continuation, preventing an earlier root from acquiring a new
epoch. The token retains no cache object or Source and grants no authority. A
deliberately different caller-owned session starts its own binding lifetime.
Cancellation, canonical bytes, logical charges and public protocols stay unchanged.

Product regressions compare complete uncached Compiler/result/publication bytes,
logical charges, full regional index bytes, definitions and every bounded
reference/completion page. They exercise actual object reuse, dependency edits,
value shapes and manifests, document-symbol publication, global dimensions,
ownership changes, retention limits, cancellation and concurrent host requests.
Deterministic lifecycle controls invalidate between binding start and resolution,
during actual supplied-snapshot enumeration, before later regional binding and
after actual root binding within full compilation. A separate-session positive
control retains valid fresh work; the original failing case identities remain
attributed to their original Product source.
Actual execution, source pins, raw discovery/results and review belong to the PR
evidence; these Product tests do not constitute independent Security acceptance.

This advances dependency-directed reference resolution. Full semantic validation
and index assembly still run afresh; no general incremental-verdict or performance
claim is made. #1030 remains open for its complete editor/typed Rename and paired
acceptance criteria. Existing P0 #1930/#1938 retain current runtime authority,
privacy/revocation and independent immutable-storage qualification. Actual
Avalonia/Native, #1144/#1128, Trust/Eligibility/RLS/Registry, main, Production and
Release holds remain unchanged. No Security branch or shared CI is modified.
