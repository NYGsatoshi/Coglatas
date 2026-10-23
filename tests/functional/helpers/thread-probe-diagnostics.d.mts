export const THREAD_PROBE_ATTACHMENT: string;
export interface ThreadTarget { singleTab: boolean; visible: boolean; enabled: boolean; selected: boolean;
  inViewport: boolean; centerReceivesPointer: boolean; workspaceExpanded: boolean }
export interface ThreadProbe {
  phases: string[];
  target: ThreadTarget | null;
  observation?: import('./thread-probe-observation.mjs').ThreadObservation;
}
export function validateThreadProbe(value: unknown): ThreadProbe;
