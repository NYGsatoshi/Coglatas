# Retained Workbench read rosters

This bounded #1030 / #782 Product correction preserves two existing local
read contracts: legacy FindReferences results and synthetic snapshot
materialization reads. It adds no query, cursor, transport field or authority.

Both readers previously returned a standard read-only collection whose
non-generic ICollection.SyncRoot exposed its mutable backing List. An in-process
consumer could replace, append, remove or clear retained entries while the
original semantic inspection, Compiler result and admitted transport bytes
remained unchanged. A later fresh read still returned the original facts.
This was a retained-view integrity mismatch; it did not modify Source, grant
current access or establish a server authorization bypass.

The readers now detach their entries into private storage behind a local
read-only list. Existing generic IList reads, Contains, IndexOf, CopyTo and
enumeration remain available; all existing generic mutation methods still throw
NotSupportedException. No non-generic collection or synchronization-storage view
is exposed. Existing order, member identities, search coverage/truncation,
snapshot admission and all Source/input checks remain unchanged. Bounded Core
reference pages and their paired offline adoption remain separate, unchanged APIs.

## Regression evidence

Eight Product cases use the actual local Compiler/Workbench search and genuine
pinned materialization transport. Each attempts one synchronization-storage
operation after verifying the generic write refusals, detached copies and actual
original members. Original Source/Compiler/inspection/transport facts and a fresh
read are checked before the retained-roster assertion. The original implementation
fails only that final count/member assertion in all eight cases. The exact same
test bytes are used for the correction, followed by focused, full ProjectIDE,
architecture and static checks. Exact execution results and source/artifact
identities belong to the PR evidence.

The first private helper build lacked a reference-type constraint for generic
identity assertions and executed zero cases. That build failure is retained;
adding the constraint preserves every assertion and precedes the eight executed
counterexamples. The first correction build also exposed the Preview
exporter's explicit linked-source dependency; its project now includes the
same private roster implementation. That zero-case failure is retained, with
no build/analyzer gate relaxation.

This remains pure local Product behavior. Runtime query/open authority,
operational cache/materialization lifecycle, independent Security qualification,
actual Avalonia and full #1030 acceptance remain separate. Existing P0 #1930
retains runtime index/query ownership; no Security/shared CI source is changed.
#1144 stays BLOCKED; #1128 stays SUSPENDED/NOT_EVALUATED. Registry/Trust/
Eligibility/RLS/Native/Production/Release HOLDs and broader parent issues remain.
