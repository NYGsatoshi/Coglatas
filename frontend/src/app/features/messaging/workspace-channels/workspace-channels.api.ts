import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';

export type WorkspaceChannelVisibility = 'PublicWithinScope' | 'Private';

export interface WorkspaceChannelItem {
  readonly id: string;
  readonly workspaceId: string;
  readonly name: string;
  readonly visibility: WorkspaceChannelVisibility | 'ParticipantOnly';
  readonly isMember: boolean;
  readonly canJoin: boolean;
  readonly canManage: boolean;
  readonly isDefault: boolean;
}

export interface WorkspaceChannelDirectory {
  readonly workspaceId: string;
  readonly canCreate: boolean;
  readonly items: readonly WorkspaceChannelItem[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

const PAGE_SIZE = 50;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function channelScopeId(value: unknown): string | null {
  return typeof value === 'string' && UUID.test(value) && value !== '00000000-0000-0000-0000-000000000000'
    ? value.toLowerCase() : null;
}

export function validChannelName(value: string): boolean {
  const name = value.trim();
  return name.length > 0 && name.length <= 200 && name.toLowerCase() !== 'general' &&
    !/[\u0000-\u001f\u007f-\u009f]/u.test(name);
}

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid channel response.');
  return value as Record<string, unknown>;
}

export function readChannelItem(value: unknown, expectedWorkspaceId: string): WorkspaceChannelItem {
  const row = object(value);
  const id = channelScopeId(row['id']);
  const workspaceId = channelScopeId(row['workspaceId']);
  const name = row['name'];
  const visibility = row['visibility'];
  if (!id || !workspaceId || workspaceId !== channelScopeId(expectedWorkspaceId) ||
      typeof name !== 'string' || name.length > 200 || typeof visibility !== 'string' ||
      !['PublicWithinScope', 'Private', 'ParticipantOnly'].includes(visibility) ||
      ['isMember', 'canJoin', 'canManage', 'isDefault'].some((key) => typeof row[key] !== 'boolean')) {
    throw new Error('Invalid channel response.');
  }
  const isMember = row['isMember'] === true;
  const canJoin = row['canJoin'] === true;
  const canManage = row['canManage'] === true;
  const isDefault = row['isDefault'] === true;
  if ((visibility !== 'PublicWithinScope' && !isMember) ||
      (canJoin && (isMember || isDefault || visibility !== 'PublicWithinScope')) ||
      (canManage && (!isMember || isDefault)) ||
      (isDefault && (name !== 'general' || visibility !== 'PublicWithinScope'))) {
    throw new Error('Inconsistent channel response.');
  }
  return { id, workspaceId, name, visibility: visibility as WorkspaceChannelItem['visibility'],
    isMember, canJoin, canManage, isDefault };
}

export function readChannelDirectory(value: unknown, workspaceId: string, expectedPage: number): WorkspaceChannelDirectory {
  const body = object(value);
  const totalCount = body['totalCount'];
  if (channelScopeId(body['workspaceId']) !== channelScopeId(workspaceId) || !channelScopeId(workspaceId) ||
      typeof body['canCreate'] !== 'boolean' || body['page'] !== expectedPage || body['pageSize'] !== PAGE_SIZE ||
      typeof totalCount !== 'number' || !Number.isSafeInteger(totalCount) || totalCount < 0 ||
      !Array.isArray(body['items']) || body['items'].length > PAGE_SIZE || body['items'].length > totalCount) {
    throw new Error('Invalid channel directory.');
  }
  const items = body['items'].map((item: unknown) => readChannelItem(item, workspaceId));
  if (new Set(items.map((item) => item.id)).size !== items.length) throw new Error('Duplicate channel identity.');
  return { workspaceId: channelScopeId(workspaceId)!, canCreate: body['canCreate'], items,
    page: expectedPage, pageSize: PAGE_SIZE, totalCount };
}

export function channelHttpStatus(error: unknown): number {
  if (!error || typeof error !== 'object') return 0;
  const candidate = error as { httpStatus?: unknown; status?: unknown };
  return typeof candidate.httpStatus === 'number' ? candidate.httpStatus
    : typeof candidate.status === 'number' ? candidate.status : 0;
}

@Injectable({ providedIn: 'root' })
export class WorkspaceChannelsApi {
  private readonly http = inject(HttpClient);

  list(workspaceId: string, page: number, search: string): Observable<WorkspaceChannelDirectory> {
    return this.http.get<unknown>(this.endpoint(workspaceId), {
      withCredentials: true, params: { page, pageSize: PAGE_SIZE, search }
    }).pipe(map((response) => readChannelDirectory(response, workspaceId, page)));
  }

  create(workspaceId: string, name: string, visibility: WorkspaceChannelVisibility, key: string): Observable<WorkspaceChannelItem> {
    return this.http.post<unknown>(this.endpoint(workspaceId), { name, visibility }, {
      withCredentials: true, headers: { 'Idempotency-Key': key }
    }).pipe(map((response) => {
      const item = readChannelItem(response, workspaceId);
      if (!item.isMember || item.isDefault || item.name !== name.trim() || item.visibility !== visibility) {
        throw new Error('Channel creation was not confirmed.');
      }
      return item;
    }));
  }

  join(workspaceId: string, conversationId: string): Observable<WorkspaceChannelItem> {
    const id = this.requireId(conversationId);
    return this.http.post<unknown>(`${this.endpoint(workspaceId)}/${id}/join`, {}, { withCredentials: true })
      .pipe(map((response) => {
        const item = readChannelItem(response, workspaceId);
        if (item.id !== id || !item.isMember || item.isDefault || item.visibility !== 'PublicWithinScope') {
          throw new Error('Channel participation was not confirmed.');
        }
        return item;
      }));
  }

  invite(workspaceId: string, conversationId: string, recipientUserId: string): Observable<void> {
    const id = this.requireId(conversationId);
    const userId = this.requireId(recipientUserId);
    return this.http.post<unknown>(`${this.endpoint(workspaceId)}/${id}/members`, { userId }, { withCredentials: true })
      .pipe(map((response) => {
        if (object(response)['status'] !== 'OK') throw new Error('Channel membership was not confirmed.');
      }));
  }

  private requireId(value: string): string {
    const id = channelScopeId(value);
    if (!id) throw new Error('Invalid channel scope identity.');
    return id;
  }

  private endpoint(workspaceId: string): string {
    return `/api/workspaces/${this.requireId(workspaceId)}/chat-channels`;
  }
}
