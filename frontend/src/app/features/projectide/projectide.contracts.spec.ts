/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  hasCompleteCurrentPass,
  PreviewBundle,
  ProjectLocation,
  Identity,
} from './projectide.contracts';
import { PreviewReadError, readPreviewBundle, resolveLocation } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

export const bundle = () =>
  readPreviewBundle(
    JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8')),
  );
const fixture = (key = 'fail') => bundle().cases.find((c) => c.key === key)!;
const changed = <T>(value: T, update: (draft: T) => void): T => {
  const draft = structuredClone(value);
  update(draft);
  return draft;
};
const without = <T>(value: T, path: string): T => {
  const copy = structuredClone(value);
  const parts = path.split('.');
  let target = copy as unknown as Record<string, unknown>;
  for (const key of parts.slice(0, -1)) {
    target = target[key] as Record<string, unknown>;
  }
  delete target[parts.at(-1)!];
  return copy;
};

describe('ProjectIDE Core artifact view contract', () => {
  it.each([
    'context.branch',
    'result.boundInputStamp',
    'result.boundInputStamp.sourceDigest',
    'source.documents',
    'sourceText',
    'sourceMap.nodes',
    'sourceMap.relations',
    'declarations.0.data',
    'result.diagnostics.0.primaryLocation.context',
    'result.rules',
    'result.diagnostics.0.evidence.premises',
    'result.rules.0.evidence',
  ])('rejects missing nested %s with an explicit unsupported error', (path) => {
    const value = without(bundle(), 'cases.0.' + path);
    expect(() => readPreviewBundle(value)).toThrow(PreviewReadError);
    expect(() => readPreviewBundle(value)).toThrow('unsupported');
  });
  it('rejects null cases without a native TypeError', () => {
    expect(() => readPreviewBundle({ ...bundle(), cases: [null] })).toThrow(PreviewReadError);
  });
  it.each(['source.documents', 'declarations', 'relations', 'result.diagnostics', 'result.rules', 'result.artifacts'])(
    'rejects ambiguous duplicate identities in %s', (path) => {
      const b = structuredClone(bundle());
      const parts = path.split('.');
      let target = b.cases[0] as unknown as Record<string, unknown>;
      for (const key of parts.slice(0, -1)) {
        target = target[key] as Record<string, unknown>;
      }
      const items = target[parts.at(-1)!] as unknown[];
      items.push(structuredClone(items[0]));
      expect(() => readPreviewBundle(b)).toThrow('unsupported');
    },
  );
  it('rejects a diagnostic pointing outside the Source document set', () => {
    const b = structuredClone(bundle());
    (b.cases[0].result.diagnostics[0].primaryLocation as { documentId: string }).documentId = 'absent';
    expect(() => readPreviewBundle(b)).toThrow('wrong-scope');
  });
  it.each(['missing-with-premises', 'proof-without-premises', 'proof-with-missing-reason'])(
    'rejects an impossible Core evidence shape: %s', (mode) => {
      const b = structuredClone(bundle());
      const e = b.cases[0].result.rules[0].evidence as unknown as { kind: string; premises: unknown[]; missingReason: string | null };
      if (mode === 'missing-with-premises') {
        e.kind = 'coglatas.missing';
        e.missingReason = 'coglatas.fixture.missing';
      } else if (mode === 'proof-without-premises') {
        e.premises = [];
      } else {
        e.missingReason = 'coglatas.fixture.missing';
      }
      expect(() => readPreviewBundle(b)).toThrow('unsupported');
    },
  );
  it('reads only explicitly synthetic exporter artifacts', () => {
    expect(bundle().mode).toBe('synthetic-fixture');
    expect(bundle().cases).toHaveLength(10);
  });
  it.each([
    ['fail', 'coglatas.completed', 'coglatas.complete', 'coglatas.fail'],
    ['pass', 'coglatas.completed', 'coglatas.complete', 'coglatas.pass'],
    ['unknown', 'coglatas.completed', 'coglatas.partial', 'coglatas.inconclusive'],
    ['unsupported', 'coglatas.completed', 'coglatas.partial', 'coglatas.inconclusive'],
    ['not-run', 'coglatas.not-run', 'coglatas.none', 'coglatas.not-run'],
    ['cancelled', 'coglatas.cancelled', 'coglatas.none', 'coglatas.not-run'],
    ['timeout', 'coglatas.timed-out', 'coglatas.none', 'coglatas.not-run'],
    ['stale-pass', 'coglatas.completed', 'coglatas.complete', 'coglatas.pass'],
  ])('retains independent Core axes for %s', (key, execution, coverage, verdict) => {
    expect(fixture(key).result).toMatchObject({ execution, coverage, verdict });
  });
  it.each(['fail', 'unknown', 'unsupported', 'not-run', 'cancelled', 'timeout', 'stale-pass'])(
    'never projects %s as a complete current pass',
    (key) => {
      expect(hasCompleteCurrentPass(fixture(key))).toBe(false);
    },
  );
  it('projects only the current complete proven pass', () => {
    expect(hasCompleteCurrentPass(fixture('pass'))).toBe(true);
  });
  it('does not turn missing evidence into success', () => {
    expect(
      hasCompleteCurrentPass(
        changed(
          fixture('pass'),
          (c) => ((c.result as { evidenceComplete: boolean }).evidenceComplete = false),
        ),
      ),
    ).toBe(false);
  });
  it.each(['format', 'mode'])('rejects unsupported %s', (field) => {
    expect(() => readPreviewBundle({ ...bundle(), [field]: 'future' })).toThrow('unsupported');
  });
  it('rejects future execution tokens', () => {
    const b = structuredClone(bundle());
    (b.cases[0].result as unknown as { execution: string }).execution = 'future';
    expect(() => readPreviewBundle(b)).toThrow('unsupported');
  });
  it('rejects a substituted result input', () => {
    const b = structuredClone(bundle());
    (b.cases[0].result as { compilerInputId: string }).compilerInputId = 'other';
    expect(() => readPreviewBundle(b)).toThrow('wrong-scope');
  });
  it('rejects a substituted source digest', () => {
    const b = structuredClone(bundle());
    (b.cases[0].sourceDigest as { value: string }).value = 'b'.repeat(64);
    expect(() => readPreviewBundle(b)).toThrow('wrong-scope');
  });
  it('rejects a source map from another input', () => {
    const b = structuredClone(bundle());
    (b.cases[0].sourceMap as { compilerInputId: string }).compilerInputId = 'other';
    expect(() => readPreviewBundle(b)).toThrow('wrong-scope');
  });
  it('rejects a source map without its producing result artifact', () => {
    const b = structuredClone(bundle());
    (b.cases[0].result as unknown as { artifacts: unknown[] }).artifacts = [];
    expect(() => readPreviewBundle(b)).toThrow('wrong-scope');
  });
  it.each(['not-run', 'cancelled', 'timeout'])(
    'does not associate a produced SourceMap with %s',
    (key) => {
      expect(fixture(key).sourceMap).toBeNull();
    },
  );
  it('preserves exact diagnostic order and semantic identities', () => {
    const value = fixture('unsupported');
    expect(
      readPreviewBundle(bundle())
        .cases.find((c) => c.key === 'unsupported')!
        .result.diagnostics.map((d) => d.diagnosticId),
    ).toEqual(value.result.diagnostics.map((d) => d.diagnosticId));
  });
});

describe('ProjectIDE semantic source navigation', () => {
  const location = (): ProjectLocation => fixture().result.diagnostics[0].primaryLocation;
  it.each([
    'result.boundInputStamp.sourceContext.branch.tenantId',
    'result.compilerInputId',
    'sourceMap.compilerInputId',
    'sourceMap.nodes.0.location.context.branch.tenantId',
    'declarations.0.location.context.branch.tenantId',
  ])('rechecks direct navigation binding for %s', (path) => {
    const c = structuredClone(fixture());
    const parts = path.split('.');
    let target = c as unknown as Record<string, unknown>;
    for (const key of parts.slice(0, -1)) {
      target = target[key] as Record<string, unknown>;
    }
    target[parts.at(-1)!] = 'substituted';
    expect(resolveLocation(c, location())).toBe('wrong-scope');
  });
  it('rejects direct navigation when the producing artifact is missing', () => {
    const c = changed(fixture(), (draft) => {
      (draft.result as unknown as { artifacts: unknown[] }).artifacts = [];
    });
    expect(resolveLocation(c, location())).toBe('wrong-scope');
  });
  it.each([
    'source.documents',
    'sourceText',
    'sourceMap.nodes',
    'sourceMap.relations',
    'context.branch',
    'declarations.0.location',
  ])('safely rejects malformed navigation input %s', (path) => {
    expect(
      resolveLocation(without(fixture(), path), {
        ...location(),
        textSpan: { start: 0, length: 0 },
      }),
    ).toBe('unavailable');
  });
  it('safely rejects a malformed location context', () => {
    expect(resolveLocation(fixture(), without(location(), 'context.branch'))).toBe('unavailable');
  });
  it('resolves the same revision by stable entity and document ID', () => {
    const target = resolveLocation(fixture(), location());
    expect(typeof target).toBe('object');
    expect((target as { kind: string }).kind).toBe('coglatas.constraint');
  });
  it.each(['tenantId', 'projectId', 'branchId'])('rejects another %s', (field) => {
    const loc = structuredClone(location());
    (loc.context.branch as unknown as Record<string, string>)[field] = 'other';
    expect(resolveLocation(fixture(), loc)).toBe('wrong-scope');
  });
  it('rejects another revision', () => {
    const loc = { ...location(), context: fixture('stale-pass').context };
    expect(resolveLocation(fixture(), loc)).toBe('wrong-scope');
  });
  it('blocks stale source-map navigation', () => {
    const c = fixture('stale-pass');
    expect(resolveLocation(c, c.declarations[0].location)).toBe('stale');
  });
  it('fails safely for missing document/entity/path', () => {
    for (const loc of [
      { ...location(), documentId: 'absent' },
      { ...location(), entityId: 'absent' },
      { ...location(), propertyPath: '/missing' },
    ]) {
      expect(resolveLocation(fixture(), loc as ProjectLocation)).toBe('unavailable');
    }
  });
  it('does not use a logical filename as identity', () => {
    const c = changed(
      fixture(),
      (d) => ((d.source.documents[0] as { logicalName: string }).logicalName = 'renamed'),
    );
    expect(typeof resolveLocation(c, location())).toBe('object');
  });
  it('rejects navigation absent from a produced SourceMap', () => {
    const c = changed(
      fixture(),
      (d) => ((d.sourceMap as unknown as { nodes: unknown[] }).nodes = []),
    );
    expect(resolveLocation(c, location())).toBe('unavailable');
  });
  it('resolves Core property paths and relations', () => {
    const c = fixture();
    expect(typeof resolveLocation(c, { ...location(), propertyPath: '/payload/predicate' })).toBe(
      'object',
    );
    expect(typeof resolveLocation(c, c.relations[0].location)).toBe('object');
  });
  it('validates exact UTF-16 boundaries rather than Unicode scalar counts', () => {
    const c = fixture();
    const index = c.sourceText.indexOf('🌌');
    expect(index).toBeGreaterThan(0);
    expect(
      typeof resolveLocation(c, { ...location(), textSpan: { start: index, length: 2 } }),
    ).toBe('object');
    expect(resolveLocation(c, { ...location(), textSpan: { start: index + 1, length: 1 } })).toBe(
      'unavailable',
    );
    expect(
      resolveLocation(c, { ...location(), textSpan: { start: c.sourceText.length, length: 1 } }),
    ).toBe('unavailable');
  });
});

describe('ProjectIDE context and cancellation generation', () => {
  const store = () => new ProjectIdeWorkbenchStore();
  it.each(['branch-pass', 'other-project'])(
    'clears prior state on %s context changes',
    async (key) => {
      const s = store();
      await s.load(async () => bundle());
      s.choose('fail');
      s.loadResult();
      s.choose(key);
      expect(s.result()).toBeNull();
      expect(s.selection()).toBeNull();
      expect(s.context()?.key).toBe(key);
    },
  );
  it('discards results and selection on revision/branch context changes', async () => {
    const s = store();
    await s.load(async () => bundle());
    s.choose('fail');
    s.loadResult();
    s.select({ kind: 'diagnostic', id: s.result()!.result.diagnostics[0].diagnosticId });
    expect(s.inspectedRecord()?.kind).toBe('coglatas.constraint');
    expect(s.navigate()).toBe(true);
    s.choose('stale-pass');
    expect(s.result()).toBeNull();
    expect(s.selection()).toBeNull();
    expect(s.sourceTarget()).toBeNull();
  });
  it('clears all protected data on permission revocation', async () => {
    const s = store();
    await s.load(async () => bundle());
    s.choose('fail');
    s.loadResult();
    s.clear('denied');
    expect(s.bundle()).toBeNull();
    expect(s.context()).toBeNull();
    expect(s.result()).toBeNull();
    expect(s.state()).toBe('denied');
  });
  it('aborts and discards a late old generation even when the reader ignores cancellation', async () => {
    const s = store();
    let release!: (b: PreviewBundle) => void;
    let signal!: AbortSignal;
    const old = s.load((sg) => {
      signal = sg;
      return new Promise((resolve) => (release = resolve));
    });
    await s.load(async () => bundle());
    s.choose('pass');
    release(bundle());
    await old;
    expect(signal.aborted).toBe(true);
    expect(s.context()?.key).toBe('pass');
  });
  it('does not repopulate after disposal or cancel', async () => {
    const s = store();
    let release!: (b: PreviewBundle) => void;
    const read = s.load(() => new Promise((resolve) => (release = resolve)));
    s.clear('cancelled');
    release(bundle());
    await read;
    expect(s.bundle()).toBeNull();
    expect(s.state()).toBe('cancelled');
  });
  it('reports errors without carrying an earlier result and supports recovery', async () => {
    const s = store();
    await s.load(async () => bundle());
    s.choose('pass');
    s.loadResult();
    await s.load(async () => {
      throw new Error('private');
    });
    expect(s.state()).toBe('error');
    expect(s.result()).toBeNull();
    await s.load(async () => bundle());
    expect(s.state()).toBe('ready');
  });
  it('keeps unknown selections unavailable', async () => {
    const s = store();
    await s.load(async () => bundle());
    s.choose('fail');
    s.select({ kind: 'entity', id: 'absent' as Identity<'Entity'> });
    expect(s.inspectedRecord()).toBeNull();
    expect(s.navigate()).toBe(false);
  });
});
