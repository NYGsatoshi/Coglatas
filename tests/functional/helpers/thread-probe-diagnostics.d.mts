export const THREAD_PROBE_ATTACHMENT: string;
export interface ThreadProbe {
  phases: string[];
  target: { singleTab: boolean; visible: boolean; enabled: boolean; selected: boolean;
    inViewport: boolean; centerReceivesPointer: boolean; workspaceExpanded: boolean };
}
export function validateThreadProbe(value: unknown): ThreadProbe;
