export interface RosterQualificationInput {
  ownerContext: { close(): Promise<void> } | undefined;
  evidence: { ownerContextClosed: boolean | null; status: string };
  testInfo: {
    outputPath(name: string): string;
    attach(name: string, options: { contentType: string; path: string }): Promise<void>;
    annotations: { type: string; description?: string }[];
  };
  primaryFailure: boolean;
}
export function finishRosterQualification(input: RosterQualificationInput): Promise<Error | null>;
