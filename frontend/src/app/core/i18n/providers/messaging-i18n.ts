import { inject } from '@angular/core';

import { I18nService } from '../i18n.service';
import { MESSAGING_ENGLISH_MESSAGES, MESSAGING_JAPANESE_MESSAGES } from '../messages/messaging.messages';

/** Register this feature only when its lazy-loaded UI is instantiated. */
export function injectMessagingI18n(): I18nService {
  const i18n = inject(I18nService);
  i18n.registerMessages(MESSAGING_ENGLISH_MESSAGES, MESSAGING_JAPANESE_MESSAGES);
  return i18n;
}
