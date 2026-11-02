import { computed, Injectable, signal } from '@angular/core';
import {
  PreviewBundle,
  PreviewCase,
  ReadState,
  Selection,
  SourceRecord,
} from './projectide.contracts';
import { PreviewReadError, resolveLocation } from './projectide-preview.adapter';

/** Semantic selection/input generation; DOM panels, focus and filters stay in the component. */
@Injectable()
export class ProjectIdeWorkbenchStore {
  readonly state = signal<ReadState>('idle');
  readonly bundle = signal<PreviewBundle | null>(null);
  readonly context = signal<PreviewCase | null>(null);
  readonly result = signal<PreviewCase | null>(null);
  readonly selection = signal<Selection | null>(null);
  readonly sourceTarget = signal<SourceRecord | null>(null);
  readonly navigationState = signal('');
  private generation = 0;
  private controller: AbortController | null = null;
  readonly diagnostic = computed(() => {
    const selected = this.selection();
    return selected?.kind === 'diagnostic'
      ? (this.result()?.result.diagnostics.find((d) => d.diagnosticId === selected.id) ?? null)
      : null;
  });
  readonly inspectedRecord = computed(() => {
    const context = this.context();
    const selected = this.selection();
    const diagnostic = this.diagnostic();
    if (!context || !selected) {
      return null;
    }
    const kind =
      selected.kind === 'diagnostic'
        ? diagnostic?.primaryLocation.entityId
          ? 'entity'
          : 'relation'
        : selected.kind;
    const id =
      selected.kind !== 'diagnostic'
        ? selected.id
        : (diagnostic?.primaryLocation.entityId ?? diagnostic?.primaryLocation.relationId);
    return (
      [...context.declarations, ...context.relations].find(
        (r) => r.recordType === kind && r.identity === id,
      ) ?? null
    );
  });
  async load(read: (signal: AbortSignal) => Promise<PreviewBundle>): Promise<void> {
    this.clear('loading');
    const generation = this.generation;
    const controller = new AbortController();
    this.controller = controller;
    try {
      const bundle = await read(controller.signal);
      if (controller.signal.aborted || generation !== this.generation) {
        return;
      }
      this.bundle.set(bundle);
      this.state.set('ready');
    } catch (error) {
      if (controller.signal.aborted || generation !== this.generation) {
        return;
      }
      this.state.set(error instanceof PreviewReadError ? error.state : 'error');
    }
  }
  choose(key: string): void {
    this.resetSelection();
    this.result.set(null);
    this.context.set(this.bundle()?.cases.find((c) => c.key === key) ?? null);
  }
  loadResult(): void {
    this.resetSelection();
    this.result.set(this.context());
  }
  select(selection: Selection): void {
    this.selection.set(selection);
    this.sourceTarget.set(null);
    this.navigationState.set('');
  }
  clearSelection(): void {
    this.resetSelection();
  }
  navigate(): boolean {
    const context = this.context();
    const location = this.diagnostic()?.primaryLocation ?? this.inspectedRecord()?.location;
    if (!context || !location) {
      this.navigationState.set('unavailable');
      return false;
    }
    const target = resolveLocation(context, location);
    if (typeof target === 'string') {
      this.navigationState.set(target);
      this.sourceTarget.set(null);
      return false;
    }
    this.sourceTarget.set(target);
    this.navigationState.set('ready');
    return true;
  }
  clear(state: ReadState = 'idle'): void {
    this.generation++;
    this.controller?.abort();
    this.controller = null;
    this.bundle.set(null);
    this.context.set(null);
    this.result.set(null);
    this.resetSelection();
    this.state.set(state);
  }
  private resetSelection(): void {
    this.selection.set(null);
    this.sourceTarget.set(null);
    this.navigationState.set('');
  }
}
