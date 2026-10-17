import { normalizeApiError } from '../../core/api/api-error.adapter';
import { I18nService, TranslationKey } from '../../core/i18n/i18n.service';
import type { FileViewModel } from './files.types';

/** Presentation metadata belongs to application copy, never to supplied file content. */
export interface FileMessageSource {
  readonly key: TranslationKey;
  readonly parameters?: Readonly<Record<string, string | number>>;
  readonly error?: Readonly<{ httpStatus: number; message: string }>;
}

export interface FileMessagePresentation {
  readonly message?: string;
  readonly messageSource?: FileMessageSource;
}

export function fileMessage(
  i18n: I18nService,
  key: TranslationKey,
  parameters?: Readonly<Record<string, string | number>>,
): Required<FileMessagePresentation> {
  return { message: i18n.translate(key, parameters), messageSource: { key, parameters } };
}

export function fileErrorMessage(
  i18n: I18nService,
  error: unknown,
  key: TranslationKey,
): Required<FileMessagePresentation> {
  const normalized = normalizeApiError(error);
  return {
    message: i18n.apiErrorMessage(normalized, key),
    messageSource: {
      key,
      error: { httpStatus: normalized.httpStatus, message: normalized.message },
    },
  };
}

export function renderFileMessage(i18n: I18nService, value: FileMessagePresentation): string {
  const source = value.messageSource;
  if (!source) {
    return value.message ?? '';
  }
  if (source.error) {
    return i18n.apiErrorMessage(source.error, source.key);
  }
  const parameters = Object.fromEntries(
    Object.entries(source.parameters ?? {}).map(([key, item]) => [
      key,
      typeof item === 'number' ? i18n.formatNumber(item) : item,
    ]),
  );
  return i18n.translate(source.key, parameters);
}

export function displayFileName(i18n: I18nService, file: FileViewModel): string {
  return file.originalFileNameIsFallback
    ? i18n.translate('files.untitledFile')
    : file.originalFileName;
}

export function displayFileOwner(i18n: I18nService, file: FileViewModel): string {
  return file.uploadedByDisplayIsFallback
    ? i18n.translate('files.unknownUser')
    : file.uploadedByDisplay;
}
