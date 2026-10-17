import { inject } from '@angular/core';

import { I18nService } from '../i18n.service';
import { PROJECTS_ENGLISH_MESSAGES, PROJECTS_JAPANESE_MESSAGES } from '../messages/projects.messages';
import { PROJECT_EXECUTION_ENGLISH_MESSAGES, PROJECT_EXECUTION_JAPANESE_MESSAGES } from '../messages/project-execution.messages';
import { PROJECT_FEEDBACK_ENGLISH_MESSAGES, PROJECT_FEEDBACK_JAPANESE_MESSAGES } from '../messages/project-feedback.messages';

/** Register this feature only when its lazy-loaded UI is instantiated. */
export function injectProjectsI18n(): I18nService {
  const i18n = inject(I18nService);
  i18n.registerMessages(PROJECTS_ENGLISH_MESSAGES, PROJECTS_JAPANESE_MESSAGES);
  i18n.registerMessages(PROJECT_EXECUTION_ENGLISH_MESSAGES, PROJECT_EXECUTION_JAPANESE_MESSAGES);
  i18n.registerMessages(PROJECT_FEEDBACK_ENGLISH_MESSAGES, PROJECT_FEEDBACK_JAPANESE_MESSAGES);
  return i18n;
}
