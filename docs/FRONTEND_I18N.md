# Frontend internationalization

The legacy vanilla JavaScript i18n layer under `src/Coglatas.Web/wwwroot/scripts/i18n` was removed with the static SPA. MVP-A P0 frontend work should define i18n inside the Angular source under `frontend/`.

## Locales

Do not add new locale files under `src/Coglatas.Web/wwwroot`. That directory is for hosted Angular build artifacts only.

The active runtime locale layer is `frontend/src/app/core/i18n/i18n.service.ts`.
It currently supports Japanese (`ja`) and English (`en`). Japanese is the default
when no preference has been saved, preserving the existing browser experience.

The Account page lets each user select a display language. The selection applies
immediately, updates the document `lang` attribute, and is persisted in that
browser's local storage as `coglatas.locale`. It is intentionally a browser UI
preference: no tenant-wide setting or user-profile API is changed by this flow.

## Adding UI text

1. Add text to `I18nService` through its Angular runtime translation mechanism.
2. Keep keys or message identifiers hierarchical and feature-oriented, for example:
   - `common.save`
   - `auth.signIn`
   - `nav.projects`
   - `projects.empty`
   - `chat.messageRequired`
3. Use interpolation for dynamic UI text.

## Rules

- New visible UI text must use translation keys instead of hardcoded English or Japanese strings.
- Do not translate user-generated content, including posts, chat messages, comments, uploaded file names, project names, user-entered titles, descriptions, or free-form text fields.
- Date/time and number display should use `I18nService.formatDateTime` and
  `I18nService.formatNumber`, rather than a hard-coded browser locale.
- Keep Japanese concise and natural for school users.
