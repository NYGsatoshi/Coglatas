import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EMPTY, of, Subject, throwError } from 'rxjs';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { InMemoryLocalDraftRepository, LOCAL_DRAFT_REPOSITORY, REMOTE_DRAFT_ADAPTER } from './local-draft.adapters';
import { LocalCheckpointResult, RemoteDraftAttempt } from './local-draft.models';
import { LocalDraftStore } from './local-draft.store';

describe('LocalDraftStore local-only boundary', () => {
  const user = signal({ userId: 'user-a' });
  const tenant = signal({ tenantId: 'tenant-a', isAvailable: true });
  const workspace = signal({ id: 'workspace-a' });
  const authenticated = signal(true);
  let clear: () => void;
  let repository: InMemoryLocalDraftRepository;
  let remote: { attemptSave: ReturnType<typeof vi.fn> };
  let store: LocalDraftStore;

  beforeEach(() => {
    user.set({ userId: 'user-a' }); tenant.set({ tenantId: 'tenant-a', isAvailable: true });
    workspace.set({ id: 'workspace-a' }); authenticated.set(true);
    repository = new InMemoryLocalDraftRepository();
    remote = { attemptSave: vi.fn(() => of<RemoteDraftAttempt>('unavailable')) };
    TestBed.configureTestingModule({ providers: [
      { provide: AuthSessionFacade, useValue: { currentUser: user, currentTenant: tenant, isAuthenticated: authenticated } },
      { provide: ActiveWorkspaceFacade, useValue: { activeWorkspace: workspace } },
      { provide: RealtimeFacade, useValue: { registerProtectedStateClearer: (_owner: string, callback: () => void) => { clear = callback; return () => undefined; } } },
      { provide: LOCAL_DRAFT_REPOSITORY, useValue: repository },
      { provide: REMOTE_DRAFT_ADAPTER, useValue: remote }
    ] });
    store = TestBed.inject(LocalDraftStore);
    TestBed.tick();
  });

  afterEach(() => { TestBed.resetTestingModule(); vi.restoreAllMocks(); });

  it('retains Unicode input in root state and keeps a truthful memory-only checkpoint', () => {
    store.edit('title', '研究 🧪');
    store.edit('body', '<script>text only</script>');
    expect(TestBed.inject(LocalDraftStore).view().content.title).toBe('研究 🧪');
    store.saveCheckpoint();
    expect(store.view().status).toBe('checkpoint');
    expect(store.view().message).toContain('not saved to the server');
    expect(store.changed()).toBe(false);
    expect(remote.attemptSave).not.toHaveBeenCalled();
    store.edit('body', 'new working edits');
    store.restoreCheckpoint();
    expect(store.view().content.body).toBe('<script>text only</script>');
  });

  it('never claims server persistence when the production adapter is unavailable', () => {
    store.edit('body', 'Keep this text');
    store.checkServerSave();
    expect(store.view().status).toBe('unavailable');
    expect(store.view().content.body).toBe('Keep this text');
    expect(store.view().message).toContain('cross-device sync are unavailable');
  });

  it('preserves edits after an adapter network failure and supports a separate retry', () => {
    remote.attemptSave.mockReturnValueOnce(throwError(() => new Error('private network diagnostics')));
    store.edit('body', 'unsent text');
    store.checkServerSave();
    expect(store.view().status).toBe('error');
    expect(store.view().content.body).toBe('unsent text');
    expect(store.view().message).not.toContain('private network diagnostics');
    store.checkServerSave();
    expect(store.view().status).toBe('unavailable');
  });

  it('keeps edits after a remote conflict without claiming a merge or save', () => {
    remote.attemptSave.mockReturnValue(of<RemoteDraftAttempt>('conflict'));
    store.edit('body', 'mine'); store.checkServerSave();
    expect(store.view().status).toBe('conflict');
    expect(store.view().content.body).toBe('mine');
    expect(store.view().message).toContain('no server save was confirmed');
  });

  it('recovers from an empty adapter response instead of leaving the UI busy', () => {
    remote.attemptSave.mockReturnValueOnce(EMPTY);
    store.edit('body', 'keep me'); store.checkServerSave();
    expect(store.view().status).toBe('error');
    expect(store.view().content.body).toBe('keep me');
    store.checkServerSave();
    expect(store.view().status).toBe('unavailable');
  });

  it('requires explicit retry to replace a changed local checkpoint', () => {
    store.edit('body', 'my edits');
    repository.save(store.view().scope!, 0, { title: '', body: 'other checkpoint', links: [] }).subscribe();
    store.saveCheckpoint();
    expect(store.view().status).toBe('conflict');
    expect(store.view().content.body).toBe('my edits');
    store.saveCheckpoint();
    expect(repository.read(store.view().scope!).content.body).toBe('other checkpoint');
    store.saveCheckpoint(true);
    expect(store.view().status).toBe('checkpoint');
    expect(repository.read(store.view().scope!).content.body).toBe('my edits');
  });

  it('restores the competing checkpoint only when explicitly requested', () => {
    store.edit('body', 'my edits');
    repository.save(store.view().scope!, 0, { title: '', body: 'other checkpoint', links: [] }).subscribe();
    store.saveCheckpoint(); store.restoreCheckpoint();
    expect(store.view().content.body).toBe('other checkpoint');
  });

  it('retains newer edits during asynchronous saves and prevents duplicate saves', () => {
    const result = new Subject<LocalCheckpointResult>();
    const save = vi.spyOn(repository, 'save').mockReturnValue(result);
    store.edit('body', 'snapshot'); store.saveCheckpoint();
    store.edit('body', 'newer edits'); store.saveCheckpoint(); store.checkServerSave();
    expect(save).toHaveBeenCalledTimes(1);
    expect(remote.attemptSave).not.toHaveBeenCalled();
    result.next({ status: 'saved', checkpoint: { version: 1, content: { title: '', body: 'snapshot', links: [] } } });
    result.complete();
    expect(store.view().content.body).toBe('newer edits');
    expect(store.changed()).toBe(true);
    expect(store.view().status).toBe('editing');
  });

  it('handles checkpoint adapter failure without losing live text or reflecting diagnostics', () => {
    vi.spyOn(repository, 'save').mockImplementation(() => { throw new Error('private storage error'); });
    store.edit('body', 'still here'); store.saveCheckpoint();
    expect(store.view().status).toBe('error');
    expect(store.view().content.body).toBe('still here');
    expect(store.view().message).not.toContain('private storage error');
  });

  it('handles synchronous checkpoint reads during restore and retry without losing working edits', () => {
    store.edit('body', 'keep me');
    vi.spyOn(repository, 'read').mockImplementation(() => { throw new Error('private read diagnostics'); });
    expect(() => store.restoreCheckpoint()).not.toThrow();
    expect(store.view().content.body).toBe('keep me');
    expect(store.view().status).toBe('error');
    expect(store.view().message).not.toContain('private read diagnostics');
    expect(() => store.saveCheckpoint(true)).not.toThrow();
    expect(store.view().content.body).toBe('keep me');
  });

  it('blocks link mutations while a checkpoint save is in flight and keeps its pending status', () => {
    const result = new Subject<LocalCheckpointResult>();
    vi.spyOn(repository, 'save').mockReturnValue(result);
    store.setLinksPreview(true); store.addLink('demo-task'); store.saveCheckpoint();
    store.addLink('demo-file'); store.removeLink('demo-task'); store.setLinksPreview(false);
    expect(store.view().status).toBe('saving');
    expect(store.view().content.links.map((link) => link.id)).toEqual(['demo-task']);
    expect(store.view().linksPreviewEnabled).toBe(true);
    result.complete();
  });

  it.each(['user', 'tenant', 'workspace', 'logout'])('redacts and rejects old-scope edits in the same tick as a %s change', (boundary) => {
    store.edit('body', 'private'); store.setLinksPreview(true); store.addLink('demo-file'); store.saveCheckpoint();
    if (boundary === 'user') { user.set({ userId: 'user-b' }); }
    if (boundary === 'tenant') { tenant.set({ tenantId: 'tenant-b', isAvailable: true }); }
    if (boundary === 'workspace') { workspace.set({ id: 'workspace-b' }); }
    if (boundary === 'logout') { authenticated.set(false); }
    // Deliberately do not flush effects before observing or invoking the store.
    expect(store.hasContent()).toBe(false);
    expect(store.view().content.links).toEqual([]);
    store.edit('body', 'old input event');
    store.edit('title', 'another queued old input event');
    store.setLinksPreview(true); store.addLink('demo-task');
    expect(store.hasContent()).toBe(false);
  });

  it('rejects an asynchronous result after an actor change before cleanup effects run', () => {
    const response = new Subject<RemoteDraftAttempt>();
    remote.attemptSave.mockReturnValue(response);
    store.edit('body', 'private'); store.checkServerSave();
    user.set({ userId: 'user-b' });
    response.next('conflict');
    expect(store.hasContent()).toBe(false);
    expect(store.view().status).toBe('editing');
    expect(store.view().message).toBe('');
  });

  it.each(['user', 'tenant', 'workspace', 'logout'])('clears text, checkpoints and preview links on the %s boundary', (boundary) => {
    const originalScope = store.view().scope!;
    store.edit('body', 'private text'); store.setLinksPreview(true); store.addLink('demo-file'); store.saveCheckpoint();
    if (boundary === 'user') { user.set({ userId: 'user-b' }); }
    if (boundary === 'tenant') { tenant.set({ tenantId: 'tenant-b', isAvailable: true }); }
    if (boundary === 'workspace') { workspace.set({ id: 'workspace-b' }); }
    if (boundary === 'logout') { authenticated.set(false); }
    TestBed.tick();
    expect(store.view().content).toEqual({ title: '', body: '', links: [] });
    expect(store.view().linksPreviewEnabled).toBe(false);
    expect(repository.read(originalScope).version).toBe(0);
  });

  it('cancels stale responses and clears immediately on authorization invalidation', () => {
    const response = new Subject<RemoteDraftAttempt>();
    remote.attemptSave.mockReturnValue(response);
    store.edit('body', 'private'); store.checkServerSave();
    clear();
    expect(response.observed).toBe(false);
    response.next('conflict');
    expect(store.view().scope).toBeNull();
    expect(store.hasContent()).toBe(false);
    expect(store.view().status).toBe('editing');
  });

  it('clears all local content on adapter denial without displaying resource details', () => {
    remote.attemptSave.mockReturnValue(of<RemoteDraftAttempt>('denied'));
    store.edit('body', 'private'); store.setLinksPreview(true); store.addLink('demo-task'); store.checkServerSave();
    expect(store.view().status).toBe('denied');
    expect(store.view().scope).toBeNull();
    expect(store.hasContent()).toBe(false);
  });

  it('keeps Links opt-in, rejects unknown and duplicate targets, and supports search/removal', () => {
    store.addLink('demo-file'); expect(store.view().content.links).toEqual([]);
    store.setLinksPreview(true); store.searchLinks('ｆｉｌｅ');
    expect(store.targets().map((item) => item.id)).toEqual(['demo-file']);
    store.addLink('real-resource-id'); store.addLink('demo-file'); store.addLink('demo-file');
    expect(store.view().content.links).toHaveLength(1);
    expect(store.targets()).toEqual([]);
    store.removeLink('demo-file'); expect(store.view().content.links).toEqual([]);
    expect(remote.attemptSave).not.toHaveBeenCalled();
  });

  it('discards text and checkpoints while retaining only the current scope', () => {
    const scope = store.view().scope;
    store.edit('body', 'private'); store.saveCheckpoint(); store.discard();
    expect(store.view().scope).toBe(scope);
    expect(store.hasContent()).toBe(false);
    expect(repository.read(scope!).version).toBe(0);
  });

  it('warns about tab refresh while content exists even outside the page lifetime', () => {
    store.edit('body', 'private');
    const event = new Event('beforeunload', { cancelable: true });
    globalThis.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    store.discard();
    const emptyEvent = new Event('beforeunload', { cancelable: true });
    globalThis.dispatchEvent(emptyEvent);
    expect(emptyEvent.defaultPrevented).toBe(false);
  });
});
