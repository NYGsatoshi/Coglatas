import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';

export interface FrontendRuntimeFlags {
  readonly 'frontend.designSystemV04'?: boolean;
  readonly 'frontend.syncfusionGrid'?: boolean;
  readonly 'frontend.syncfusionUploader'?: boolean;
  readonly 'tasks.myTasksV1'?: boolean;
  readonly 'tasks.kanbanV1'?: boolean;
  readonly 'tasks.ganttV1'?: boolean;
  readonly 'realtime.signalR'?: boolean;
  readonly 'realtime.optimisticMessaging'?: boolean;
}

declare global {
  interface Window {
    __COGLATAS_FEATURE_FLAGS__?: FrontendRuntimeFlags;
  }
}

@Injectable({ providedIn: 'root' })
export class FrontendFeatureFlagsService {
  private readonly document = inject(DOCUMENT);
  private readonly values = signal<FrontendRuntimeFlags>(this.readRuntimeFlags());
  readonly designSystemV04Enabled = computed(() => this.values()['frontend.designSystemV04'] ?? true);
  // The canonical rollout amendment intentionally has no separate
  // `frontend.syncfusionAdapters` key. These flags switch only an adapter
  // implementation; they never grant a product capability.
  readonly syncfusionGridEnabled = computed(() => this.values()['frontend.syncfusionGrid'] ?? false);
  readonly syncfusionUploaderEnabled = computed(() => this.values()['frontend.syncfusionUploader'] ?? false);
  readonly myTasksV1Enabled = computed(() => this.values()['tasks.myTasksV1'] ?? true);
  readonly kanbanV1Enabled = computed(() => this.values()['tasks.kanbanV1'] ?? true);
  readonly ganttV1Enabled = computed(() => this.values()['tasks.ganttV1'] ?? true);
  readonly realtimeSignalREnabled = computed(() => this.values()['realtime.signalR'] ?? false);
  readonly optimisticMessagingEnabled = computed(() => this.values()['realtime.optimisticMessaging'] ?? true);

  constructor() {
    this.applyDesignSystemMarker();
  }

  setForTesting(values: FrontendRuntimeFlags): void {
    this.values.set(values);
    this.applyDesignSystemMarker();
  }

  private readRuntimeFlags(): FrontendRuntimeFlags {
    return typeof window === 'undefined' ? {} : window.__COGLATAS_FEATURE_FLAGS__ ?? {};
  }

  private applyDesignSystemMarker(): void {
    this.document.documentElement.dataset['coglatasDesignSystem'] = this.designSystemV04Enabled() ? 'v04' : 'legacy';
  }
}
