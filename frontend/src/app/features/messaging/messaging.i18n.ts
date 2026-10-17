import { I18nService } from '../../core/i18n/i18n.service';
import {
  MessagingMessageViewModel,
  MessagingPageViewModel,
  MessagingThreadViewModel,
} from './messaging.types';

// Only explicit application-authored keys are translated. Bodies, titles,
// participant names, search text and server strings remain opaque content.
export function localizeMessage(
  message: MessagingMessageViewModel,
  i18n: I18nService,
): MessagingMessageViewModel {
  return {
    ...message,
    authorLabel: message.authorLabelKey
      ? i18n.translate(message.authorLabelKey)
      : message.authorLabel,
    authorRoleLabel: message.authorRoleLabelKey
      ? i18n.translate(message.authorRoleLabelKey)
      : message.authorRoleLabel,
    sentAtLabel:
      message.deliveryState === 'sending'
        ? i18n.translate('messaging.composer.sending')
        : message.createdAt
          ? i18n.formatDateTime(message.createdAt, { dateStyle: 'medium', timeStyle: 'short' })
          : message.deliveryState === 'failed'
            ? i18n.translate('messaging.notSent')
            : message.sentAtLabel,
    safeFailureReason:
      message.safeFailureReason && message.safeFailureReasonKey
        ? i18n.translate(message.safeFailureReasonKey)
        : message.safeFailureReason,
  };
}

export function localizeThread(
  thread: MessagingThreadViewModel,
  i18n: I18nService,
): MessagingThreadViewModel {
  return {
    ...thread,
    rootMessage: thread.rootMessage ? localizeMessage(thread.rootMessage, i18n) : undefined,
    replies: thread.replies.map((reply) => localizeMessage(reply, i18n)),
    error: thread.error && thread.errorKey ? i18n.translate(thread.errorKey) : thread.error,
  };
}

export function localizeMessagingPage(
  page: MessagingPageViewModel,
  i18n: I18nService,
): MessagingPageViewModel {
  const conversation = page.conversation;
  return {
    ...page,
    title: page.titleKey ? i18n.translate(page.titleKey) : page.title,
    conversation: {
      ...conversation,
      title: conversation.titleKey ? i18n.translate(conversation.titleKey) : conversation.title,
      subtitle: conversation.subtitleKey
        ? i18n.translate(conversation.subtitleKey)
        : conversation.subtitle,
      composerDisabledReason:
        conversation.composerDisabledReason && conversation.composerDisabledReasonKey
          ? i18n.translate(conversation.composerDisabledReasonKey)
          : conversation.composerDisabledReason,
      attachment: {
        ...conversation.attachment,
        label: conversation.attachment.labelKey
          ? i18n.translate(conversation.attachment.labelKey)
          : conversation.attachment.label,
      },
    },
    messages: page.messages.map((message) => localizeMessage(message, i18n)),
    sendState:
      page.sendState.status === 'failed' && page.sendState.messageKey
        ? { ...page.sendState, message: i18n.translate(page.sendState.messageKey) }
        : page.sendState,
    inlineError:
      page.inlineError && page.inlineErrorKey
        ? i18n.translate(page.inlineErrorKey)
        : page.inlineError,
  };
}
