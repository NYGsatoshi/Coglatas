import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { AqApi, aqSegment } from './aq-api';
import { aqCreatePoster, aqPngBlob, aqReadLogo } from './aq-qr-poster';

export interface AqQrShareData {
  title: string;
  surveyUrl: string;
  pngBase64: string;
}

export function validAqQrShare(value: AqQrShareData): boolean {
  if (typeof value?.title !== 'string' || typeof value.surveyUrl !== 'string' ||
      typeof value.pngBase64 !== 'string' || value.pngBase64.length > 512 * 1024 ||
      !value.pngBase64.startsWith('iVBORw0KGgo') || !/^[A-Za-z0-9+/]+={0,2}$/u.test(value.pngBase64)) return false;
  try {
    const url = new URL(value.surveyUrl);
    const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);
    return (url.protocol === 'https:' || (url.protocol === 'http:' && loopback)) &&
      !url.username && !url.password && !url.search && !url.hash &&
      /^\/app\/aq\/surveys\/[0-9a-f]{64}$/u.test(url.pathname);
  } catch {
    return false;
  }
}

@Component({
  selector: 'app-aq-qr-share',
  templateUrl: './aq-qr-share.html',
  styleUrl: './aq-qr-share.css',
})
export class AqQrShare {
  readonly campaignId = input.required<string>();
  readonly targetId = input.required<string>();
  private readonly api = inject(AqApi);
  private readonly refreshVersion = signal(0);
  protected readonly share = signal<AqQrShareData | null>(null);
  protected readonly qrSrc = computed(() => this.share() ? 'data:image/png;base64,' + this.share()!.pngBase64 : '');
  protected readonly loading = signal(false);
  protected readonly unavailable = signal(false);
  protected readonly notice = signal('');
  protected readonly error = signal('');
  protected readonly logo = signal('');
  protected readonly logoLoading = signal(false);
  protected readonly printing = signal(false);
  private generation = 0;
  private logoGeneration = 0;

  constructor() {
    effect(onCleanup => {
      const campaign = this.campaignId();
      const target = this.targetId();
      this.refreshVersion();
      const generation = ++this.generation;
      this.share.set(null);
      this.logo.set('');
      ++this.logoGeneration;
      this.logoLoading.set(false);
      this.printing.set(false);
      this.notice.set('');
      this.error.set('');
      this.unavailable.set(false);
      this.loading.set(true);
      const subscription = this.api.read<AqQrShareData>('survey-campaigns/' + aqSegment(campaign) + '/qr-share',
        { targetId: target }).subscribe({
        next: value => {
          if (generation !== this.generation) return;
          this.loading.set(false);
          if (validAqQrShare(value)) this.share.set(value);
          else this.unavailable.set(true);
        },
        error: () => {
          if (generation !== this.generation) return;
          this.loading.set(false);
          this.unavailable.set(true);
        },
      });
      onCleanup(() => {
        ++this.generation;
        ++this.logoGeneration;
        subscription.unsubscribe();
      });
    });
  }

  protected refresh(): void {
    this.refreshVersion.update(version => version + 1);
  }

  protected async copyUrl(): Promise<void> {
    const share = this.share();
    if (!share) return;
    const generation = this.generation;
    this.clearMessages();
    try {
      await navigator.clipboard.writeText(share.surveyUrl);
      if (generation === this.generation) this.notice.set('回答URLをコピーしました。');
    } catch {
      if (generation === this.generation) this.error.set('コピーできませんでした。URL欄から手動でコピーしてください。');
    }
  }

  protected async copyQr(): Promise<void> {
    const share = this.share();
    if (!share) return;
    const generation = this.generation;
    this.clearMessages();
    try {
      await navigator.clipboard.write([new ClipboardItem({ 'image/png': aqPngBlob(share.pngBase64) })]);
      if (generation === this.generation) this.notice.set('QR画像をコピーしました。');
    } catch {
      if (generation === this.generation) this.error.set('画像をコピーできませんでした。QR画像 PNGの保存機能をご利用ください。');
    }
  }

  protected showProjection(dialog: HTMLDialogElement): void {
    if (!this.share() || dialog.open) return;
    dialog.showModal();
    // The modal covers the viewport even if the browser denies fullscreen.
    void dialog.requestFullscreen?.().then(() => {
      if (!dialog.open && document.fullscreenElement === dialog) void document.exitFullscreen().catch(() => undefined);
    }).catch(() => undefined);
  }

  protected closeProjection(dialog: HTMLDialogElement): void {
    if (document.fullscreenElement === dialog) void document.exitFullscreen().catch(() => undefined);
    if (dialog.open) dialog.close();
  }

  protected async selectLogo(event: Event): Promise<void> {
    const control = event.target as HTMLInputElement;
    const file = control.files?.[0];
    control.value = '';
    if (!file) return;
    const generation = ++this.logoGeneration;
    this.clearMessages();
    this.logo.set('');
    this.logoLoading.set(true);
    try {
      const logo = await aqReadLogo(file);
      if (generation === this.logoGeneration) this.logo.set(logo);
    } catch {
      if (generation === this.logoGeneration) this.error.set('ロゴを読み込めませんでした。2MB以下、4096×4096px以下のPNGまたはJPEGを選択してください。');
    } finally {
      if (generation === this.logoGeneration) this.logoLoading.set(false);
    }
  }

  protected removeLogo(): void {
    ++this.logoGeneration;
    this.logo.set('');
    this.logoLoading.set(false);
    this.clearMessages();
  }

  protected async downloadPoster(): Promise<void> {
    const share = this.share();
    if (!share || this.printing() || this.logoLoading()) return;
    const generation = this.generation;
    const logoGeneration = this.logoGeneration;
    this.clearMessages();
    this.printing.set(true);
    try {
      const pdf = await aqCreatePoster(share.title, this.qrSrc(), this.logo());
      if (generation !== this.generation || logoGeneration !== this.logoGeneration) return;
      const url = URL.createObjectURL(pdf);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'presentation-qr-poster-a4.pdf';
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      this.notice.set('印刷用A4ポスターを保存しました。');
    } catch {
      if (generation === this.generation) this.error.set('ポスターを作成できませんでした。既存の印刷用PDFをご利用ください。');
    } finally {
      if (generation === this.generation) this.printing.set(false);
    }
  }

  private clearMessages(): void {
    this.notice.set('');
    this.error.set('');
  }
}
