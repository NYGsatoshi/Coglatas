# ProjectIDE Core Slice 2d: exact units and numeric evaluation

This candidate extends #905 Slice 2a/2b and follows #1029's shared quantity
semantics. It targets the latest Slice 2b feature branch; Slice 2c is not a
dependency. The independent test commits remain descendants of #1604 on the
SecurityTestImplimation lineage. This document defines the supported pure
contracts, not hosted acceptance or authority to merge.

`UnitDefinition` fixes a namespaced unit identity, positive definition version,
dimension vector and expected content digest. `UnitConversionDefinition` fixes
its own identity/version/digest, both complete unit identities and an exact
positive reduced rational factor. Each digest is verified over canonical Source
bytes with the `coglatas.unit-definition/1` or `coglatas.unit-conversion/1`
domain tag. Integrity does not establish authorization or manifest completeness.
The host must supply approved fixed definitions; this layer neither fetches a
registry nor certifies caller-supplied definitions as approved.

Conversion takes an explicit ordered path. It checks every pinned identity and
dimension, rejects conflicting definitions and inconsistent cycles, and applies
the existing `ExactRational` arithmetic. An inverse requires an explicit inverse
definition. Missing paths and unknown unit bindings are Unsupported. The result
retains the original Source quantity, source definition, normalized definition,
exact rational amount and ordered conversion provenance. Source is never rewritten.
Affine, nonlinear and extension conversions remain losslessly Unsupported.

`NumericOperand` adapts supported decoded Decimal/Rational/Quantity values and
explicit conversion results. `ProjectNumericOperations` supports exact addition,
subtraction, multiplication, division and comparison. Addition/comparison requires
identical dimension and unit expressions; different units require explicit
conversion. Multiplication/division derives checked dimension and unit exponents.
A dimensionless quantity keeps its declared unit; it is not silently coerced to
a scalar. Bound normalized units include version and digest, so changing a unit
definition cannot silently preserve comparison compatibility.

Known, Null, Unknown, Invalid and Unsupported remain distinct. Invalid and
Unsupported inputs take precedence; incomplete inputs remain Unknown, and Null
inputs remain Null when no stronger limitation exists. No missing value becomes
zero. Division by zero and dimension mismatch are Invalid. Exhausted numeric,
dimension or cumulative operation bounds are Unknown. Profiles are provisional
processing safeguards, not measured performance guarantees. Original inputs and
canonical basis digests identify arithmetic evidence; these are derived values,
not independently writable Source or IR publication.

Quantization requires an explicit scale and `coglatas.nearest-ties-to-even` mode.
It retains the original exact rational, rounded Decimal, scale, rounding mode
and canonical input basis. No other rounding, unit coercion or saturation occurs.
Time quantities retain their own contracts: WorkingDuration and CalendarPeriod
are Unsupported numeric operands rather than elapsed seconds.

Tests cover exact and repeating values, positive/negative quantities, explicit
inverse/multistep conversion, identity/version/digest/dimension failures, conversion
cycles, unknown extensions, logical bounds, Null/Unknown, quantization and culture
independence. The fixed unit canonical/digest golden vector is independently
computed. Verification combines only the specific independent test commits with
a fixed product candidate in a detached worktree; it does not merge the test base.
Final combined SHA/tree/test evidence is recorded in the PR and integration receipt.
Standalone test CI still lacks unmerged product types and is not claimed as PASS.

No Resource reservation, allocation service, stock mutation or cross-Project
transaction is implemented. Reusable allocation versus consumable stock remains
the existing Source contract. No package, API, database, RLS, Shadow/Enforce,
Required Check or deployment change is included. Web UI acceptance is NotRun;
Windows/Avalonia remains blocked by #1144. #1128 remains SUSPENDED/NOT_EVALUATED.
The Student Council release decision, protected branches and merge authority are
unchanged. Full Slice 2 acceptance awaits all required contracts, review and CI.
