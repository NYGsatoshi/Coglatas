# Angular 22 official migration verification

## Scope

This record covers ANG22-05 (#663) on top of the Angular 22 / TypeScript 6 / NgRx 22 dependency baseline.

## Angular migrations applied

The migration branch installed both Angular workspaces with strict peer dependency resolution on Node 24.15.0 / npm 11.17.0, then executed the repository-installed Angular 22 schematics without `--force` or `--legacy-peer-deps`:

- `@angular/core`: 21.2.19 -> 22.1.5 (`frontend` and `coglatas-frontend`)
- `@angular/cli`: 21.2.19 -> 22.1.7 (`frontend` and `coglatas-frontend`)
- `@angular/cdk`: 21.2.14 -> 22.1.5 (`frontend`)

The retained core-migration changes are limited to explicit `ChangeDetectionStrategy.Eager` compatibility on the 117 migrated components and HTTP/XHR compatibility where required. Migration-only formatting churn was removed before review.

The schematic also proposed `extendedDiagnostics` in the application tsconfigs. That generated setting is not retained because Angular 22 rejects `extendedDiagnostics` when the repository's existing `strictTemplates: false` compatibility mode is active (`NG4003`). The pre-migration compiler settings therefore remain unchanged in this lane; enabling strict template checking or extended diagnostics belongs in a separate, deliberate migration.

Optional CLI migrations are intentionally not applied here:

- Karma -> Vitest migration: the maintained frontend already uses the repository's current test architecture and this is not an Angular 22 compatibility requirement.
- application-builder/Vite migration: explicitly deferred by #658/#663 and handled separately from the Angular 22 compatibility migration.

## NgRx migration inventory

The NgRx 22.0.0 upstream migration collections were inspected for every package used by `frontend`:

- `@ngrx/store`: latest migration entry is v18 beta.
- `@ngrx/effects`: latest migration entry is v18 beta.
- `@ngrx/entity`: latest migration entry targets the v5.2/v6 transition.
- `@ngrx/component-store`: latest migration entry is v18 beta.

There is no v21 -> v22 source migration to apply. An attempted generic `ng update @ngrx/store --migrate-only` loaded historical migration code and failed in the package's ESM context before selecting any v21 -> v22 migration. Because the v22 migration inventory contains no applicable entry, this runner failure is not treated as an application migration requirement and no workaround/force path is used.

## Constraints retained

- no signals rewrite
- no broad OnPush conversion
- no Storybook Vite/application-builder migration
- no unrelated source refactor
- no force/legacy peer dependency mode

## Acceptance

The retained compatibility changes are validated by the normal repository build, Angular unit, Storybook, Playwright, static-analysis, security, and supply-chain gates before this migration is accepted into the Angular 22 integration branch.
