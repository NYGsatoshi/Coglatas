import { HttpClient, HttpParams } from '@angular/common/http';
import { effect, inject, Injectable, InjectionToken, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';

import {
  ADMIN_DEFAULT_PAGE_SIZE,
  ADMIN_MAXIMUM_PAGE_SIZE,
  AUDIT_TYPED_FIELD_NOTE,
  AdminPageStatus,
  AuditFilterSnapshot,
  AuditGridRow,
  AuditCapabilityViewModel,
  AuditDetailViewModel,
  AuditLogScenario,
  AuditLogViewModel,
  AuditMockRecord,
  AuditResultDisplay,
  AuditSeverityDisplay,
  AuditSensitiveMetadataViewModel,
  EMPTY_AUDIT_FILTERS,
  EXPORT_AUTHORIZATION_NOTE,
  ExportDiagnosticsScenario,
  ExportDiagnosticsViewModel,
  ExportJobGridRow,
  ExportJobMockRecord,
  ExportJobResult,
  ExportJobStatus,
} from './admin.types';

export const COGLATAS_ADMIN_AUDIT_MOCK = new InjectionToken<AuditLogScenario>('COGLATAS_ADMIN_AUDIT_MOCK');
export const COGLATAS_EXPORT_DIAGNOSTICS_MOCK = new InjectionToken<ExportDiagnosticsScenario>(
  'COGLATAS_EXPORT_DIAGNOSTICS_MOCK',
);

const severityLabels: Record<AuditSeverityDisplay, string> = {
  info: 'Info',
  warning: 'Warning',
  critical: 'Critical',
  unclassified: 'Unrecognized severity',
};

const resultLabels: Record<AuditResultDisplay, string> = {
  success: 'Success',
  denied: 'Denied',
  failed: 'Failed',
  unclassified: 'Unrecognized result',
};

const statusLabels: Record<ExportJobStatus, string> = {
  pending: 'Pending',
  running: 'Running',
  succeeded: 'Succeeded',
  failed: 'Failed',
};

const exportResultLabels: Record<ExportJobResult, string> = {
  notReady: 'Not ready',
  available: 'Available after server check',
  failed: 'Failed',
  suppressed: 'Suppressed',
};

interface PagedResponseDto<T> {
  readonly items?: readonly T[];
  readonly page?: unknown;
  readonly pageSize?: unknown;
  readonly totalCount?: unknown;
}

interface AuditLogDto {
  readonly id: string;
  readonly createdAt: string;
  readonly action: string;
  readonly actorDisplayName: string;
  readonly targetType: string;
  readonly workspaceLabel?: string | null;
  readonly severity: unknown;
  readonly result: unknown;
  readonly summary: string;
  readonly requestId?: string | null;
}

interface AuditCapabilityDto {
  readonly canViewSensitiveMetadata?: unknown;
}

interface AuditSensitiveMetadataDto {
  readonly auditId?: unknown;
  readonly metadata?: unknown;
  readonly redactionApplied?: unknown;
}

@Injectable({
  providedIn: 'root',
})
export class AdminFacade {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthSessionFacade);
  private readonly realtime = inject(RealtimeFacade);
  private readonly auditScenario = inject(COGLATAS_ADMIN_AUDIT_MOCK, { optional: true });
  private readonly exportScenario = inject(COGLATAS_EXPORT_DIAGNOSTICS_MOCK, { optional: true });
  private readonly auditState = signal<AuditLogViewModel>(
    this.auditScenario ? this.auditFromScenario(this.auditScenario) : this.emptyAudit('loading'),
  );
  private readonly auditDetailState = signal<AuditDetailViewModel>(this.emptyAuditDetail());
  private readonly auditCapabilityState = signal<AuditCapabilityViewModel>({
    loaded: this.auditScenario !== null,
    canViewSensitiveMetadata: this.auditScenario?.canViewSensitiveMetadata === true,
  });
  private readonly auditSensitiveMetadataState = signal<AuditSensitiveMetadataViewModel>(
    this.emptyAuditSensitiveMetadata(),
  );
  private readonly exportState = signal<ExportDiagnosticsViewModel>(
    this.exportScenario
      ? this.exportFromScenario(this.exportScenario)
      : this.emptyExportDiagnostics(),
  );
  private auditLogRequestVersion = 0;
  private auditLogRequestInFlight = false;
  private auditLogSubscription?: Subscription;
  private auditInitialized = false;
  private appliedAuditFilters: AuditFilterSnapshot = EMPTY_AUDIT_FILTERS;
  private appliedAuditFromDate: string | null = null;
  private auditDetailRequestVersion = 0;
  private auditDetailSubscription?: Subscription;
  private auditCapabilityRequestVersion = 0;
  private auditCapabilitySubscription?: Subscription;
  private auditSensitiveMetadataRequestVersion = 0;
  private auditSensitiveMetadataSubscription?: Subscription;
  private auditProtectedStateRegistered = false;

  constructor() {
    if (!this.auditScenario) {
      this.registerAuditProtectedStateWhenAuthenticated();
      effect(() => {
        this.auth.session();
        this.registerAuditProtectedStateWhenAuthenticated();
      });
      this.auditInitialized = true;
      this.loadAuditLog('initial');
    }
  }

  getAuditLog(): AuditLogViewModel {
    return this.auditState();
  }

  initializeAuditLog(filters: AuditFilterSnapshot = EMPTY_AUDIT_FILTERS): void {
    if (this.auditScenario) {return;}
    const normalized = normalizeFilters(filters);
    if (this.auditInitialized && filtersEqual(normalized, this.appliedAuditFilters)) {return;}
    this.applyAuditFilters(normalized);
  }

  applyAuditFilters(filters: AuditFilterSnapshot): void {
    if (this.auditScenario) {return;}
    const normalized = normalizeFilters(filters);
    this.auditInitialized = true;
    this.appliedAuditFilters = normalized;
    this.appliedAuditFromDate = auditRangeFromDate(normalized.range);
    this.cancelAuditLogRequest();
    this.loadAuditLog('initial');
  }

  reloadAuditLog(): void {
    const current = this.auditState();
    if (!this.auditScenario && !this.auditLogRequestInFlight && current.status === 'error' && current.canRetry) {
      this.loadAuditLog('retry');
    }
  }

  getAuditDetail(): AuditDetailViewModel {
    return this.auditDetailState();
  }

  getAuditCapabilities(): AuditCapabilityViewModel {
    return this.auditCapabilityState();
  }

  getAuditSensitiveMetadata(): AuditSensitiveMetadataViewModel {
    return this.auditSensitiveMetadataState();
  }

  selectAuditDetail(auditId: string): void {
    const current = this.auditDetailState();
    if (current.auditId === auditId && (current.status === 'loading' || current.status === 'ready')) {
      return;
    }

    this.clearAuditSensitiveMetadata(auditId);

    if (this.auditScenario) {
      const record = this.auditScenario.auditRecords.find((item) => item.id === auditId);
      this.auditDetailState.set(record
        ? { status: 'ready', auditId, row: this.toAuditGridRow(record) }
        : {
            status: 'notFound',
            auditId,
            row: null,
            message: 'The selected audit event is unavailable.',
          });
      return;
    }

    this.loadAuditCapabilities();

    const requestVersion = ++this.auditDetailRequestVersion;
    this.auditDetailState.set({ status: 'loading', auditId, row: null });
    this.auditDetailSubscription?.unsubscribe();
    this.auditDetailSubscription = this.http
      .get<AuditLogDto>(`/api/admin/audit-grid/${encodeURIComponent(auditId)}`, { withCredentials: true })
      .subscribe({
        next: (record) => {
          if (requestVersion !== this.auditDetailRequestVersion) {
            return;
          }

          this.auditDetailState.set({
            status: 'ready',
            auditId,
            row: this.toAuditGridRow(this.toAuditRecord(record)),
          });
        },
        error: (error: { status?: number }) => {
          if (requestVersion !== this.auditDetailRequestVersion) {
            return;
          }

          if (error.status === 404) {
            this.auditDetailState.set({
              status: 'notFound',
              auditId,
              row: null,
              message: 'The selected audit event is unavailable.',
            });
            return;
          }

          this.auditDetailState.set({
            status: error.status === 401 || error.status === 403 ? 'permissionDenied' : 'error',
            auditId,
            row: null,
            message: error.status === 401 || error.status === 403
              ? 'Audit permission is required to view this event.'
              : 'The selected audit event could not be loaded.',
          });
        },
      });
  }

  clearAuditDetail(): void {
    this.auditDetailSubscription?.unsubscribe();
    this.auditDetailSubscription = undefined;
    this.auditDetailRequestVersion += 1;
    this.auditDetailState.set(this.emptyAuditDetail());
    this.clearAuditSensitiveMetadata();
  }

  revealAuditSensitiveMetadata(auditId: string): void {
    const capability = this.auditCapabilityState();
    const detail = this.auditDetailState();
    if (
      this.auditScenario ||
      !capability.loaded ||
      !capability.canViewSensitiveMetadata ||
      detail.status !== 'ready' ||
      detail.auditId !== auditId
    ) {
      return;
    }

    const current = this.auditSensitiveMetadataState();
    if (current.auditId === auditId && (current.status === 'loading' || current.status === 'ready')) {
      return;
    }

    this.auditSensitiveMetadataSubscription?.unsubscribe();
    const requestVersion = ++this.auditSensitiveMetadataRequestVersion;
    this.auditSensitiveMetadataState.set({
      status: 'loading',
      auditId,
      formattedJson: '',
      redactionApplied: false,
    });
    this.auditSensitiveMetadataSubscription = this.http
      .get<AuditSensitiveMetadataDto>(
        `/api/admin/audit-grid/${encodeURIComponent(auditId)}/sensitive-metadata`,
        { withCredentials: true },
      )
      .subscribe({
        next: (response) => {
          if (requestVersion !== this.auditSensitiveMetadataRequestVersion) {
            return;
          }

          const metadata = toJsonObject(response.metadata);
          if (
            response.auditId !== auditId ||
            metadata === null ||
            typeof response.redactionApplied !== 'boolean'
          ) {
            this.auditSensitiveMetadataState.set(this.auditSensitiveMetadataError(auditId));
            return;
          }

          const formattedJson = JSON.stringify(metadata, null, 2);
          this.auditSensitiveMetadataState.set({
            status: Object.keys(metadata).length === 0 ? 'empty' : 'ready',
            auditId,
            formattedJson,
            redactionApplied: response.redactionApplied,
          });
        },
        error: (error: { status?: number }) => {
          if (requestVersion !== this.auditSensitiveMetadataRequestVersion) {
            return;
          }

          const status = error.status === 401 || error.status === 403
            ? 'permissionDenied'
            : error.status === 404
              ? 'notFound'
              : 'error';
          const message = status === 'permissionDenied'
            ? 'Sensitive audit metadata access is unavailable.'
            : status === 'notFound'
              ? 'Sensitive metadata for this audit event is unavailable.'
              : 'Sensitive audit metadata could not be loaded.';
          this.auditSensitiveMetadataState.set({
            status,
            auditId,
            formattedJson: '',
            redactionApplied: false,
            message,
          });
        },
      });
  }

  hideAuditSensitiveMetadata(): void {
    const auditId = this.auditDetailState().auditId;
    this.clearAuditSensitiveMetadata(auditId);
  }

  getExportDiagnostics(): ExportDiagnosticsViewModel {
    return this.exportState();
  }

  private loadAuditLog(loadPhase: Extract<AuditLogViewModel['loadPhase'], 'initial' | 'retry'>): void {
    if (this.auditLogRequestInFlight) {
      return;
    }

    const requestVersion = ++this.auditLogRequestVersion;
    this.auditLogRequestInFlight = true;
    const filters = this.appliedAuditFilters;
    const fromDate = this.appliedAuditFromDate;
    this.auditState.set({
      ...this.emptyAudit('loading', filters),
      loadPhase,
    });
    this.auditLogSubscription = this.http
      .get<PagedResponseDto<AuditLogDto>>('/api/admin/audit-grid', {
        withCredentials: true,
        params: auditFilterParams(filters, fromDate),
      })
      .subscribe({
        next: (response) => {
          if (requestVersion !== this.auditLogRequestVersion) {
            return;
          }

          this.auditLogRequestInFlight = false;
          const rows = (response.items ?? []).map((record) =>
            this.toAuditGridRow(this.toAuditRecord(record)),
          );
          const totalCount = isNonNegativeInteger(response.totalCount) && response.totalCount >= rows.length
            ? response.totalCount
            : rows.length;
          this.auditState.set({
            ...this.emptyAudit(rows.length === 0 ? 'empty' : 'ready', filters),
            rows,
            totalCount,
            message: rows.length === 0
              ? hasActiveAuditFilters(filters)
                ? 'No audit entries match the applied filters.'
                : 'No audit records were returned by the API.'
              : undefined,
          });
        },
        error: (error: { status?: number }) => {
          if (requestVersion !== this.auditLogRequestVersion) {
            return;
          }

          this.auditLogRequestInFlight = false;
          const permissionDenied = error.status === 401 || error.status === 403;
          this.auditState.set({
            ...this.emptyAudit(
              permissionDenied ? 'permissionDenied' : 'error',
              filters,
            ),
            canRetry: !permissionDenied && isRetryableAuditListError(error.status),
            message:
              permissionDenied
                ? 'Authentication or audit permission is required.'
                : 'Audit log API request failed.',
          });
        },
      });
  }

  private loadAuditCapabilities(): void {
    const requestVersion = ++this.auditCapabilityRequestVersion;
    this.auditCapabilityState.set({ loaded: false, canViewSensitiveMetadata: false });
    this.auditCapabilitySubscription?.unsubscribe();
    this.auditCapabilitySubscription = this.http
      .get<AuditCapabilityDto>('/api/audit/capabilities', { withCredentials: true })
      .subscribe({
        next: (response) => {
          if (requestVersion !== this.auditCapabilityRequestVersion) {
            return;
          }

          this.auditCapabilityState.set({
            loaded: true,
            canViewSensitiveMetadata: response.canViewSensitiveMetadata === true,
          });
        },
        error: () => {
          if (requestVersion !== this.auditCapabilityRequestVersion) {
            return;
          }

          this.auditCapabilityState.set({ loaded: true, canViewSensitiveMetadata: false });
        },
      });
  }

  private auditFromScenario(scenario: AuditLogScenario): AuditLogViewModel {
    return {
      status: scenario.status,
      loadPhase: scenario.loadPhase ?? (scenario.status === 'loading' ? 'initial' : 'idle'),
      canRetry: scenario.canRetry ?? false,
      title: scenario.title,
      subtitle: scenario.subtitle,
      rows: scenario.auditRecords.map((record) => this.toAuditGridRow(record)),
      totalCount: scenario.auditRecords.length,
      appliedFilters: EMPTY_AUDIT_FILTERS,
      columns: [],
      pageSize: {
        defaultPageSize: ADMIN_DEFAULT_PAGE_SIZE,
        maximumPageSize: ADMIN_MAXIMUM_PAGE_SIZE,
      },
      typedFieldNote: AUDIT_TYPED_FIELD_NOTE,
      message: scenario.message,
    };
  }

  private exportFromScenario(scenario: ExportDiagnosticsScenario): ExportDiagnosticsViewModel {
    return {
      status: scenario.status,
      title: scenario.title,
      subtitle: scenario.subtitle,
      rows: scenario.exportJobs.map((job) => this.toExportJobGridRow(job)),
      columns: [],
      pageSize: {
        defaultPageSize: ADMIN_DEFAULT_PAGE_SIZE,
        maximumPageSize: ADMIN_MAXIMUM_PAGE_SIZE,
      },
      canRequestDiagnosticsExport: scenario.canRequestDiagnosticsExport,
      authorizationNote: EXPORT_AUTHORIZATION_NOTE,
      initialSelectedJobId: scenario.initialSelectedJobId,
      message: scenario.message,
    };
  }

  private emptyAudit(
    status: AdminPageStatus,
    appliedFilters: AuditFilterSnapshot = EMPTY_AUDIT_FILTERS,
  ): AuditLogViewModel {
    return {
      status,
      loadPhase: status === 'loading' ? 'initial' : 'idle',
      canRetry: false,
      title: 'Audit log',
      subtitle: 'Live API data',
      rows: [],
      totalCount: 0,
      appliedFilters,
      columns: [],
      pageSize: {
        defaultPageSize: ADMIN_DEFAULT_PAGE_SIZE,
        maximumPageSize: ADMIN_MAXIMUM_PAGE_SIZE,
      },
      typedFieldNote: AUDIT_TYPED_FIELD_NOTE,
    };
  }

  private emptyAuditDetail(): AuditDetailViewModel {
    return { status: 'idle', auditId: null, row: null };
  }

  private emptyAuditSensitiveMetadata(auditId: string | null = null): AuditSensitiveMetadataViewModel {
    return {
      status: 'hidden',
      auditId,
      formattedJson: '',
      redactionApplied: false,
    };
  }

  private auditSensitiveMetadataError(auditId: string): AuditSensitiveMetadataViewModel {
    return {
      status: 'error',
      auditId,
      formattedJson: '',
      redactionApplied: false,
      message: 'Sensitive audit metadata could not be loaded.',
    };
  }

  private clearAuditSensitiveMetadata(auditId: string | null = null): void {
    this.auditSensitiveMetadataSubscription?.unsubscribe();
    this.auditSensitiveMetadataSubscription = undefined;
    this.auditSensitiveMetadataRequestVersion += 1;
    this.auditSensitiveMetadataState.set(this.emptyAuditSensitiveMetadata(auditId));
  }

  private cancelAuditLogRequest(): void {
    this.auditLogSubscription?.unsubscribe();
    this.auditLogSubscription = undefined;
    this.auditLogRequestVersion += 1;
    this.auditLogRequestInFlight = false;
  }

  private clearAuditProtectedState(): void {
    this.cancelAuditLogRequest();
    this.auditDetailSubscription?.unsubscribe();
    this.auditDetailSubscription = undefined;
    this.auditCapabilitySubscription?.unsubscribe();
    this.auditCapabilitySubscription = undefined;
    this.auditDetailRequestVersion += 1;
    this.auditCapabilityRequestVersion += 1;
    this.appliedAuditFilters = EMPTY_AUDIT_FILTERS;
    this.appliedAuditFromDate = null;
    this.auditInitialized = false;
    this.auditState.set(this.emptyAudit('permissionDenied', EMPTY_AUDIT_FILTERS));
    this.auditDetailState.set(this.emptyAuditDetail());
    this.auditCapabilityState.set({ loaded: false, canViewSensitiveMetadata: false });
    this.clearAuditSensitiveMetadata();
  }

  private registerAuditProtectedStateWhenAuthenticated(): void {
    const session = this.auth.session();
    if (this.auditProtectedStateRegistered || session.status !== 'active' || !session.isAuthenticated) {return;}
    this.realtime.registerProtectedStateClearer?.(
      'admin-audit',
      () => this.clearAuditProtectedState(),
    );
    this.auditProtectedStateRegistered = true;
  }

  private emptyExportDiagnostics(): ExportDiagnosticsViewModel {
    return {
      status: 'empty',
      title: 'Export diagnostics not available in MVP0',
      subtitle: 'Disabled for MVP0',
      rows: [],
      columns: [],
      pageSize: {
        defaultPageSize: ADMIN_DEFAULT_PAGE_SIZE,
        maximumPageSize: ADMIN_MAXIMUM_PAGE_SIZE,
      },
      canRequestDiagnosticsExport: false,
      authorizationNote: EXPORT_AUTHORIZATION_NOTE,
      message: 'Export diagnostics are not implemented for MVP0. Requests, job history, and downloads are disabled.',
    };
  }

  private toAuditGridRow(record: AuditMockRecord): AuditGridRow {
    return {
      id: record.id,
      createdAt: record.createdAt,
      action: record.action,
      actorDisplay: record.actorDisplay,
      targetType: record.targetType,
      workspace: record.workspace,
      severity: record.severity,
      severityLabel: severityLabels[record.severity],
      result: record.result,
      resultLabel: resultLabels[record.result],
      summary: record.summary,
      requestId: record.requestId,
      redactedDetails: record.redactedDetails,
    };
  }

  private toExportJobGridRow(job: ExportJobMockRecord): ExportJobGridRow {
    return {
      id: job.id,
      createdAt: job.createdAt,
      jobType: job.jobType,
      status: job.status,
      statusLabel: statusLabels[job.status],
      requestedBy: job.requestedBy,
      scope: job.scope,
      result: job.result,
      resultLabel: exportResultLabels[job.result],
      requestId: job.requestId,
      redactedDetails: job.redactedDetails,
    };
  }

  private toAuditRecord(record: AuditLogDto): AuditMockRecord {
    return {
      id: record.id,
      createdAt: formatDate(record.createdAt),
      action: record.action,
      actorDisplay: record.actorDisplayName,
      targetType: record.targetType,
      workspace: record.workspaceLabel ?? '',
      severity: toAuditSeverity(record.severity),
      result: toAuditResult(record.result),
      summary: record.summary,
      requestId: record.requestId ?? '',
      redactedDetails: [],
      rawMetadataProbeNeverRender: '',
    };
  }
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function formatDate(value: unknown): string {
  const raw = stringValue(value);
  return raw ? new Date(raw).toLocaleString() : '';
}

function toAuditSeverity(value: unknown): AuditSeverityDisplay {
  return value === 'info' || value === 'warning' || value === 'critical'
    ? value
    : 'unclassified';
}

function toAuditResult(value: unknown): AuditResultDisplay {
  return value === 'success' || value === 'denied' || value === 'failed'
    ? value
    : 'unclassified';
}

function isRetryableAuditListError(status: number | undefined): boolean {
  return status === undefined || status === 0 || status === 408 || status === 429 || (status >= 500 && status <= 599);
}

function toJsonObject(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function normalizeFilters(filters: AuditFilterSnapshot): AuditFilterSnapshot {
  return {
    q: filters.q.trim(),
    severity: filters.severity,
    type: filters.type.trim(),
    actor: filters.actor.trim(),
    source: filters.source.trim(),
    status: filters.status,
    range: filters.range,
  };
}

function filtersEqual(left: AuditFilterSnapshot, right: AuditFilterSnapshot): boolean {
  return left.q === right.q &&
    left.severity === right.severity &&
    left.type === right.type &&
    left.actor === right.actor &&
    left.source === right.source &&
    left.status === right.status &&
    left.range === right.range;
}

function auditFilterParams(filters: AuditFilterSnapshot, fromDate: string | null): HttpParams {
  let params = new HttpParams();
  if (filters.q) {params = params.set('q', filters.q);}
  if (filters.severity) {params = params.set('severity', filters.severity);}
  if (filters.type) {params = params.set('action', filters.type);}
  if (filters.actor) {params = params.set('actor', filters.actor);}
  if (filters.source) {params = params.set('entityType', filters.source);}
  if (filters.status) {params = params.set('result', filters.status);}
  return fromDate ? params.set('fromDate', fromDate) : params;
}

function auditRangeFromDate(range: AuditFilterSnapshot['range']): string | null {
  const durationMs = range === '24h'
    ? 24 * 60 * 60 * 1000
    : range === '7d'
      ? 7 * 24 * 60 * 60 * 1000
      : range === '30d'
        ? 30 * 24 * 60 * 60 * 1000
        : 0;
  return durationMs > 0 ? new Date(Date.now() - durationMs).toISOString() : null;
}

function hasActiveAuditFilters(filters: AuditFilterSnapshot): boolean {
  return Object.values(filters).some((value) => value !== '');
}

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0;
}
