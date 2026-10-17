import { I18nService } from '../../core/i18n/i18n.service';
import { PROJECTS_ENGLISH_MESSAGES } from '../../core/i18n/messages/projects.messages';

export type ProjectUiCopy = typeof PROJECTS_ENGLISH_MESSAGES[keyof typeof PROJECTS_ENGLISH_MESSAGES] | 'Cancel' | 'Close' | 'Retry' | 'Confirm' | 'Working…';
type ProjectMessageKey = keyof typeof PROJECTS_ENGLISH_MESSAGES | 'common.cancel' | 'common.close' | 'common.retry' | 'common.confirm' | 'common.working';

const messageKeys = new Map<ProjectUiCopy, ProjectMessageKey>(
  Object.entries(PROJECTS_ENGLISH_MESSAGES).map(([key, value]) => [value, key as keyof typeof PROJECTS_ENGLISH_MESSAGES])
);
messageKeys.set('Cancel', 'common.cancel');
messageKeys.set('Close', 'common.close');
messageKeys.set('Retry', 'common.retry');
messageKeys.set('Confirm', 'common.confirm');
messageKeys.set('Working…', 'common.working');

/** Only application-owned UI copy is accepted. Resource and configured Stage names bypass this helper. */
export function projectUiText(i18n: I18nService, text: ProjectUiCopy, parameters: Readonly<Record<string, string | number>> = {}): string {
  return i18n.translate(messageKeys.get(text)!, parameters);
}

export function projectPriorityText(i18n: I18nService, priority: 'low' | 'medium' | 'high' | 'urgent' | 'critical'): string {
  const labels: Record<typeof priority, ProjectUiCopy> = {low: 'Low', medium: 'Medium', high: 'High', urgent: 'Urgent', critical: 'Critical'};
  return projectUiText(i18n, labels[priority]);
}
