import { TestBed } from '@angular/core/testing';

import { ContinueWorkingScope } from '../../shared/continue-working/continue-working-history.service';
import { COGLATAS_FILE_STARS_STORAGE, LOCAL_FILE_STARS_LIMIT, LocalFileStarsRepository } from './file-stars.repository';

const SCOPE: ContinueWorkingScope = { tenantId: 'tenant-a', userId: 'user-a', workspaceId: '11111111-1111-4111-8111-111111111111' };
const FILE_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

describe('LocalFileStarsRepository', () => {
  let repository: LocalFileStarsRepository;
  let records: Map<string, string>;
  beforeEach(() => {
    records = new Map();
    TestBed.configureTestingModule({ providers: [{ provide: COGLATAS_FILE_STARS_STORAGE, useValue: {
      getItem: (key: string) => records.get(key) ?? null,
      setItem: (key: string, value: string) => records.set(key, value),
      removeItem: (key: string) => records.delete(key),
    } }] });
    repository = TestBed.inject(LocalFileStarsRepository);
  });
  afterEach(() => TestBed.resetTestingModule());

  it('persists only versioned opaque IDs, with independent Tenant, user and Workspace buckets', () => {
    expect(repository.write(SCOPE, [FILE_ID])).toBe(true);
    expect([...records.values()]).toEqual([JSON.stringify({ version: 1, ids: [FILE_ID] })]);
    expect(repository.read(SCOPE).ids).toEqual([FILE_ID]);
    for (const other of [{ ...SCOPE, tenantId: 'tenant-b' }, { ...SCOPE, userId: 'user-b' },
      { ...SCOPE, workspaceId: '22222222-2222-4222-8222-222222222222' }]) {
      expect(repository.read(other).ids).toEqual([]);
    }
  });

  it.each([
    '{', JSON.stringify({ version: 2, ids: [FILE_ID] }), JSON.stringify({ version: 1, ids: [FILE_ID], name: 'private' }),
    JSON.stringify({ version: 1, ids: ['../private'] }), JSON.stringify({ version: 1, ids: [FILE_ID, FILE_ID] }),
    JSON.stringify({ version: 1, ids: [FILE_ID.toUpperCase()] }), 'x'.repeat(4_097),
  ])('discards malformed, extra-field, oversized or noncanonical browser records', (invalid) => {
    repository.write(SCOPE, []);
    records.set([...records.keys()][0]!, invalid);
    expect(repository.read(SCOPE)).toEqual({ status: 'discarded', ids: [] });
    expect(records.size).toBe(0);
  });

  it('rejects duplicate IDs and writes beyond the bounded hydration limit', () => {
    expect(repository.write(SCOPE, [FILE_ID, FILE_ID])).toBe(false);
    expect(repository.write(SCOPE, Array.from({ length: LOCAL_FILE_STARS_LIMIT + 1 }, () => FILE_ID))).toBe(false);
    expect(records.size).toBe(0);
  });

  it('reports unavailable storage rather than pretending persistence succeeded', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: COGLATAS_FILE_STARS_STORAGE, useValue: null }] });
    repository = TestBed.inject(LocalFileStarsRepository);
    expect(repository.read(SCOPE)).toEqual({ status: 'unavailable', ids: [] });
    expect(repository.write(SCOPE, [FILE_ID])).toBe(false);
  });
});
