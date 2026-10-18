export type DraftLinkKind = 'Project' | 'Task' | 'File';

/** Browser-only view models. These are not Draft or Links API contracts. */
export interface DraftLinkPreview {
  readonly id: string;
  readonly kind: DraftLinkKind;
  readonly label: string;
}

export interface LocalDraftContent {
  readonly title: string;
  readonly body: string;
  readonly links: readonly DraftLinkPreview[];
}

export interface LocalDraftCheckpoint {
  readonly version: number;
  readonly content: LocalDraftContent;
}

export type LocalCheckpointResult =
  | { readonly status: 'saved'; readonly checkpoint: LocalDraftCheckpoint }
  | { readonly status: 'conflict'; readonly checkpoint: LocalDraftCheckpoint };

export type RemoteDraftAttempt = 'unavailable' | 'failed' | 'conflict' | 'denied';

export const EMPTY_LOCAL_DRAFT: LocalDraftContent = { title: '', body: '', links: [] };

export function copyDraft(content: LocalDraftContent): LocalDraftContent {
  return { ...content, links: content.links.map((link) => ({ ...link })) };
}

export function sameDraft(left: LocalDraftContent, right: LocalDraftContent): boolean {
  return left.title === right.title && left.body === right.body &&
    left.links.length === right.links.length && left.links.every((link, index) =>
      link.id === right.links[index].id && link.kind === right.links[index].kind && link.label === right.links[index].label);
}
