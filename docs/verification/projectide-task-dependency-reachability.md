# Structural Task dependency reachability

The local Core query implements the dependency-reachability portion of #1032
under #888 and the accepted IMPACT-02, IMPACT-03, IMPACT-10 and IMPACT-18
decisions. It reads the existing whole-graph result of a fixed Compiler input.
The same query works for an admitted base, Draft or Scenario graph.

The caller supplies the exact expected Compiler input digest and a Task's
semantic location. The query confirms that the compiled graph was evaluated
and structurally valid and that the Task resolves to its exact admitted
context, document and identity. Stale inputs, unavailable graphs and
unadmitted Task locations return a refusal without rows. A missing, denied
or invalid required graph therefore cannot supply a complete empty result.

The query follows `DependsOn` from prerequisite to dependent. Depth one
identifies a direct dependent. Greater depths identify transitive dependents.
Each row retains one deterministic shortest path of original relation
locations. Relation locations provide stable ordering when several paths have
the same length. `RelatedTo` and other relation kinds do not propagate through
this dependency query.

The depth and work limits produce explicit results. Work includes graph
indexing, traversal, and copying the original relation locations for each
path. Each result retains the fully discovered paths available at that bound.
Cancellation stops the query without returning a result. The input and graph
digests bind available results to their immutable source.

This query provides structural paths through the compiled Task graph.
Calendar, slack, scheduling effects, Resource and evidence impact require
their own analysis. The typed result is local to Core. Persisted schemas, new
transport, current authorization, and paired client acceptance remain under
their existing contracts. #1032 remains open.
