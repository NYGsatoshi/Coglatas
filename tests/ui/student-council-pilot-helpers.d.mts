export interface PilotScope {
  workspaceId: string; channelId: string; directId: string; directMessageId: string; rootMessageId: string; fileId: string;
  fileSha256?: string; fileSizeBytes?: number;
}
export interface PilotFixture {
  version: number; syntheticOnly: boolean; existingIdsVerified: boolean; inventoryReference: string;
  allowed: PilotScope & { fileSha256: string; fileSizeBytes: number };
  deniedSameTenant: PilotScope; deniedCrossTenant: PilotScope;
  revocation: PilotScope & { userId: string };
  memberUserId: string; operatorUserId: string;
}
export interface PilotConfiguration {
  baseURL: string; commit: string; phase: 'exercise' | 'verify-restart'; scope: 'core'; binding: string;
  accounts: Record<'member' | 'operator' | 'revokee', { email: string; password: string }>;
  fixture: PilotFixture;
}
export function readPilotConfiguration(environment: NodeJS.ProcessEnv, fixture: unknown): PilotConfiguration;
export function pilotOrigin(value: string): string;
export function validateFixture(fixture: unknown): void;
export function validateCheckpoint(value: any, configuration: PilotConfiguration): Record<string, any>;
export function validateRestartRecord(value: unknown, checkpoint: Record<string, any>): unknown;
export function validateExerciseReceipt(value: unknown, checkpoint: Record<string, any>): void;
export function isRedactedDenial(result: { status: number; text: string }, protectedIds?: string[]): boolean;
export function assertBoundOrigin(value: string, expectedOrigin: string): void;
export function scopeReadPaths(scope: Record<string, any>): string[];
export function sanitizedReceipt(configuration: PilotConfiguration, phase: string): Record<string, unknown>;
