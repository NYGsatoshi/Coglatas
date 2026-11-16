# Actual initial-root Compiler pass observation

This bounded #1034 Product API observes actual function entry/return boundaries
inside the initial-root Compiler call. It is a local owned observation of existing
canonical JSON, not FullDump, FullTrace, a host run or an adopted artifact registry.
Ordinary Compile, predicate capture/observation and existing host/client protocols
keep their separate contracts. No HTTP endpoint or runtime activation is added.

## Capture before compilation

PureCoreCompiler.CompileWithPassObservation takes an immutable
CompilerPassObservationPolicy before executing. Manifest is the default. The
explicit AvailableOriginalJson mode can retain existing original JSON within a
predeclared byte budget. Local event limits are also declared before execution.
These limits govern this memory capture, not durable retention, Pin or quotas.

The hooks surround actual root parse/normalize, binding, semantic validation/IR,
analysis and publication. Entered and Returned describe a function boundary;
they do not assert a successful Compiler verdict or complete evidence. A
cancelled function emits CancelledBeforeReturn instead of a fabricated return.
Pre-cancellation produces no started pass. CompileRegion receives no collector,
so root events cannot impersonate regional observations.

The Source passed to the decoder, its actual decoded return, locked input,
existing semantic model/IR/source map JSON, actual root rule outcomes and actual
root publication result can be referenced. The original binding/validation engine
objects have no canonical body at these boundaries and remain explicit gaps.
External snapshots, units and calendars are not pre-enumerated or serialized by
capture. Observation cannot change their one-read consumption contract.

Root publication precedes dependency attachment. Its original body is distinct
from the final result basis when attachment changes the compilation. Actual
publication overflow records only the real fallback result and its gap; it does
not recover the failed larger publication body. An exception path that returns no
compilation also returns no observation. No optimization or execution-plan trace
is inferred from the later CompilerStageOutcome metadata.

## Original bodies, metadata and gaps

Every retained event has an actual ordinal, pass identifier, phase, existing
original body references and an explicit gap. Domain-separated SHA256 and original
UTF-8 length describe each existing JSON body. Shared domain/digest bodies are
charged once. Manifest keeps event/reference metadata without retaining bodies.

A capture byte overflow drops the entire body map and keeps bounded original
reference metadata. An event overflow drops retained events and bodies; actual
visit counts remain separate. A later reader cannot reconstruct the missing
bytes. Execution, coverage, verdict and evidenceComplete remain the actual
Compiler result axes, independent of capture availability.

RecordedPassCompilation has no public constructor or mutable properties.
Compilation is JsonIgnore and the observation body map is internal, so serializing
the public owner/observation is metadata-only even after explicit body capture.

## Explicit owned snapshots

ProjectCompilerPassObservationSnapshot.Read requires the actual owner, original
compilation, original observation and expected input/result digests. Equal hashes
from another genuine compile do not satisfy object ownership. Wrong ownership,
input or result returns no prior basis, record metadata or bodies. Manifest is
the default reader mode. AvailableOriginalJson returns only retained original
immutable SourceJson objects. A viewing byte limit drops the whole view body set
without rewriting the original capture state. Read cancellation leaves the
caller-owned original observation intact; no physical erasure is claimed.

This ownership is local object admission. It grants no current actor/Tenant read
or export permission, global invocation identity, causal proof, producer trust,
eligible evidence or retention policy. Declared host invocations, live clients,
current access/redaction, durable storage/Pin/deletion and actual Avalonia UI
acceptance remain separate work and P0 owner gates.

## Qualification boundary

CompilerPassObservationTests covers existing actual Source/Compiler vectors,
original result and logical-operation invariance, genuine equal-hash foreign
owners, exact original body hashes/references, metadata-only serialization,
capture/viewing gaps, same-Source different settings, cancellation during real
supply enumeration and disposal, actual publication overflow and unchanged thrown
supply exceptions. Exact committed source, native discovery/TRX, raw original
artifacts, static analysis, reviews and current hosted CI must be recorded in the
PR before integration. An isolated work prototype is not a Product test result.

Full #1034/#905/#782 remain OPEN. Security/shared CI work stays with existing
P0#1959/#1966/#1932/#1974/#1980; this Product slice changes none of their branches
or policies. Preserve #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and all
Registry/Trust/Eligibility/RLS/Native/Production/Release holds.
