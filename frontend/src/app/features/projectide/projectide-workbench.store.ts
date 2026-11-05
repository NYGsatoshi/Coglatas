import { computed, Injectable, signal } from '@angular/core';
import {
  BoundReference,
  Diagnostic,
  InspectedSymbol,
  PreviewBundle,
  PreviewCase,
  ProjectLocation,
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
  private readonly referenceSelection = signal<{
    reference: BoundReference;
    symbol: InspectedSymbol;
    result: PreviewCase;
    sourceMap: PreviewCase['sourceMap'];
  } | null>(null);
  private readonly currentReferenceSelection = computed(() => {
    const selected = this.referenceSelection();
    return selected && selected.result === this.context() && selected.result === this.result() &&
      selected.sourceMap === selected.result.sourceMap ? selected : null;
  });
  readonly selectedReference = computed(() => this.currentReferenceSelection()?.reference ?? null);
  readonly referenceSymbol = computed(() => this.currentReferenceSelection()?.symbol ?? null);
  private readonly target = signal<{ record: SourceRecord; location: ProjectLocation } | null>(null);
  private readonly currentTarget = computed(() =>
    this.referenceSelection() && !this.currentReferenceSelection() ? null : this.target(),
  );
  readonly sourceTarget = computed(() => this.currentTarget()?.record ?? null);
  readonly sourceLocation = computed(() => this.currentTarget()?.location ?? null);
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
  private readonly recordSymbols = computed(() => {
    const record = this.inspectedRecord();
    return record?.recordType === 'entity' && this.result() === this.context() && this.result()?.freshness === 'Current' ? (this.result()?.semanticInspection?.symbols ?? []).filter((symbol) =>
      symbol.location.entityId === record.identity && symbol.location.documentId === record.location.documentId) : [];
  });
  readonly diagnosticSymbol = computed(() => {
    const diagnostic = this.diagnostic();
    if (!diagnostic || !this.result()?.sourceMap) return null;
    const location = diagnostic.primaryLocation;
    const matches = this.recordSymbols().filter(({ location: symbol }) =>
      symbol.propertyPath === location.propertyPath &&
      symbol.textSpan?.start === location.textSpan?.start &&
      symbol.textSpan?.length === location.textSpan?.length,
    );
    return matches.length === 1 ? matches[0] : null;
  });
  readonly inspectedSymbols = computed(() => {
    if (this.selection()?.kind !== 'diagnostic') return this.recordSymbols();
    const symbol = this.diagnosticSymbol();
    return symbol ? [symbol] : [];
  });
  readonly inspectedReferences = computed(() => {
    const keys = new Set(this.inspectedSymbols().map((symbol) => symbol.key));
    return (this.result()?.semanticInspection?.references ?? []).filter((reference) => reference.symbolKey !== null && keys.has(reference.symbolKey));
  });
  readonly referenceRows = computed(() => {
    const result = this.result();
    const record = this.inspectedRecord();
    if (!record || result !== this.context() || result?.freshness !== 'Current') return [];
    const incoming = new Set(this.inspectedReferences());
    return (result.semanticInspection?.references ?? []).filter(({ origin, state, symbolKey }, index, references) =>
      state === 'Resolved' && symbolKey !== null && (incoming.has(references[index]) ||
        (this.selection()?.kind !== 'diagnostic' && origin.documentId === record.location.documentId &&
          (record.recordType === 'entity' ? origin.entityId : origin.relationId) === record.identity)),
    );
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
  selectReference(reference: BoundReference): boolean {
    // Keep the inspector anchor when changing the reference detail.
    this.referenceSelection.set(null);
    this.target.set(null);
    const result = this.result();
    const symbol = this.resolveReference(reference);
    if (typeof symbol === 'string' || !result) {
      this.navigationState.set(typeof symbol === 'string' ? symbol : 'unavailable');
      return false;
    }
    this.referenceSelection.set({ reference, symbol, result, sourceMap: result.sourceMap });
    this.navigationState.set('');
    return true;
  }
  navigateReference(destination: 'origin' | 'definition'): boolean {
    const reference = this.selectedReference();
    const symbol = reference ? this.resolveReference(reference) : this.referenceSelection() ? 'stale' : 'unavailable';
    if (typeof symbol === 'string' || !reference) {
      this.target.set(null);
      this.referenceSelection.set(null);
      this.navigationState.set(typeof symbol === 'string' ? symbol : 'unavailable');
      return false;
    }
    return this.navigateLocation(destination === 'origin' ? reference.origin : symbol.location, true);
  }
  private resolveReference(reference: BoundReference): InspectedSymbol | string {
    const result = this.result();
    if (!result || result !== this.context() || result.freshness !== 'Current') return 'stale';
    const inspection = result.semanticInspection;
    if (!inspection || !inspection.references.includes(reference) ||
        reference.state !== 'Resolved' || reference.symbolKey === null) return 'unavailable';
    const matches = inspection.symbols.filter((symbol) => symbol.key === reference.symbolKey);
    if (matches.length !== 1 || matches[0].status !== 'Decoded') return 'unavailable';
    const origin = resolveLocation(result, reference.origin, true);
    if (typeof origin === 'string') return origin;
    const definition = resolveLocation(result, matches[0].location, true);
    return typeof definition === 'string' ? definition : matches[0];
  }
  clearSelection(): void {
    this.resetSelection();
  }
  navigate(): boolean {
    const context = this.context();
    if (this.selection()?.kind === 'diagnostic' && this.result() !== context) {
      this.resetSelection();
      this.navigationState.set('stale');
      return false;
    }
    return this.navigateLocation(this.diagnostic()?.primaryLocation ?? this.inspectedRecord()?.location);
  }
  navigateDiagnosticSymbol(): boolean {
    return this.navigateLocation(this.diagnosticSymbol()?.location);
  }
  private navigateLocation(location: ProjectLocation | undefined, requireSourceMap = false): boolean {
    this.target.set(null);
    const context = this.context();
    if (!context || !location) {
      this.navigationState.set('unavailable');
      return false;
    }
    const target = resolveLocation(context, location, requireSourceMap);
    if (typeof target === 'string') {
      this.navigationState.set(target);
      return false;
    }
    this.target.set({ record: target, location });
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
    this.referenceSelection.set(null);
    this.target.set(null);
    this.navigationState.set('');
  }
}
