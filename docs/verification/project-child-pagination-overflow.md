# Project child list pagination overflow

`ProjectService` shares an in-memory paging helper between Milestone lists,
legacy Project/Task/Milestone-target Comment lists, and the compatibility Task
list fallback. The callers first enforce the existing parent authority and
apply their existing search/deletion filters and safe page/size normalization.
Signed integer multiplication previously wrapped sufficiently large positive
page requests, sometimes returning existing authorized rows again.

The helper computes the offset as a `long` and bounds it by the filtered
collection count before casting. Oversized pages are empty while preserving
the normalized page/size and complete filtered total. Ordinary page ordering,
response mapping, authorization and persistence behavior are unchanged.

## Regression controls

The new partial test file leaves every existing test, assertion and double
unchanged. It exercises three callers through their actual public service
methods using the existing repository/authority doubles:

- Twelve original oversized requests cover negative-wrap and large nonoverflow
  boundaries at normalized sizes of 2 and 100. Three separate additional
  controls use page `42_949_674` and size 100, where the old offset wraps to 4.
- Eighteen ordinary, last, exhausted and normalized page requests assert row
  IDs, ordering, filtered totals and page/size metadata.
- Three outsider controls retain parent-denial errors, and three unmatched
  searches retain empty totals at the maximum page.
- Interleaved other-parent, deleted and unmatched records are excluded before
  paging. Every case checks that no audit or save was produced.

The immutable test-only commit `b08307b32cd48170912f9150752b8d4e5ee0145e`
ran natively on Windows/.NET 10.0.403: **9 failed, 27 passed, 0 skipped**.
All nine failures were the expected oversized-page `Assert.Empty` failures;
the three remaining oversized controls do not overflow and were already empty.
The original 36-case source remains unchanged. A separate test-only commit
`6169a8cc388bdfd31063fbdf559c2c53ba5e80c9` adds only the three small-positive-wrap
controls to that unfixed product and ran natively: **3 failed, 0 passed,
0 skipped**, all at `Assert.Empty`. Original TRX, logs, raw Coverage, source
and binary bindings from both RED executions remain retained.

## Limits

The optimized `ProjectRepository.ListTasksPageAsync` SQL route does not call
this helper and is not repaired or qualified by these tests. The Task control
deliberately covers the compatibility fallback. The Comment control uses the
Project target; it does not establish each other target's authorization.
Other-parent exclusion in the doubles does not establish foreign-parent
membership denial, PostgreSQL SQL ordering, HTTP behavior, concurrent authority,
RLS or deployed/physical acceptance. No Overall Coverage improvement, Security
Rating change, historical timeout cause, Recovery qualification or Release GO
follows from local service controls. Current protected checks, independent
review, original official Gate and exact resulting-Main evidence are separate.
