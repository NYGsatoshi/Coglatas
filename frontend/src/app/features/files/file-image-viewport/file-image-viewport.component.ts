import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, HostListener, Input, OnChanges, OnDestroy, SimpleChanges, ViewChild, computed, signal } from '@angular/core';
import { LucideMaximize, LucideRotateCw, LucideZoomIn, LucideZoomOut } from '@lucide/angular';

import { injectFilesI18n } from '../../../core/i18n/providers/files-i18n';

@Component({
  selector: 'app-file-image-viewport',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  imports: [LucideMaximize, LucideRotateCw, LucideZoomIn, LucideZoomOut],
  templateUrl: './file-image-viewport.component.html',
  styleUrl: './file-image-viewport.component.scss',
})
export class FileImageViewportComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input({ required: true }) source = '';
  @Input({ required: true }) alt = '';
  @ViewChild('viewport', { static: true }) private viewport?: ElementRef<HTMLElement>;

  readonly i18n = injectFilesI18n();
  private readonly naturalSize = signal({ width: 0, height: 0 });
  private readonly viewportSize = signal({ width: 0, height: 0 });
  private readonly customScale = signal(1);
  private observer?: ResizeObserver;
  readonly failed = signal(false);
  readonly mode = signal<'fit' | 'actual' | 'zoom'>('fit');
  readonly rotation = signal(0);
  readonly ready = computed(() => !this.failed() && this.naturalSize().width > 0 && this.naturalSize().height > 0 && this.viewportSize().width > 0 && this.viewportSize().height > 0);
  private readonly rotatedSize = computed(() => this.rotation() % 180 === 0
    ? this.naturalSize()
    : { width: this.naturalSize().height, height: this.naturalSize().width });
  readonly fitScale = computed(() => this.ready()
    ? Math.min(1, this.viewportSize().width / this.rotatedSize().width, this.viewportSize().height / this.rotatedSize().height)
    : 1);
  readonly scale = computed(() => {
    if (this.mode() === 'fit') { return this.fitScale(); }
    if (this.mode() === 'actual') { return 1; }
    return Math.max(this.fitScale() / 4, Math.min(4, this.customScale()));
  });
  readonly imageWidth = computed(() => this.naturalSize().width * this.scale());
  readonly imageHeight = computed(() => this.naturalSize().height * this.scale());
  // CSS transforms do not change layout bounds. Reserve the rotated bounds so
  // all four image edges remain reachable through the native scroll region.
  readonly stageWidth = computed(() => Math.max(this.viewportSize().width, this.rotatedSize().width * this.scale()));
  readonly stageHeight = computed(() => Math.max(this.viewportSize().height, this.rotatedSize().height * this.scale()));
  readonly transform = computed(() => `translate(-50%, -50%) rotate(${this.rotation()}deg)`);
  readonly status = computed(() => this.i18n.translate('files.image.status', {
    percent: this.i18n.formatNumber(this.scale() * 100, { maximumFractionDigits: 2 }),
    degrees: this.i18n.formatNumber(this.rotation()),
  }));

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['source']) { this.reset(); }
  }

  ngAfterViewInit(): void {
    this.measureViewport();
    if (typeof ResizeObserver !== 'undefined' && this.viewport) {
      this.observer = new ResizeObserver(() => this.measureViewport());
      this.observer.observe(this.viewport.nativeElement);
    }
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    this.source = '';
    this.alt = '';
    this.reset();
  }

  @HostListener('window:resize')
  measureViewport(): void {
    const element = this.viewport?.nativeElement;
    if (!element) { return; }
    const size = { width: element.clientWidth, height: element.clientHeight };
    if (size.width !== this.viewportSize().width || size.height !== this.viewportSize().height) {
      this.viewportSize.set(size);
    }
  }

  imageLoaded(event: Event): void {
    const image = event.target as HTMLImageElement;
    if ((image.currentSrc || image.src) !== this.source) { return; }
    if (!image.naturalWidth || !image.naturalHeight) { this.imageFailed(event); return; }
    this.naturalSize.set({ width: image.naturalWidth, height: image.naturalHeight });
    this.measureViewport();
  }

  imageFailed(event: Event): void {
    const image = event.target as HTMLImageElement;
    if ((image.currentSrc || image.src) !== this.source) { return; }
    this.reset();
    this.failed.set(true);
  }

  fit(): void { this.mode.set('fit'); this.resetScroll(); }
  actualSize(): void { this.mode.set('actual'); this.resetScroll(); }
  rotate(): void { this.rotation.update((angle) => (angle + 90) % 360); this.resetScroll(); }
  zoom(direction: 1 | -1): void {
    this.customScale.set(Math.max(this.fitScale() / 4, Math.min(4, this.scale() * (direction === 1 ? 1.25 : 0.8))));
    this.mode.set('zoom');
    this.resetScroll();
  }

  private reset(): void {
    this.naturalSize.set({ width: 0, height: 0 });
    this.rotation.set(0);
    this.mode.set('fit');
    this.customScale.set(1);
    this.failed.set(false);
    this.resetScroll();
  }

  private resetScroll(): void {
    const element = this.viewport?.nativeElement;
    if (element) { element.scrollLeft = 0; element.scrollTop = 0; }
  }
}
