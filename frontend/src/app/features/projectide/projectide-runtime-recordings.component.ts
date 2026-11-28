import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../../core/feature-flags/frontend-feature-flags.service';
import { I18nService } from '../../core/i18n/i18n.service';
import type { PROJECTIDE_ENGLISH_MESSAGES } from '../../core/i18n/messages/projectide.messages';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';

const HOST_STATUSES = ['Completed', 'Cancelled', 'Unsupported', 'InvalidInput', 'InternalError'] as const;
const CAPTURE_MODES = ['Manifest', 'AvailableOutputs'] as const;
const CAPTURE_STATES = [
  'ManifestOnly', 'CapturedAvailableOutputs', 'NoOutputsProduced', 'BodyBudgetExceeded', 'UnavailableBodyProvider',
  'RefusedWrongResult', 'RefusedInputBinding', 'RefusedOutputBinding', 'UnsupportedBodyKind',
] as const;
const EXECUTIONS = ['Completed', 'NotRun', 'Cancelled', 'TimedOut', 'ResourceExhausted', 'InternalError'] as const;
const COVERAGES = ['Complete', 'Partial', 'None'] as const;
const VERDICTS = ['Pass', 'Fail', 'Inconclusive', 'Error', 'NotRun'] as const;

interface RuntimeRecording {
  readonly receiptDigest: string;
  readonly hostStatus: typeof HOST_STATUSES[number];
  readonly captureMode: typeof CAPTURE_MODES[number];
  readonly captureState: typeof CAPTURE_STATES[number] | null;
  readonly result: {
    readonly resultDigest: string;
    readonly execution: typeof EXECUTIONS[number];
    readonly coverage: typeof COVERAGES[number];
    readonly verdict: typeof VERDICTS[number];
    readonly evidenceComplete: boolean;
  } | null;
}

type LookupState =
  | { readonly scope: string; readonly status: 'invalid' | 'loading' | 'unavailable' }
  | { readonly scope: string; readonly status: 'ready'; readonly recording: RuntimeRecording };

@Component({
  selector: 'app-projectide-runtime-recordings',
  templateUrl: './projectide-runtime-recordings.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    form { display: flex; flex-wrap: wrap; align-items: end; gap: .5rem; }
    form > div { min-inline-size: 0; }
    label { display: block; }
    input { max-inline-size: 100%; }
    code { overflow-wrap: anywhere; }
  `,
})
export class ProjectIdeRuntimeRecordingsComponent {
  readonly projectId = input.required<string>();
  private readonly http = inject(HttpClient);
  private readonly flags = inject(FrontendFeatureFlagsService);
  private readonly auth = inject(AuthSessionFacade);
  private readonly workspace = inject(ActiveWorkspaceFacade);
  private readonly i18n = inject(I18nService);
  private readonly selected = signal<{ readonly scope: string; readonly runId: string } | null>(null);
  private readonly lookup = signal<LookupState | null>(null);
  private request?: Subscription;

  readonly scope = computed(() => {
    const projectId = normalizeUuid(this.projectId());
    const tenant = this.auth.currentTenant();
    const userId = this.auth.currentUser()?.userId;
    const workspaceId = this.workspace.activeWorkspace()?.id ?? null;
    if (!this.flags.projectIdeDevelopmentRuntimeEnabled() || !projectId ||
        !this.auth.isAuthenticated() || !tenant?.isAvailable || !userId) {
      return null;
    }

    return JSON.stringify([projectId, tenant.tenantId, userId, workspaceId]);
  });

  readonly runId = computed(() => {
    const selection = this.selected();
    return selection?.scope === this.scope() ? selection.runId : '';
  });

  readonly currentLookup = computed(() => {
    const lookup = this.lookup();
    return lookup?.scope === this.scope() ? lookup : null;
  });

  readonly t = (key: keyof typeof PROJECTIDE_ENGLISH_MESSAGES): string => this.i18n.translate(key);

  constructor() {
    const unregister = inject(RealtimeFacade).registerProtectedStateClearer(
      'projectide-runtime-recordings',
      () => this.clear(),
    );
    inject(DestroyRef).onDestroy(() => {
      unregister();
      this.clear();
    });
    effect(() => {
      const scope = this.scope();
      untracked(() => {
        if (this.lookup()?.scope !== scope) this.cancelLookup();
        if (this.selected()?.scope !== scope) this.selected.set(null);
      });
    });
  }

  setRunId(runId: string): void {
    this.cancelLookup();
    const scope = this.scope();
    this.selected.set(scope ? { scope, runId } : null);
  }

  findRecording(): void {
    const scope = this.scope();
    const projectId = normalizeUuid(this.projectId());
    if (!scope || !projectId || this.currentLookup()?.status === 'loading') return;

    const runId = normalizeUuid(this.runId().trim());
    if (!runId) {
      this.lookup.set({ scope, status: 'invalid' });
      return;
    }

    const pending = { scope, status: 'loading' } as const;
    this.lookup.set(pending);
    const setResult = (recording: RuntimeRecording | null): void => {
      if (this.currentLookup() !== pending) return;
      this.lookup.set(recording ? { scope, status: 'ready', recording } : { scope, status: 'unavailable' });
    };

    this.request = this.http.get<unknown>(`/api/projectide/runtime/projects/${projectId}/compiler-recordings/${runId}`).subscribe({
      next: (body) => setResult(readRecording(body, runId)),
      error: () => setResult(null),
    });
  }

  private cancelLookup(): void {
    this.request?.unsubscribe();
    this.request = undefined;
    this.lookup.set(null);
  }

  private clear(): void {
    this.cancelLookup();
    this.selected.set(null);
  }
}

function normalizeUuid(value: unknown): string | null {
  return typeof value === 'string' && value.length === 36 &&
    /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000' ? value.toLowerCase() : null;
}

function isSha256Digest(value: unknown): value is string {
  return typeof value === 'string' && value.length === 64 && /^[0-9a-f]+$/.test(value);
}

function isToken<T extends string>(value: unknown, tokens: readonly T[]): value is T {
  return tokens.some(candidate => candidate === value);
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readRecording(body: unknown, runId: string): RuntimeRecording | null {
  if (!isObject(body) || Object.keys(body).length !== 6 || normalizeUuid(body['runId']) !== runId ||
      !isSha256Digest(body['receiptDigest']) || !isToken(body['hostStatus'], HOST_STATUSES) ||
      !isToken(body['captureMode'], CAPTURE_MODES)) return null;

  const originalCaptureState = body['captureState'];
  const captureState = isToken(originalCaptureState, CAPTURE_STATES) ? originalCaptureState : null;
  if (originalCaptureState !== null && captureState === null) return null;

  const result = body['result'];
  if (result !== null &&
      (!isObject(result) || Object.keys(result).length !== 5 || !isSha256Digest(result['resultDigest']) ||
        !isToken(result['execution'], EXECUTIONS) || !isToken(result['coverage'], COVERAGES) ||
        !isToken(result['verdict'], VERDICTS) || typeof result['evidenceComplete'] !== 'boolean')) return null;

  return {
    receiptDigest: body['receiptDigest'],
    hostStatus: body['hostStatus'],
    captureMode: body['captureMode'],
    captureState,
    result: result && {
      resultDigest: result['resultDigest'],
      execution: result['execution'],
      coverage: result['coverage'],
      verdict: result['verdict'],
      evidenceComplete: result['evidenceComplete'],
    },
  };
}
