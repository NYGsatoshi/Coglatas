import { TestBed } from '@angular/core/testing';

import { injectMessagingI18n } from './providers/messaging-i18n';
import { I18nService, TranslationKey } from './i18n.service';
import { FILES_ENGLISH_MESSAGES, FILES_JAPANESE_MESSAGES } from './messages/files.messages';
import { MESSAGING_ENGLISH_MESSAGES, MESSAGING_JAPANESE_MESSAGES } from './messages/messaging.messages';
import { PROJECTS_ENGLISH_MESSAGES, PROJECTS_JAPANESE_MESSAGES } from './messages/projects.messages';
import { PROJECT_EXECUTION_ENGLISH_MESSAGES, PROJECT_EXECUTION_JAPANESE_MESSAGES } from './messages/project-execution.messages';
import { PROJECT_FEEDBACK_ENGLISH_MESSAGES, PROJECT_FEEDBACK_JAPANESE_MESSAGES } from './messages/project-feedback.messages';
import { SHELL_ENGLISH_MESSAGES, SHELL_JAPANESE_MESSAGES } from './messages/shell.messages';

interface MessageCatalog {
  readonly name: string;
  readonly en: Readonly<Record<string, string>>;
  readonly ja: Readonly<Record<string, string>>;
}

const catalogs: readonly MessageCatalog[] = [
  { name: 'shell', en: SHELL_ENGLISH_MESSAGES, ja: SHELL_JAPANESE_MESSAGES },
  { name: 'projects', en: PROJECTS_ENGLISH_MESSAGES, ja: PROJECTS_JAPANESE_MESSAGES },
  { name: 'messaging', en: MESSAGING_ENGLISH_MESSAGES, ja: MESSAGING_JAPANESE_MESSAGES },
  { name: 'project execution', en: PROJECT_EXECUTION_ENGLISH_MESSAGES, ja: PROJECT_EXECUTION_JAPANESE_MESSAGES },
  { name: 'project feedback', en: PROJECT_FEEDBACK_ENGLISH_MESSAGES, ja: PROJECT_FEEDBACK_JAPANESE_MESSAGES },
  { name: 'files', en: FILES_ENGLISH_MESSAGES, ja: FILES_JAPANESE_MESSAGES }
];

describe('Core workflow message catalogs', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'en'));

  afterEach(() => {
    TestBed.resetTestingModule();
    window.localStorage.removeItem('coglatas.locale');
  });

  it('does not let one feature silently replace another feature message', () => {
    const keys = catalogs.flatMap((catalog) => Object.keys(catalog.en));
    expect(new Set(keys).size).toBe(keys.length);
  });

  it('registers feature messages when the lazy UI requests the shared language service', () => {
    const service = TestBed.inject(I18nService);
    expect(service.translate('messaging.composer.send')).toBe('messaging.composer.send');
    const featureService = TestBed.runInInjectionContext(injectMessagingI18n);
    expect(featureService).toBe(service);
    expect(service.translate('messaging.composer.send')).toBe('Send');
    service.setLocale('ja');
    expect(service.translate('messaging.composer.send')).toBe('送信');
    expect(TestBed.runInInjectionContext(injectMessagingI18n)).toBe(service);
    expect(service.translate('messaging.composer.send')).toBe('送信');
  });

  for (const catalog of catalogs) {
    it(`provides complete nonempty bilingual ${catalog.name} messages with matching arguments`, () => {
      const keys = Object.keys(catalog.en).sort();
      expect(keys.length).toBeGreaterThan(0);
      expect(Object.keys(catalog.ja).sort()).toEqual(keys);
      for (const key of keys) {
        expect(catalog.en[key].trim(), key).not.toBe('');
        expect(catalog.ja[key].trim(), key).not.toBe('');
        expect(argumentsIn(catalog.ja[key]), key).toEqual(argumentsIn(catalog.en[key]));
      }
    });

    it(`wires the ${catalog.name} catalog into the same live locale service`, () => {
      const service = TestBed.inject(I18nService);
      service.registerMessages(catalog.en, catalog.ja);
      for (const locale of ['ja', 'en'] as const) {
        service.setLocale(locale);
        for (const [key, value] of Object.entries(catalog[locale])) {
          expect(service.translate(key as TranslationKey), key).toBe(value);
        }
      }
    });

    it(`preserves supplied Unicode values when interpolating ${catalog.name} messages`, () => {
      const service = TestBed.inject(I18nService);
      service.registerMessages(catalog.en, catalog.ja);
      for (const locale of ['ja', 'en'] as const) {
        service.setLocale(locale);
        for (const [key, value] of Object.entries(catalog[locale])) {
          const parameters = Object.fromEntries(argumentsIn(value).map((argument) => [argument, `資料-${argument}`]));
          const translated = service.translate(key as TranslationKey, parameters);
          expect(translated, key).not.toContain('{{');
          for (const parameter of Object.values(parameters)) {
            expect(translated, key).toContain(parameter);
          }
        }
      }
    });
  }
});

function argumentsIn(value: string): string[] {
  return [...new Set([...value.matchAll(/\{\{(\w+)\}\}/gu)].map((match) => match[1]))].sort();
}
