import type { Page } from '@playwright/test';

export type TaskTitleFocusPhase = 'before-assertion' | 'assertion-failed';
export interface TaskTitleFocusState {
  activeControlKind: 'task-title' | 'input' | 'textarea' | 'select' | 'button' | 'dialog' | 'body' | 'other' | 'none';
  documentHasFocus: boolean;
  documentReadyState: 'loading' | 'interactive' | 'complete';
  titlePresent: boolean;
  titleIsActiveElement: boolean;
  titleDisabled: boolean;
  titleConnected: boolean;
}
export type TaskTitleFocusSnapshot = { phase: TaskTitleFocusPhase } & (
  { availability: 'available' } & TaskTitleFocusState |
  { availability: 'unavailable' }
);
export function readTaskTitleFocusState(): TaskTitleFocusState;
export function captureTaskTitleFocus(page: Pick<Page, 'evaluate'>, phase: TaskTitleFocusPhase): Promise<TaskTitleFocusSnapshot>;
