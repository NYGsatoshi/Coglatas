/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { readPreviewBundle, resolveLocation } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { PreviewBundle, PreviewCase } from './projectide.contracts';

// Keep original Core-emitted cases, but validate only the two independent scopes exercised here.
// Full-bundle and cross-case preparation validation belongs to the adapter contract suite.
const exported = JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8'));
const fixtureText = JSON.stringify({ ...exported, cases: exported.cases.filter((item: { key: string }) =>
  item.key === 'test-assertion-navigation' || item.key === 'branch-pass') });
// Parse and admit a fresh graph for each test; never share mutable admitted objects.
const bundle = () => readPreviewBundle(JSON.parse(fixtureText));
const current = (fixture: PreviewBundle) => fixture.cases.find(item => item.key === 'test-assertion-navigation')!;
const diagnostic = (value: PreviewCase, key: string) => value.result.diagnostics.find(item =>
  item.primaryLocation.propertyPath === '/payload/assertions/' + key.replaceAll('~', '~0').replaceAll('/', '~1') + '/target')!;

describe('Typed Test assertion Source navigation', () => {
  // Unicode is intentional assertion-key fixture data, including pointer escape characters.
  it.each(['example.check', 'example.a/b~c', 'example.テスト/α~β', 'example.foreign'])(
    'opens the exact Core-emitted assertion location for %s without changing the failed verdict', async key => {
      const fixture = bundle(); const store = new ProjectIdeWorkbenchStore();
      await store.load(async () => fixture); store.choose('test-assertion-navigation'); store.loadResult();
      const emitted = store.result()!.result.diagnostics.find(item => item.primaryLocation.propertyPath === diagnostic(current(fixture), key).primaryLocation.propertyPath)!;
      expect(store.selectDiagnostic(emitted)).toBe(true);
      expect(store.navigate()).toBe(true);
      expect(store.sourceTarget()).toBe(store.inspectedRecord());
      expect(store.sourceTarget()!.identity).toBe(emitted.primaryLocation.entityId);
      expect(resolveLocation(store.context()!, emitted.primaryLocation)).toBe(store.sourceTarget());
      expect(store.result()!.result.verdict).toBe('coglatas.fail');
      expect(store.result()!.sourceMap).toBeNull();
      expect(resolveLocation(store.context()!, emitted.primaryLocation, true)).toBe('unavailable');
    });

  it.each(['example.missing', 'example.foreign'])('rejects a key outside the selected declaration: %s', key => {
    const fixture = bundle(); const value = current(fixture); const location = diagnostic(value, 'example.check').primaryLocation;
    expect(resolveLocation(value, { ...location, propertyPath: '/payload/assertions/' + key + '/target' })).toBe('invalid-location');
  });

  it('preserves canonical array indices but rejects absent elements and inherited properties', () => {
    const fixture = bundle(); const value = current(fixture); const location = diagnostic(value, 'example.check').primaryLocation;
    expect(typeof resolveLocation(value, { ...location, propertyPath: '/payload/assertions/0/target' })).toBe('object');
    for (const path of ['/payload/assertions/99', '/payload/assertions/01', '/payload/assertions/example.check/absent', '/payload/assertions/example.check/__proto__']) {
      expect(resolveLocation(value, { ...location, propertyPath: path })).toBe('invalid-location');
    }
  });

  it('rejects malformed escapes and stale or foreign input contexts', () => {
    const fixture = bundle(); const value = current(fixture); const location = diagnostic(value, 'example.check').primaryLocation;
    expect(resolveLocation(value, { ...location, propertyPath: '/payload/assertions/example.~2bad' })).toBe('unavailable');
    expect(resolveLocation(value, { ...location, context: fixture.cases.find(item => item.key === 'branch-pass')!.context })).toBe('wrong-scope');
    expect(resolveLocation({ ...value, freshness: 'Stale' }, location)).toBe('stale');
  });

  it('clears an assertion target on result replacement, scope change and permission loss', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle());
    store.choose('test-assertion-navigation'); store.loadResult();
    const emitted = store.result()!.result.diagnostics.find(item => item.primaryLocation.propertyPath.includes('/assertions/'))!;
    expect(store.selectDiagnostic(emitted)).toBe(true); expect(store.navigate()).toBe(true);
    store.loadResult(); expect(store.sourceTarget()).toBeNull(); expect(store.diagnostic()).toBeNull();
    expect(store.selectDiagnostic(emitted)).toBe(true); expect(store.navigate()).toBe(true);
    store.choose('branch-pass'); expect(store.sourceTarget()).toBeNull(); expect(store.selectDiagnostic(emitted)).toBe(false);
    store.choose('test-assertion-navigation'); store.loadResult();
    expect(store.selectDiagnostic(emitted)).toBe(true); expect(store.navigate()).toBe(true);
    store.clear('denied'); expect(store.sourceTarget()).toBeNull(); expect(store.navigate()).toBe(false);
  });
});
