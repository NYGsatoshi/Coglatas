import { Injectable, InjectionToken, inject } from '@angular/core';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { createLocalOpaqueId } from '../../core/security/client-id';
import { AuditFilterSnapshot, AuditSavedView } from './admin.types';

export interface AuditViewStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export type AuditSavedViewsStatus =
  | 'ready'
  | 'identityUnavailable'
  | 'storageUnavailable'
  | 'discarded'
  | 'invalidInput';

export interface AuditSavedViewsResult {
  readonly status: AuditSavedViewsStatus;
  readonly views: readonly AuditSavedView[];
}

export const COGLATAS_AUDIT_VIEW_STORAGE = new InjectionToken<AuditViewStorage | null>(
  'COGLATAS_AUDIT_VIEW_STORAGE',
  { providedIn: 'root', factory: browserLocalStorage },
);

const storageVersion = 1;
const maximumViews = 20;
const keyVersion = 'v1';

interface StoredAuditViews {
  readonly version: 1;
  readonly views: readonly AuditSavedView[];
}

/**
 * Browser-local presentation preferences. The strict identity partition and
 * input-only record ensure a saved view is never a cached result or grant.
 */
@Injectable({ providedIn: 'root' })
export class AuditViewPreferenceService {
  private readonly auth = inject(AuthSessionFacade);
  private readonly storage = inject(COGLATAS_AUDIT_VIEW_STORAGE);

  identityKey(): string | null {
    const identity = this.identity();
    return identity ? `${identity.scope}:${identity.userId}` : null;
  }

  load(): AuditSavedViewsResult {
    const identity = this.identity();
    if (!identity) {return { status: 'identityUnavailable', views: [] };}
    if (!this.storage) {return { status: 'storageUnavailable', views: [] };}

    let raw: string | null;
    try {
      raw = this.storage.getItem(this.key(identity));
    } catch {
      return { status: 'storageUnavailable', views: [] };
    }
    if (raw === null) {return { status: 'ready', views: [] };}

    const parsed = parseStoredViews(raw);
    if (parsed) {return { status: 'ready', views: parsed.views };}
    try {
      this.storage.removeItem(this.key(identity));
    } catch {
      return { status: 'storageUnavailable', views: [] };
    }
    return { status: 'discarded', views: [] };
  }

  save(name: string, snapshot: AuditFilterSnapshot): AuditSavedViewsResult {
    const identity = this.identity();
    if (!identity) {return { status: 'identityUnavailable', views: [] };}
    const normalizedName = name.trim();
    const normalizedSnapshot = normalizeSnapshot(snapshot);
    if (!isValidName(normalizedName) || !normalizedSnapshot) {
      return { status: 'invalidInput', views: this.load().views };
    }

    const current = this.load();
    if (current.status !== 'ready' && current.status !== 'discarded') {return current;}
    const existing = current.views.find((view) => view.name.toLowerCase() === normalizedName.toLowerCase());
    if (!existing && current.views.length >= maximumViews) {
      return { status: 'invalidInput', views: current.views };
    }
    const saved: AuditSavedView = {
      id: existing?.id ?? createId(),
      name: normalizedName,
      snapshot: normalizedSnapshot,
    };
    const views = existing
      ? current.views.map((view) => view.id === existing.id ? saved : view)
      : [...current.views, saved];
    return this.write(identity, views, current.views);
  }

  delete(viewId: string): AuditSavedViewsResult {
    const identity = this.identity();
    if (!identity) {return { status: 'identityUnavailable', views: [] };}
    const current = this.load();
    if (current.status !== 'ready') {return current;}
    if (!current.views.some((view) => view.id === viewId)) {
      return { status: 'invalidInput', views: current.views };
    }
    return this.write(identity, current.views.filter((view) => view.id !== viewId), current.views);
  }

  private write(
    identity: { readonly scope: string; readonly userId: string },
    views: readonly AuditSavedView[],
    fallback: readonly AuditSavedView[],
  ): AuditSavedViewsResult {
    if (!this.storage) {return { status: 'storageUnavailable', views: fallback };}
    try {
      this.storage.setItem(this.key(identity), JSON.stringify({ version: storageVersion, views }));
      return { status: 'ready', views };
    } catch {
      return { status: 'storageUnavailable', views: fallback };
    }
  }

  private identity(): { readonly scope: string; readonly userId: string } | null {
    const session = this.auth.session();
    const tenant = session.currentTenant;
    const user = session.currentUser;
    if (session.status !== 'active' || !session.isAuthenticated || !user?.userId || !tenant) {return null;}
    if (tenant.isPlatformScope) {return { scope: 'platform', userId: user.userId };}
    return tenant.isAvailable && tenant.tenantId
      ? { scope: tenant.tenantId, userId: user.userId }
      : null;
  }

  private key(identity: { readonly scope: string; readonly userId: string }): string {
    return `coglatas.audit.saved-views.${keyVersion}:${encodeURIComponent(identity.scope)}:${encodeURIComponent(identity.userId)}`;
  }
}

function parseStoredViews(raw: string): StoredAuditViews | null {
  try {
    const value: unknown = JSON.parse(raw);
    if (!isRecord(value) || !hasExactKeys(value, ['version', 'views']) ||
        value['version'] !== storageVersion || !Array.isArray(value['views']) ||
        value['views'].length > maximumViews) {return null;}
    const views = value['views'].map(parseView);
    if (!views.every((view): view is AuditSavedView => view !== null)) {return null;}
    const ids = new Set(views.map((view) => view.id));
    const names = new Set(views.map((view) => view.name.toLowerCase()));
    return ids.size === views.length && names.size === views.length
      ? { version: storageVersion, views }
      : null;
  } catch {
    return null;
  }
}

function parseView(value: unknown): AuditSavedView | null {
  if (!isRecord(value) || !hasExactKeys(value, ['id', 'name', 'snapshot']) ||
      typeof value['id'] !== 'string' || !/^audit-[A-Za-z0-9-]{8,80}$/u.test(value['id']) ||
      typeof value['name'] !== 'string' || !isValidName(value['name'])) {return null;}
  const snapshot = normalizeSnapshot(value['snapshot']);
  return snapshot ? { id: value['id'], name: value['name'], snapshot } : null;
}

export function normalizeAuditFilterSnapshot(value: AuditFilterSnapshot): AuditFilterSnapshot | null {
  return normalizeSnapshot(value);
}

function normalizeSnapshot(value: unknown): AuditFilterSnapshot | null {
  if (!isRecord(value) || !hasExactKeys(value, ['q', 'severity', 'type', 'actor', 'source', 'status', 'range'])) {return null;}
  const q = normalizeText(value['q'], 200);
  const type = normalizeText(value['type'], 160);
  const actor = normalizeText(value['actor'], 200);
  const source = normalizeText(value['source'], 80);
  if (q === null || type === null || actor === null || source === null ||
      !severityValues.includes(value['severity'] as AuditFilterSnapshot['severity']) ||
      !statusValues.includes(value['status'] as AuditFilterSnapshot['status']) ||
      !rangeValues.includes(value['range'] as AuditFilterSnapshot['range'])) {return null;}
  return {
    q,
    severity: value['severity'] as AuditFilterSnapshot['severity'],
    type,
    actor,
    source,
    status: value['status'] as AuditFilterSnapshot['status'],
    range: value['range'] as AuditFilterSnapshot['range'],
  };
}

function normalizeText(value: unknown, maximum: number): string | null {
  if (typeof value !== 'string') {return null;}
  const normalized = value.trim();
  return normalized.length <= maximum ? normalized : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function hasExactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean {
  const keys = Object.keys(value).sort();
  return keys.length === expected.length && [...expected].sort().every((key, index) => keys[index] === key);
}

function isValidName(value: string): boolean {
  return value.length > 0 && value.length <= 80 && value === value.trim();
}

function createId(): string {
  return createLocalOpaqueId('audit');
}

function browserLocalStorage(): AuditViewStorage | null {
  if (typeof window === 'undefined') {return null;}
  try { return window.localStorage; } catch { return null; }
}

const severityValues: readonly AuditFilterSnapshot['severity'][] = ['', 'info', 'warning', 'critical'];
const statusValues: readonly AuditFilterSnapshot['status'][] = ['', 'success', 'denied', 'failed'];
const rangeValues: readonly AuditFilterSnapshot['range'][] = ['', '24h', '7d', '30d'];
