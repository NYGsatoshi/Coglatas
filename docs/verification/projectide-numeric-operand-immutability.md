# ProjectIDE numeric operand unit-factor storage

This bounded #905/#1034 Product correction preserves the existing immutable
numeric input and exact basis contracts. It changes storage ownership, not
arithmetic, tolerances, rounding, unit meaning or numerical assurance.

## Reproduced effect

NumericOperand.UnitPowers used a read-only dictionary over a sorted dictionary.
Its public ICollection.SyncRoot exposed a mutable TreeSet through
ICollection<KeyValuePair<string, int>>. Ordinary public collection Clear/Add
could change the typed unit factors while the operand's retained canonical Data
still described the original input. No reflection or unsafe access is required.

Clearing a quantity's factors changed exact addition and comparison from Known
to Unsupported. Replacing a factor changed the unit map of a Known product or
quotient. An empty scalar map could acquire an unrecorded quantity factor and
change a known scalar addition to Unsupported. These five clean original
controls fail at actual evaluation or retained result comparisons.

These are local in-process correctness counterexamples, not demonstrated remote
exploitation, a production incident, actual device acceptance or a resumed
numerical assurance program.

## Owned map and verification

The existing ImmutableMapView owns the ordinally sorted factor snapshot, as it
already does for declared dimensions. Enumeration, keys, values and generic map
access preserve the recorded factors without exposing synchronization storage.
Existing canonical input/result data, dimension checks, exact values, operation
counts and processing limits retain their meaning. The obsolete ObjectModel
import is removed; there is no dependency or severity/threshold change.

A non-incremental build precedes corrected execution. All549 controls pass with
no failed or skipped cases: the existing544 validation/Draft/paired/core cases
plus five functional numeric map controls. The full18-case exporter remains
byte-identical. Original failures, public collection root observations and
corrected receipts are retained separately.

The initial expanded isolated probe attempted to use an internal unit identity
helper and failed compilation, receiving no execution credit. Final controls
obtain the factor identity from an already bound public operand and execute
against the original source before validating the correction.

Independent qualification remains P0 #1938. Current authorization, Producer
Trust/Eligibility/Registry, RLS, real data, actual Avalonia, Native/Production/
Release and numerical resumption remain separate. #1144 is BLOCKED and #1128
SUSPENDED/NOT_EVALUATED. This agent changes ProjectIDE only; it does not edit,
synchronize or merge SecurityTestImplimation.
