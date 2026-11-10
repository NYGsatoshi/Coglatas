# Form response pagination bounds

An authorized response-list request with a very large positive page could wrap
the previous 32-bit offset back into stored answers. Page 1,073,741,825 with
size four produced offset zero and returned the first four responses instead of
an empty page. Negative and small positive wraps also selected existing rows.
This was a paging error after management admission, not evidence of an
authorization bypass or a historical scanner failure's cause.

The response-list offset now uses 64-bit multiplication after the existing
page/size normalization and is bounded by the selected Form's response count
before conversion to the integer accepted by `Skip`. Management admission,
missing-Form errors, repository ordering and Form selection, normalized page
metadata, total count, answer projection and anonymous identity redaction remain
unchanged. No write, audit, query, permission, or persistence contract is added.
The separate Form catalog paging repair remains independently qualified.

Twenty new test cases use the actual Application service and existing repository,
identity and authority doubles. Eight large-page cases cover four wrap classes
for both named and anonymous Forms. Six normal-page/clamp cases each check both
identity modes, including returned answer text and stored order. Responses for
another Form are interleaved in the repository fixture. Four denial controls
retain missing Form, unauthenticated actor, ordinary member and foreign Workspace
errors; two empty-list controls retain named and anonymous behavior. Each path
checks that no audit or save occurs. Original test files are unchanged.

The immutable test-only revision's eight failures and twelve passing controls
remain separate from repaired native execution. Native source/binary/report
binding, independent review, required checks, original official PR Gate and
resulting-Main qualification remain distinct. Doubles do not establish actual
PostgreSQL, HTTP, concurrent admission, deployed RLS, physical acceptance or
Release GO. No report rewriting/import, coverage exclusion, threshold, baseline,
Owner hold or formal finding disposition changes; local coverage is not a Main
improvement forecast.
