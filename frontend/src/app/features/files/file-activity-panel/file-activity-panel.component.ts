import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnChanges,
  OnDestroy,
  SimpleChanges,
  inject,
  signal,
} from '@angular/core';
import { Subscription } from 'rxjs';

import { normalizeApiError } from '../../../core/api/api-error.adapter';
import { I18nService } from '../../../core/i18n/i18n.service';
import { FileViewModel } from '../files.types';

type FileActivityKind = 'uploaded' | 'versionCreated' | 'sharingChanged';
type ActivityState = 'idle' | 'loading' | 'ready' | 'empty' | 'error';
type VersionPreviewState = 'idle' | 'loading' | 'ready' | 'unsupported' | 'error';
type VersionPreviewRenderer = 'image' | 'pdf' | 'video' | 'text' | 'unsupported';

const PREVIEW_POLICY = {
  matches: (renderer: VersionPreviewRenderer, contentType: string): boolean => {
    const normalized = contentType.toLowerCase().split(';', 1)[0]?.trim() ?? '';
    return (renderer === 'image' && normalized.startsWith('image/')) ||
      (renderer === 'pdf' && normalized === 'application/pdf') ||
      (renderer === 'video' && normalized.startsWith('video/')) ||
      (renderer === 'text' && (
        normalized.startsWith('text/') || [
          'application/json',
          'application/ndjson',
          'application/xml',
          'application/x-ndjson',
          'application/x-yaml',
          'application/yaml',
        ].includes(normalized)
      ));
  },
  textPreviewMaxBytes: 512 * 1024,
} as const;

interface FileActivityVersion {
  readonly versionId: string;
  readonly versionNumber: number;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly createdAt: string;
  readonly isCurrent: boolean;
}

interface FileActivitySharing {
  readonly change: 'policyChanged' | 'recipientGranted' | 'recipientRevoked' | 'changed';
  readonly accessState: 'private' | 'workspace' | 'unavailable';
  readonly sharingVersion?: number;
}

interface FileActivityEntry {
  readonly id: string;
  readonly kind: FileActivityKind;
  readonly actorDisplayName: string;
  readonly occurredAt: string;
  readonly version?: FileActivityVersion;
  readonly sharing?: FileActivitySharing;
}

@Component({
  selector: 'app-file-activity-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="activity" data-testid="files-activity-view">
      <div class="activity__heading">
        <div>
          <h3>{{ i18n.translate('files.activity.title') }}</h3>
          <p>{{ text('Version and sharing history for this file.', 'このファイルのバージョン履歴と共有変更です。') }}</p>
        </div>
        @if (state() === 'ready' || state() === 'empty') {
          <button type="button" class="activity__refresh" (click)="load()" data-testid="files-activity-refresh">
            {{ text('Refresh', '再読み込み') }}
          </button>
        }
      </div>

      @if (state() === 'loading') {
        <p role="status" aria-live="polite" data-testid="files-activity-loading">
          {{ text('Loading activity…', 'アクティビティを読み込んでいます…') }}
        </p>
      } @else if (state() === 'error') {
        <div class="activity__message" role="alert" data-testid="files-activity-error">
          <p>{{ message() }}</p>
          <button type="button" (click)="load()">{{ text('Try again', '再試行') }}</button>
        </div>
      } @else if (state() === 'empty') {
        <p data-testid="files-activity-empty">{{ i18n.translate('files.activity.empty') }}</p>
      } @else if (state() === 'ready') {
        <ol class="activity__timeline" data-testid="files-activity-timeline">
          @for (entry of entries(); track entry.id) {
            <li class="activity__event" [attr.data-activity-kind]="entry.kind">
              <span class="activity__marker" aria-hidden="true"></span>
              <div class="activity__event-body">
                <div class="activity__event-header">
                  <strong>{{ actionLabel(entry) }}</strong>
                  <time [attr.datetime]="entry.occurredAt">{{ formatDate(entry.occurredAt) }}</time>
                </div>
                <p class="activity__actor">
                  {{ text('By', '操作者') }}: {{ entry.actorDisplayName }}
                </p>

                @if (entry.version; as version) {
                  <div class="activity__version" data-testid="files-activity-version-entry">
                    <div>
                      <span class="activity__version-label">
                        {{ text('Version', 'バージョン') }} {{ version.versionNumber }}
                        @if (version.isCurrent) {
                          <span class="activity__current">{{ text('Current', '現在') }}</span>
                        }
                      </span>
                      <span>{{ version.fileName }}</span>
                      <span>{{ formatSize(version.sizeBytes) }} · {{ version.contentType }}</span>
                    </div>
                    <button
                      type="button"
                      (click)="viewVersion(version)"
                      [disabled]="versionPreviewState() === 'loading' && viewingVersionId() === version.versionId"
                      data-testid="files-activity-view-version"
                    >
                      {{ versionPreviewState() === 'loading' && viewingVersionId() === version.versionId
                        ? text('Opening…', '表示中…')
                        : text('View', '表示') }}
                    </button>
                  </div>
                }

                @if (entry.sharing; as sharing) {
                  <p class="activity__sharing" data-testid="files-activity-sharing-entry">
                    {{ sharingLabel(sharing) }}
                    @if (sharing.sharingVersion) {
                      <span> · {{ text('Sharing revision', '共有リビジョン') }} {{ sharing.sharingVersion }}</span>
                    }
                  </p>
                }
              </div>
            </li>
          }
        </ol>
      }

      <p class="activity__privacy-note">{{ i18n.translate('files.activity.note') }}</p>

      @if (viewingVersion()) {
        <section class="version-preview" aria-labelledby="file-version-preview-title" data-testid="files-version-preview">
          <header>
            <div>
              <p>{{ text('Historical version', '過去バージョン') }}</p>
              <h4 id="file-version-preview-title">
                {{ text('Version', 'バージョン') }} {{ viewingVersion()?.versionNumber }} · {{ viewingVersion()?.fileName }}
              </h4>
            </div>
            <button type="button" (click)="closeVersionPreview()">{{ i18n.translate('common.close') }}</button>
          </header>

          @if (versionPreviewState() === 'loading') {
            <p role="status" aria-live="polite">{{ text('Loading this version…', 'このバージョンを読み込んでいます…') }}</p>
          } @else if (versionPreviewState() === 'error') {
            <p role="alert">{{ versionPreviewMessage() }}</p>
          } @else if (versionPreviewState() === 'unsupported') {
            <p>{{ text('Preview is not available for this file type.', 'このファイル形式はプレビューに対応していません。') }}</p>
          } @else if (versionPreviewState() === 'ready') {
            @switch (versionRenderer()) {
              @case ('image') {
                <img [src]="versionObjectUrl()" [alt]="viewingVersion()?.fileName ?? ''" />
              }
              @case ('pdf') {
                <a
                  [href]="versionObjectUrl()"
                  target="_blank"
                  rel="noopener"
                  data-testid="files-version-preview-pdf-link"
                >
                  {{ text('Open PDF version', 'PDFバージョンを開く') }}
                </a>
              }
              @case ('video') {
                <video [src]="versionObjectUrl()" controls preload="metadata"></video>
              }
              @case ('text') {
                <pre>{{ versionText() }}</pre>
              }
            }
          }
        </section>
      }
    </div>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .activity { display: grid; gap: 0.9rem; }
    .activity__heading, .activity__event-header, .version-preview header {
      display: flex; align-items: flex-start; justify-content: space-between; gap: 0.75rem;
    }
    .activity__heading h3, .version-preview h4 { margin: 0; }
    .activity__heading p, .version-preview header p { margin: 0.2rem 0 0; color: var(--coglatas-color-text-muted); }
    .activity__refresh, .activity__version button, .activity__message button, .version-preview button {
      border: 1px solid var(--coglatas-color-border-default); background: var(--coglatas-color-bg-control); border-radius: 0.5rem;
      padding: 0.45rem 0.7rem; cursor: pointer;
    }
    .activity__timeline { list-style: none; margin: 0; padding: 0; display: grid; gap: 0; }
    .activity__event { position: relative; display: grid; grid-template-columns: 1rem minmax(0, 1fr); gap: 0.65rem; padding-bottom: 1rem; }
    .activity__event:not(:last-child)::before { content: ''; position: absolute; left: 0.34rem; top: 0.8rem; bottom: -0.1rem; width: 1px; background: var(--coglatas-color-border-default); }
    .activity__marker { width: 0.7rem; height: 0.7rem; margin-top: 0.3rem; border-radius: 999px; background: currentColor; opacity: 0.65; z-index: 1; }
    .activity__event-body { min-width: 0; display: grid; gap: 0.35rem; }
    .activity__event-header time, .activity__actor, .activity__privacy-note { color: var(--coglatas-color-text-muted); font-size: 0.85rem; }
    .activity__actor, .activity__sharing, .activity__privacy-note { margin: 0; }
    .activity__version { display: flex; justify-content: space-between; gap: 0.75rem; padding: 0.7rem; border: 1px solid var(--coglatas-color-border-default); border-radius: 0.6rem; }
    .activity__version > div { min-width: 0; display: grid; gap: 0.15rem; }
    .activity__version > div > span { overflow-wrap: anywhere; }
    .activity__version-label { font-weight: 600; }
    .activity__current { margin-left: 0.35rem; font-size: 0.75rem; font-weight: 600; padding: 0.1rem 0.35rem; border-radius: 999px; background: var(--coglatas-color-bg-surface-subtle); }
    .activity__privacy-note { padding-top: 0.75rem; border-top: 1px solid var(--coglatas-color-border-default); }
    .version-preview { display: grid; gap: 0.75rem; padding-top: 0.9rem; border-top: 1px solid var(--coglatas-color-border-default); }
    .version-preview img, .version-preview video { display: block; width: 100%; max-height: 26rem; border: 0; border-radius: 0.5rem; object-fit: contain; background: #fff; }
    .version-preview pre { max-height: 26rem; overflow: auto; margin: 0; padding: 0.75rem; white-space: pre-wrap; overflow-wrap: anywhere; border-radius: 0.5rem; background: var(--coglatas-color-bg-surface-subtle); }
    @media (max-width: 520px) {
      .activity__event-header, .activity__version { align-items: stretch; flex-direction: column; }
      .activity__version button { align-self: flex-start; }
    }
  `],
})
export class FileActivityPanelComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) file!: FileViewModel;

  private readonly http = inject(HttpClient);
  readonly i18n = inject(I18nService);

  readonly state = signal<ActivityState>('idle');
  readonly entries = signal<readonly FileActivityEntry[]>([]);
  readonly message = signal('');
  readonly viewingVersion = signal<FileActivityVersion | null>(null);
  readonly viewingVersionId = signal<string | null>(null);
  readonly versionPreviewState = signal<VersionPreviewState>('idle');
  readonly versionRenderer = signal<VersionPreviewRenderer>('unsupported');
  readonly versionObjectUrl = signal<string | null>(null);
  readonly versionText = signal('');
  readonly versionPreviewMessage = signal('');

  private activityRequest: Subscription | null = null;
  private versionRequest: Subscription | null = null;
  private activityGeneration = 0;
  private versionGeneration = 0;
  private objectUrl: string | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['file']) {
      this.closeVersionPreview();
      this.load();
    }
  }

  ngOnDestroy(): void {
    this.cancelActivityRequest();
    this.closeVersionPreview();
  }

  load(): void {
    this.cancelActivityRequest();
    const fileObjectId = normalizeIdentity(this.file?.canonicalFileId);
    if (!fileObjectId) {
      this.entries.set([]);
      this.state.set('error');
      this.message.set(this.text('File activity is unavailable.', 'ファイルのアクティビティを表示できません。'));
      return;
    }

    const generation = ++this.activityGeneration;
    this.state.set('loading');
    this.message.set('');
    const request = this.http.get<unknown>(`/api/files/${encodeURIComponent(fileObjectId)}/activity`, {
      withCredentials: true,
    }).subscribe({
      next: (response) => {
        if (generation !== this.activityGeneration || normalizeIdentity(this.file?.canonicalFileId) !== fileObjectId) {
          return;
        }
        this.activityRequest = null;
        const mapped = mapActivityResponse(response, fileObjectId);
        if (!mapped) {
          this.entries.set([]);
          this.state.set('error');
          this.message.set(this.text('The activity response was invalid.', 'アクティビティの応答が不正です。'));
          return;
        }
        this.entries.set(mapped);
        this.state.set(mapped.length > 0 ? 'ready' : 'empty');
      },
      error: (error: unknown) => {
        if (generation !== this.activityGeneration) {
          return;
        }
        this.activityRequest = null;
        this.entries.set([]);
        const normalized = normalizeApiError(error);
        this.state.set('error');
        this.message.set([401, 403, 404].includes(normalized.httpStatus)
          ? this.text('You no longer have access to this file.', 'このファイルへのアクセス権がありません。')
          : this.text('File activity could not be loaded.', 'ファイルのアクティビティを読み込めませんでした。'));
      },
    });
    this.activityRequest = request;
  }

  viewVersion(version: FileActivityVersion): void {
    const fileObjectId = normalizeIdentity(this.file?.canonicalFileId);
    const versionId = normalizeIdentity(version.versionId);
    if (!fileObjectId || !versionId) {
      return;
    }

    this.cancelVersionRequest();
    this.revokeObjectUrl();
    const generation = ++this.versionGeneration;
    this.viewingVersion.set(version);
    this.viewingVersionId.set(versionId);
    this.versionPreviewState.set('loading');
    const renderer = rendererFor(version.fileName, version.contentType);
    this.versionRenderer.set(renderer);
    this.versionText.set('');
    this.versionPreviewMessage.set('');

    if (renderer === 'unsupported') {
      this.versionPreviewState.set('unsupported');
      return;
    }

    const request = this.http.get(
      `/api/files/${encodeURIComponent(fileObjectId)}/versions/${encodeURIComponent(versionId)}/content`,
      { observe: 'response', responseType: 'blob', withCredentials: true },
    ).subscribe({
      next: (response) => {
        if (generation !== this.versionGeneration || this.viewingVersionId() !== versionId || !response.body) {
          return;
        }
        this.versionRequest = null;
        const blob = response.body;
        if (!PREVIEW_POLICY.matches(renderer, blob.type)) {
          this.versionPreviewState.set('error');
          this.versionPreviewMessage.set(this.text(
            'The returned file type did not match this version.',
            '返されたファイル形式がこのバージョンと一致しません。',
          ));
          return;
        }

        if (renderer === 'text') {
          if (blob.size > PREVIEW_POLICY.textPreviewMaxBytes) {
            this.versionPreviewState.set('unsupported');
            return;
          }
          void blob.text().then((text) => {
            if (generation !== this.versionGeneration || this.viewingVersionId() !== versionId) {
              return;
            }
            this.versionText.set(text);
            this.versionPreviewState.set('ready');
          }).catch(() => {
            if (generation === this.versionGeneration && this.viewingVersionId() === versionId) {
              this.versionPreviewState.set('error');
              this.versionPreviewMessage.set(this.text(
                'This version could not be decoded as text.',
                'このバージョンをテキストとして読み込めませんでした。',
              ));
            }
          });
          return;
        }

        this.installObjectUrl(blob);
        this.versionPreviewState.set('ready');
      },
      error: (error: unknown) => {
        if (generation !== this.versionGeneration) {
          return;
        }
        this.versionRequest = null;
        const normalized = normalizeApiError(error);
        this.versionPreviewState.set('error');
        this.versionPreviewMessage.set([401, 403, 404].includes(normalized.httpStatus)
          ? this.text('You no longer have access to this version.', 'このバージョンへのアクセス権がありません。')
          : this.text('This version could not be opened.', 'このバージョンを開けませんでした。'));
      },
    });
    this.versionRequest = request;
  }

  closeVersionPreview(): void {
    this.cancelVersionRequest();
    this.revokeObjectUrl();
    this.viewingVersion.set(null);
    this.viewingVersionId.set(null);
    this.versionPreviewState.set('idle');
    this.versionRenderer.set('unsupported');
    this.versionText.set('');
    this.versionPreviewMessage.set('');
  }

  actionLabel(entry: FileActivityEntry): string {
    switch (entry.kind) {
      case 'uploaded':
        return this.text('File uploaded', 'ファイルをアップロード');
      case 'versionCreated':
        return this.text('Version created', 'バージョンを作成');
      case 'sharingChanged':
        return this.text('Sharing changed', '共有設定を変更');
    }
  }

  sharingLabel(sharing: FileActivitySharing): string {
    const action = (() => {
      switch (sharing.change) {
        case 'policyChanged':
          return this.text('Sharing policy changed', '共有ポリシーを変更');
        case 'recipientGranted':
          return this.text('Recipient access granted', '共有先のアクセスを許可');
        case 'recipientRevoked':
          return this.text('Recipient access revoked', '共有先のアクセスを解除');
        default:
          return this.text('Sharing changed', '共有設定を変更');
      }
    })();
    const state = (() => {
      switch (sharing.accessState) {
        case 'private':
          return this.text('Private', '非公開');
        case 'workspace':
          return 'Workspace';
        default:
          return this.text('Unavailable', '利用不可');
      }
    })();
    return `${action} · ${state}`;
  }

  formatDate(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }
    return new Intl.DateTimeFormat(this.i18n.localeTag(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(date);
  }

  formatSize(bytes: number): string {
    return this.i18n.formatFileSize(bytes);
  }

  text(english: string, japanese: string): string {
    return this.i18n.locale() === 'ja' ? japanese : english;
  }

  private installObjectUrl(blob: Blob): void {
    this.revokeObjectUrl();
    const url = URL.createObjectURL(blob);
    this.objectUrl = url;
    this.versionObjectUrl.set(url);
  }

  private revokeObjectUrl(): void {
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
    }
    this.objectUrl = null;
    this.versionObjectUrl.set(null);
  }

  private cancelActivityRequest(): void {
    this.activityGeneration += 1;
    this.activityRequest?.unsubscribe();
    this.activityRequest = null;
  }

  private cancelVersionRequest(): void {
    this.versionGeneration += 1;
    this.versionRequest?.unsubscribe();
    this.versionRequest = null;
  }
}

const activityKind = (value: unknown): FileActivityKind | undefined =>
    value === 'uploaded' || value === 'versionCreated' || value === 'sharingChanged' ? value : undefined,
  sharingChange = (value: unknown): FileActivitySharing['change'] | undefined =>
    value === 'policyChanged' || value === 'recipientGranted' || value === 'recipientRevoked' || value === 'changed'
      ? value
      : undefined,
  sharingState = (value: unknown): FileActivitySharing['accessState'] | undefined =>
    value === 'private' || value === 'workspace' || value === 'unavailable' ? value : undefined;

function mapActivityResponse(value: unknown, expectedFileObjectId: string): readonly FileActivityEntry[] | null {
  if (!isObject(value) || normalizeIdentity(value['fileObjectId']) !== expectedFileObjectId || !Array.isArray(value['items'])) {
    return null;
  }

  const items: FileActivityEntry[] = [];
  for (const raw of value['items']) {
    if (!isObject(raw)) {
      return null;
    }
    const id = normalizeIdentity(raw['id']);
    const kind = activityKind(raw['kind']);
    const actorDisplayName = stringValue(raw['actorDisplayName']);
    const occurredAt = validDate(raw['occurredAt']);
    if (!id || !kind || !actorDisplayName || !occurredAt) {
      return null;
    }

    let version: FileActivityVersion | undefined;
    if (raw['version'] !== null && raw['version'] !== undefined) {
      version = mapVersion(raw['version']);
      if (!version) {
        return null;
      }
    }

    let sharing: FileActivitySharing | undefined;
    if (raw['sharing'] !== null && raw['sharing'] !== undefined) {
      sharing = mapSharing(raw['sharing']);
      if (!sharing) {
        return null;
      }
    }

    if ((kind === 'uploaded' || kind === 'versionCreated') && !version) {
      return null;
    }
    if (kind === 'sharingChanged' && !sharing) {
      return null;
    }
    items.push({ id, kind, actorDisplayName, occurredAt, version, sharing });
  }

  return items.sort((a, b) => b.occurredAt.localeCompare(a.occurredAt));
}

function mapVersion(value: unknown): FileActivityVersion | undefined {
  if (!isObject(value)) {
    return undefined;
  }
  const versionId = normalizeIdentity(value['versionId']);
  const versionNumber = positiveInteger(value['versionNumber']);
  const fileName = stringValue(value['fileName']);
  const contentType = stringValue(value['contentType']);
  const sizeBytes = nonNegativeNumber(value['sizeBytes']);
  const createdAt = validDate(value['createdAt']);
  const isCurrent = value['isCurrent'];
  if (!versionId || !versionNumber || !fileName || !contentType || sizeBytes === undefined || !createdAt || typeof isCurrent !== 'boolean') {
    return undefined;
  }
  return { versionId, versionNumber, fileName, contentType, sizeBytes, createdAt, isCurrent };
}

function mapSharing(value: unknown): FileActivitySharing | undefined {
  if (!isObject(value)) {
    return undefined;
  }
  const change = sharingChange(value['change']);
  const accessState = sharingState(value['accessState']);
  const sharingVersion = value['sharingVersion'] === null || value['sharingVersion'] === undefined
    ? undefined
    : positiveInteger(value['sharingVersion']);
  if (!change || !accessState || (value['sharingVersion'] !== null && value['sharingVersion'] !== undefined && !sharingVersion)) {
    return undefined;
  }
  return { change, accessState, sharingVersion };
}

function rendererFor(fileName: string, contentType: string): VersionPreviewRenderer {
  const normalized = contentType.toLowerCase().split(';', 1)[0]?.trim() ?? '';
  if (normalized.startsWith('image/')) {
    return 'image';
  }
  if (normalized === 'application/pdf') {
    return 'pdf';
  }
  if (normalized.startsWith('video/')) {
    return 'video';
  }
  if (normalized.startsWith('text/') || [
    'application/json',
    'application/ndjson',
    'application/xml',
    'application/x-ndjson',
    'application/x-yaml',
    'application/yaml',
  ].includes(normalized) || /\.(txt|md|json|csv|xml|log|yaml|yml)$/i.test(fileName)) {
    return 'text';
  }
  return 'unsupported';
}

function normalizeIdentity(value: unknown): string | undefined {
  const normalized = stringValue(value)?.toLowerCase();
  return normalized && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(normalized)
    ? normalized
    : undefined;
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined;
}

function positiveInteger(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 ? value : undefined;
}

function nonNegativeNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : undefined;
}

function validDate(value: unknown): string | undefined {
  const string = stringValue(value);
  return string && !Number.isNaN(Date.parse(string)) ? string : undefined;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}