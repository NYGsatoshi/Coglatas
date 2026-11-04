import { computed, Injectable, signal } from '@angular/core';
import {
  Diagnostic,
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
    if (selected?.kind !== 'diagnostic' || this.result() !== this.context()) return null;
    const matches = this.result()?.result.diagnostics.filter((item) => item.diagnosticId === selected.id);
    return matches?.length === 1 ? matches[0] : null;
  });
  readonly inspectedRecord = computed(() => {
    const context = this.context();
    const selected = this.selection();
    if (!context || !selected) return null;
    if (selected.kind === 'diagnostic') {
      const diagnostic = this.diagnostic();
      if (!diagnostic) return null;
      const target = resolveLocation(context, diagnostic.primaryLocation);
      return typeof target === 'string' ? null : target;
    }
    return [...context.declarations, ...context.relations].find(
      (record) => record.recordType === selected.kind && record.identity === selected.id,
    ) ?? null;
  });
  readonly inspectedSymbols = computed(() => {
    const record = this.inspectedRecord();
    return record?.recordType === 'entity' && this.result() === this.context() && this.result()?.freshness === 'Current' ? (this.result()?.semanticInspection?.symbols ?? []).filter((symbol) =>
      symbol.location.entityId === record.identity && symbol.location.documentId === record.location.documentId) : [];
  });
  readonly inspectedReferences = computed(() => {
    const keys = new Set(this.inspectedSymbols().map((symbol) => symbol.key));
    return (this.result()?.semanticInspection?.references ?? []).filter((reference) => reference.symbolKey !== null && keys.has(reference.symbolKey));
  });
  async load(read: (signal: AbortSignal) => Promise<PreviewBundle>): Promise<void> {
    const generation = this.generation + 1;
    this.clear('loading');
    // Abort subscribers may synchronously clear this context or begin a newer read.
    if (generation !== this.generation) {
      return;
    }
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
  selectDiagnostic(diagnostic: Diagnostic): boolean {
    if (this.result()?.result.diagnostics.includes(diagnostic)) {
      return this.select({ kind: 'diagnostic', id: diagnostic.diagnosticId });
    }
    this.resetSelection();
    this.navigationState.set('unavailable');
    return false;
  }
  select(selection: Selection): boolean {
    this.resetSelection();
    const context = this.context();
    const diagnostics = this.result() === context ? context?.result.diagnostics ?? [] : [];
    const matches = selection.kind === 'diagnostic'
      ? diagnostics.filter((item) => item.diagnosticId === selection.id)
      : [...(context?.declarations ?? []), ...(context?.relations ?? [])].filter(
        (item) => item.recordType === selection.kind && item.identity === selection.id,
      );
    if (matches.length !== 1) {
      this.navigationState.set('unavailable');
      return false;
    }
    this.selection.set(selection);
    return true;
  }
  clearSelection(): void {
    this.resetSelection();
  }
  navigate(): boolean {
    this.sourceTarget.set(null);
    const context = this.context();
    if (this.selection()?.kind === 'diagnostic' && this.result() !== context) {
      this.resetSelection();
      this.navigationState.set('stale');
      return false;
    }
    const location = this.diagnostic()?.primaryLocation ?? this.inspectedRecord()?.location;
    if (!context || !location) {
      this.navigationState.set('unavailable');
      return false;
    }
    const target = resolveLocation(context, location);
    if (typeof target === 'string') {
      this.navigationState.set(target);
      return false;
    }
    this.sourceTarget.set(target);
    this.navigationState.set('ready');
    return true;
  }
  clear(state: ReadState = 'idle'): void {
    this.generation++;
    const controller = this.controller;
    this.controller = null;
    this.bundle.set(null);
    this.context.set(null);
    this.result.set(null);
    this.resetSelection();
    this.state.set(state);
    // Publish the cleared lifecycle before invoking synchronous abort subscribers.
    controller?.abort();
  }
  private resetSelection(): void {
    this.selection.set(null);
    this.sourceTarget.set(null);
    this.navigationState.set('');
  }
}
