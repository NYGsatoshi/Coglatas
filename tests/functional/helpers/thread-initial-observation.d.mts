import type { Page } from '@playwright/test';
export const INITIAL_THREAD_DOM_FLAGS: readonly string[];
export const THREAD_SHAPE_FLAGS: readonly string[];
export interface InitialThreadObservation {
  responseShape: Record<'objectShape' | 'rootMatches' | 'conversationMatches' | 'summaryMatches' | 'replyIdentitiesMatch' | 'countsConsistent', boolean> | null;
  failureDom: Record<'workspacePresent' | 'workspaceVisible' | 'loadingPresent' | 'loadingVisible' | 'errorPresent' | 'errorVisible' | 'rootPresent' | 'rootVisible', boolean> & Partial<Record<'pagePresent' | 'conversationPresent' | 'threadOpenClass' | 'groupClass', boolean>> | null;
  /** Safe structural sample after an already-in-flight GET returns 400 during a new action. */
  preActionDeniedDom?: NonNullable<InitialThreadObservation['failureDom']> | null;
}
export function threadResponseShape(value: unknown, expectedRoot: string, expectedConversation: string): NonNullable<InitialThreadObservation['responseShape']>;
export function observeInitialThreadShape(initial: InitialThreadObservation, readBody: () => Promise<unknown>, expectedRoot: string, expectedConversation: string): () => void;
export function validateInitialThreadObservation(value: unknown): InitialThreadObservation;
export function readInitialThreadDom(page: Pick<Page, 'evaluate'>): Promise<NonNullable<InitialThreadObservation['failureDom']>>;
