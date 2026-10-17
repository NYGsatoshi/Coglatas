import { inject } from '@angular/core';

import { I18nService } from '../i18n.service';
import { FILES_ENGLISH_MESSAGES, FILES_JAPANESE_MESSAGES } from '../messages/files.messages';

/** Register this feature only when its lazy-loaded UI is instantiated. */
export function injectFilesI18n(): I18nService {
  const i18n = inject(I18nService);
  i18n.registerMessages(FILES_ENGLISH_MESSAGES, FILES_JAPANESE_MESSAGES);
  return i18n;
}
