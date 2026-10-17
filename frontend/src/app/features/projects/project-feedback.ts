import { normalizeApiError } from '../../core/api/api-error.adapter';
import type { I18nService } from '../../core/i18n/i18n.service';
import { PROJECT_FEEDBACK_ENGLISH_MESSAGES } from '../../core/i18n/messages/project-feedback.messages';

export type ProjectFeedbackKey = keyof typeof PROJECT_FEEDBACK_ENGLISH_MESSAGES;
const tokenIdentity = Symbol('Project feedback text');
const fieldSymbols = new Map<string, symbol>();
const symbolFields = new Map<symbol, string>();

export interface ProjectFeedbackText {
  readonly [tokenIdentity]: true;
  readonly key: ProjectFeedbackKey;
  readonly parameters: Readonly<Record<string, ProjectFeedbackArgument>>;
  readonly trim?: boolean;
}
export type ProjectFeedbackString = string | ProjectFeedbackText;
export type ProjectFeedbackValue = ProjectFeedbackString | null | undefined;
export type ProjectFeedbackArgument = ProjectFeedbackValue | number;
type Resolved<T> = T extends ProjectFeedbackText ? string : T;
interface FieldSource {
  readonly text: ProjectFeedbackText;
  readonly original: string;
}

/** Only explicitly marked application copy is eligible for translation. */
export function projectFeedbackText(
  key: ProjectFeedbackKey,
  parameters: Readonly<Record<string, ProjectFeedbackArgument>> = {},
): ProjectFeedbackText {
  return { [tokenIdentity]: true, key, parameters };
}

export function trimProjectFeedback(value: ProjectFeedbackValue): ProjectFeedbackValue {
  return isText(value) ? { ...value, trim: true } : value?.trim();
}

export function projectFeedbackField<K extends string, V extends ProjectFeedbackValue>(
  field: K,
  value: V,
): Record<K, Resolved<V>> {
  let symbol = fieldSymbols.get(field);
  if (!symbol) {
    symbol = Symbol(`Project presentation: ${field}`);
    fieldSymbols.set(field, symbol);
    symbolFields.set(symbol, field);
  }
  if (!isText(value)) {
    return { [field]: value, [symbol]: undefined } as unknown as Record<K, Resolved<V>>;
  }
  const original = renderText(value);
  return {
    [field]: original,
    [symbol]: { text: value, original } satisfies FieldSource,
  } as unknown as Record<K, Resolved<V>>;
}

/** A copied/replaced server string cannot inherit an obsolete application key. */
export function projectFeedbackValue<T extends object, K extends keyof T>(
  value: T,
  field: K,
): T[K] | ProjectFeedbackText;
export function projectFeedbackValue<T extends object, K extends keyof T>(
  value: T | null | undefined,
  field: K,
): T[K] | ProjectFeedbackText | undefined;
export function projectFeedbackValue<T extends object, K extends keyof T>(
  value: T | null | undefined,
  field: K,
): T[K] | ProjectFeedbackText | undefined {
  if (!value) {
    return undefined;
  }
  const symbol = fieldSymbols.get(String(field));
  const source = symbol ? (value as Record<symbol, FieldSource>)[symbol] : undefined;
  return source && source.original === value[field] ? source.text : value[field];
}

/** Projects display changes without rewriting the raw state used by commands. */
export function projectFeedbackView<T>(i18n: I18nService, raw: T): T {
  i18n.locale();
  const visit = (value: unknown): unknown => {
    if (Array.isArray(value)) {
      const items = value.map(visit);
      return items.some((item, index) => item !== value[index]) ? items : value;
    }
    if (!value || typeof value !== 'object' || Object.getPrototypeOf(value) !== Object.prototype) {
      return value;
    }
    const record = value as Record<string | symbol, unknown>;
    let result = record;
    const replace = (key: string, next: unknown): void => {
      if (next !== record[key]) {
        if (result === record) {
          result = { ...record };
        }
        result[key] = next;
      }
    };
    for (const key of Object.keys(record)) {
      replace(key, visit(record[key]));
    }
    for (const symbol of Object.getOwnPropertySymbols(record)) {
      const field = symbolFields.get(symbol);
      if (!field) {
        continue;
      }
      const source = record[symbol] as FieldSource | undefined;
      if (source && source.original === record[field]) {
        replace(field, renderText(source.text, i18n));
      }
      if (result === record) {
        result = { ...record };
      }
      delete result[symbol];
    }
    return result;
  };
  return visit(raw) as T;
}

function isText(value: unknown): value is ProjectFeedbackText {
  return (
    !!value && typeof value === 'object' && (value as ProjectFeedbackText)[tokenIdentity] === true
  );
}

function renderText(text: ProjectFeedbackText, i18n?: I18nService): string {
  const parameters = Object.fromEntries(
    Object.entries(text.parameters).map(([key, value]) => [
      key,
      isText(value)
        ? renderText(value, i18n)
        : typeof value === 'number'
          ? i18n
            ? i18n.formatNumber(value)
            : new Intl.NumberFormat('en').format(value)
          : (value ?? ''),
    ]),
  );
  const rendered = i18n
    ? i18n.translate(text.key, parameters)
    : PROJECT_FEEDBACK_ENGLISH_MESSAGES[text.key].replace(/\{\{(\w+)\}\}/gu, (match, key: string) =>
        String(parameters[key] ?? match),
      );
  return text.trim ? rendered.trim() : rendered;
}

/** Preserve opaque remote errors; only locally constructed diagnostics carry keys. */
export function projectFeedbackError(text: ProjectFeedbackText): Error {
  return Object.assign(new Error(renderText(text)), projectFeedbackField('message', text));
}

export function normalizeProjectFeedbackError(input: unknown, explicitStatus?: number) {
  const normalized = normalizeApiError(input, explicitStatus);
  if (!input || typeof input !== 'object') {
    return normalized;
  }
  const value = projectFeedbackValue(input as { message?: string }, 'message');
  return isText(value) ? { ...normalized, ...projectFeedbackField('message', value) } : normalized;
}

export function projectPriorityFeedback(value: string): ProjectFeedbackString {
  const keys = {
    low: 'projectFeedback.priority.low',
    medium: 'projectFeedback.priority.medium',
    high: 'projectFeedback.priority.high',
    urgent: 'projectFeedback.priority.urgent',
  } as const;
  const key = keys[value as keyof typeof keys];
  return key ? projectFeedbackText(key) : value;
}

export function projectSectionFeedback(value: string): ProjectFeedbackString {
  const keys = {
    detail: 'projectFeedback.projects.sectionDetail',
    activity: 'projectFeedback.projects.sectionActivity',
    subtasks: 'projectFeedback.projects.sectionSubtasks',
    checklist: 'projectFeedback.projects.sectionChecklist',
    comments: 'projectFeedback.projects.sectionComments',
    labels: 'projectFeedback.projects.sectionLabels',
    watch: 'projectFeedback.projects.sectionWatch',
    files: 'projectFeedback.projects.sectionFiles',
  } as const;
  const key = keys[value as keyof typeof keys];
  return key ? projectFeedbackText(key) : value;
}
