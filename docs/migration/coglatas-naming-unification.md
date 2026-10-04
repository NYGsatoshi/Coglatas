# Coglatas naming unification

This change makes the current implementation checkout use Coglatas throughout
source, configuration references, review templates, and engineering records.
The implementation and private specification changes are reviewed in one pull
request per repository. Merge requires current-head CI and review acceptance,
the specification's patent isolation, and resolved administration blockers.

## Runtime contracts

- The backend runtime-config producer, Angular consumer, and browser tests use
  `window.__COGLATAS_FEATURE_FLAGS__` together. Deploy the newly built host and
  frontend as one artifact; there is no compatibility alias for the former
  browser-global name.
- The design-system marker is `data-coglatas-design-system`.
- The account-rail emblem is `C`, and the inactive Angular scaffold identifies
  the product as Coglatas.
- The response-gate diagnostic header is
  `X-Coglatas-Browser-Smoke-Response-Gate`. The gate is enabled only in the
  synthetic `Test` environment, so it is not a production public API contract.
- New runner installations default to the Linux account prefix
  `coglatasrunner`. This source change does not rename accounts or recreate
  services on an existing runner host.

The solution, project files, assemblies, namespaces, database context, current
Compose resource names, release image metadata, cookie names, and OpenAPI
artifact names already use Coglatas or purpose-specific names. The audit found
no production public URL path or published package ID requiring a breaking
rename. No dependency version or cryptographic integrity value is changed.

## Persistence safety

Current database defaults and users already use Coglatas. The only remaining
database-name fixture in source was a synthetic startup-validation connection
string; it now uses `Database=coglatas;Username=coglatas`.

No EF migration, model snapshot, migration-history identifier, schema, table,
column, database, live user, or persisted layout is changed. No migration is
required for these naming edits. Target-environment production availability
remains an operational verification question documented in `../AI_CONTEXT.md`;
the change does not presume permission to rename or destroy deployed data.

## Documentation and evidence

Active documentation, archived plans, verification records, and tracked build
logs use the current product/repository names. Diagnostic names and example
paths in historical excerpts are normalized for readability; recorded dates,
commit IDs, test totals, outcomes, and limitations are preserved. Git history
retains the original spellings and original diagnostic artifacts.

The browser-state inventory keeps its pinned source snapshot and migration
dispositions while expressing names with the current vocabulary. It does not
claim that the inventory's historical snapshot has been rewritten.

Private specification references name `Coglatas-Spec` and the
`docs/specs/coglatas-core-v4/` family. No private requirement content is copied
into this implementation repository. Legally preserved records belong only
to the specification repository's `docs/legal/patents/` directory; this
implementation repository has no patent exclusion.

## Verification scope

Review covers tracked text in UTF-8 and BOM-marked UTF-16, filenames,
directories, source identifiers, frontend scaffolds, infrastructure, CI,
configuration, metadata, diagrams, and archived evidence. Exact external
account identities and third-party dependency/integrity data remain intact
because they are not Coglatas product branding.

Five product screenshot baselines were visually inspected. Both desktop images
now use the same unmodified image generated from the changed source with the
pinned Linux Playwright renderer, `mcr.microsoft.com/playwright:v1.63.0-noble`.
The focused [Linux generation and verification run](https://github.com/NYGsatoshi/Coglatas/actions/runs/37198846533)
passed against source `6a74dedc21659493e8dabf7efe9e7000ef6d30f8`; its generated
image blob is `273440253cb3ec7692dfaf5cc55f6a94620c195e`.
The generation used `--update-snapshots=all` so an obsolete emblem could not
survive within the existing image-difference tolerance, then reran the same
test without snapshot updates. Its assertions and tolerance are unchanged.
Mobile and permission-denied baselines contain no previous product name.
Snapshot oracles were not repainted or edited by hand.

Build, backend, frontend, CI, and guard results are reported against the final
pull-request commit. Historical evidence in this repository does not establish
that those final checks passed.
