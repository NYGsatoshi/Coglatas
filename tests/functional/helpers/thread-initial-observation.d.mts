import type { Page } from '@playwright/test';
export const INITIAL_THREAD_DOM_FLAGS: readonly string[];
export const THREAD_SHAPE_FLAGS: readonly string[];
export interface InitialThreadObservation {
  responseShape: Record<'objectShape' | 'rootMatches' | 'conversationMatches' | 'summaryMatches' | 'replyIdentitiesMatch' | 'countsConsistent', boolean> | null;
  failureDom: Record<'workspacePresent' | 'workspaceVisible' | 'loadingPresent' | 'loadingVisible' | 'errorPresent' | 'errorVisible' | 'rootPresent' | 'rootVisible', boolean> | null;
}
export function threadResponseShape(value: unknown, expectedRoot: string, expectedConversation: string): NonNullable<InitialThreadObservation['responseShape']>;
export function validateInitialThreadObservation(value: unknown): InitialThreadObservation;
export function readInitialThreadDom(page: Pick<Page, 'evaluate'>): Promise<NonNullable<InitialThreadObservation['failureDom']>>;
