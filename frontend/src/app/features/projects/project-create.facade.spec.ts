import { Component, inject } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Observable, of } from 'rxjs';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { authSessionInterceptor } from '../../core/auth/auth-session.interceptor';
import { CsrfToken, CsrfTokenService } from '../../core/auth/csrf-token.service';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import {
  ProjectCreateApi,
  ProjectCreateInput,
  PROJECT_VISIBILITY_MEMBERS_ONLY,
} from './project-create.api';
import { ProjectCreateFacade } from './project-create.facade';
import { ProjectsFacade } from './projects.facade';

const workspaceId = '11111111-1111-4111-8111-111111111111';
const secondWorkspaceId = '99999999-9999-4999-8999-999999999999';
const projectId = '22222222-2222-4222-8222-222222222222';
const ownerUserId = '33333333-3333-4333-8333-333333333333';
const groupId = '44444444-4444-4444-8444-444444444444';

const optionsEnvelope = {
  requestId: 'request-options',
  data: {
    workspaceId,
    canCreateUngrouped: true,
    allowedVisibilities: [PROJECT_VISIBILITY_MEMBERS_ONLY],
    groups: [{ id: groupId, name: 'Research Group' }],
  },
  warnings: [],
};

const input: ProjectCreateInput = {
  title: 'Evidence review',
  description: null,
  groupId: null,
  visibility: PROJECT_VISIBILITY_MEMBERS_ONLY,
  startDate: '2026-08-24',
  endDate: '2026-08-28',
};

const successEnvelope = {
  requestId: 'request-create',
  data: {
    id: projectId,
    workspaceId,
    groupId: null,
    ownerUserId,
    title: input.title,
    description: null,
    status: 0,
    visibility: PROJECT_VISIBILITY_MEMBERS_ONLY,
    activationState: 1,
    startDate: input.startDate,
    endDate: input.endDate,
    versionNo: 1,
    createdAt: '2026-08-24T01:02:03Z',
  },
  warnings: [],
};

const projectConfirmation = {
  id: projectId,
  workspaceId,
  status: 0,
  activationState: 1,
};

@Component({ standalone: true, template: '<p>{{ facade.createState().message }}</p>@for (error of facade.createState().fieldErrors; track $index) {<span>{{error.message}}</span>}<b>{{facade.options().data?.groups?.[0]?.name}}</b>' })
class ProjectCreateFacadeFeedbackHost { readonly facade = inject(ProjectCreateFacade); }

describe('ProjectCreateFacade', () => {
  let facade: ProjectCreateFacade;
  let http: HttpTestingController;
  let authSession: WritableSignal<Record<string, unknown>>;
  let activeWorkspace: WritableSignal<{ id: string; label: string } | null>;
  let protectedClearer:
    ((reason: 'session' | 'tenant' | 'authorization' | 'workspace') => void) | null;
  let router: { navigate: ReturnType<typeof vi.fn> };
  let projects: { retryProjects: ReturnType<typeof vi.fn> };
  let csrfTokens: {
    ensureToken: ReturnType<typeof vi.fn>;
    clearToken: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    window.localStorage.setItem('coglatas.locale', 'en');
    authSession = signal({
      isAuthenticated: true,
      currentTenant: { tenantId: 'tenant-a' },
      currentUser: { userId: 'user-a' },
    });
    activeWorkspace = signal({ id: workspaceId, label: 'Evidence Workspace' });
    protectedClearer = null;
    router = { navigate: vi.fn().mockResolvedValue(true) };
    projects = { retryProjects: vi.fn() };
    csrfTokens = {
      ensureToken: vi.fn(() =>
        of<CsrfToken>({
          token: 'csrf-project-create',
          headerName: 'X-CSRF-Token',
          cacheKey: 'tenant-a',
        }),
      ),
      clearToken: vi.fn(),
    };

    TestBed.configureTestingModule({
      imports: [ProjectCreateFacadeFeedbackHost],
      providers: [
        provideHttpClient(withInterceptors([authSessionInterceptor])),
        provideHttpClientTesting(),
        ProjectCreateFacade,
        { provide: Router, useValue: router },
        { provide: ProjectsFacade, useValue: projects },
        {
          provide: AuthSessionFacade,
          useValue: {
            session: authSession,
            csrfCacheKey: () => 'tenant-a',
            refreshCurrentUser: () => of(null),
            handleTerminal401: vi.fn(),
          },
        },
        { provide: CsrfTokenService, useValue: csrfTokens },
        { provide: ActiveWorkspaceFacade, useValue: { activeWorkspace } },
        {
          provide: RealtimeFacade,
          useValue: {
            registerProtectedStateClearer: vi.fn(
              (
                _owner: string,
                clearer: (reason: 'session' | 'tenant' | 'authorization' | 'workspace') => void,
              ) => {
                protectedClearer = clearer;
                return () => undefined;
              },
            ),
          },
        },
      ],
    });

    facade = TestBed.inject(ProjectCreateFacade);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    http.verify();
    TestBed.resetTestingModule();
  });

  const loadOptions = async (): Promise<void> => {
    const load = facade.loadOptions(workspaceId);
    const request = http.expectOne(`/api/workspaces/${workspaceId}/projects/create-options`);
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush(optionsEnvelope);
    await expect(load).resolves.toBe(true);
  };


  it('hot-switches mounted create validation while keeping options and requests stable', async () => {
    await loadOptions();
    await facade.createProject(workspaceId, { ...input, title: '' });
    const fixture = TestBed.createComponent(ProjectCreateFacadeFeedbackHost);
    const i18n = TestBed.inject(I18nService);
    const options = facade.options().data;
    i18n.setLocale('ja'); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('プロジェクト名を入力してください');
    expect(fixture.nativeElement.textContent).toContain('Research Group');
    i18n.setLocale('en'); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Enter a Project name.');
    expect(facade.options().data).toBe(options);
    expect(http.match(() => true)).toHaveLength(0);
  });

  it('loads only strict server-owned options and fails closed on denial', async () => {
    await loadOptions();
    expect(facade.options()).toMatchObject({
      status: 'ready',
      data: {
        workspaceId,
        canCreateUngrouped: true,
        groups: [{ id: groupId, name: 'Research Group' }],
      },
    });

    const noAuthority = facade.loadOptions(workspaceId);
    http.expectOne(`/api/workspaces/${workspaceId}/projects/create-options`).flush({
      requestId: 'request-options-no-authority',
      data: {
        workspaceId,
        canCreateUngrouped: false,
        allowedVisibilities: [],
        groups: [],
      },
      warnings: [],
    });
    await expect(noAuthority).resolves.toBe(false);
    expect(facade.options().status).toBe('denied');

    const denied = facade.loadOptions(workspaceId);
    http
      .expectOne(`/api/workspaces/${workspaceId}/projects/create-options`)
      .flush(
        { requestId: 'request-denied', message: 'Denied' },
        { status: 403, statusText: 'Forbidden' },
      );
    await expect(denied).resolves.toBe(false);
    expect(facade.options().status).toBe('denied');
  });

  it('posts the canonical body once, commits before refresh, and navigates to Draft detail', async () => {
    await loadOptions();

    const create = facade.createProject(workspaceId, {
      ...input,
      title: '  Evidence review  ',
      description: '   ',
    });
    const post = http.expectOne(`/api/workspaces/${workspaceId}/projects`);
    expect(post.request.method).toBe('POST');
    expect(post.request.withCredentials).toBe(true);
    expect(post.request.body).toEqual({
      title: 'Evidence review',
      description: null,
      groupId: null,
      visibility: PROJECT_VISIBILITY_MEMBERS_ONLY,
      startDate: '2026-08-24',
      endDate: '2026-08-28',
    });
    expect(post.request.body).not.toHaveProperty('workspaceId');
    expect(post.request.body).not.toHaveProperty('members');
    expect(post.request.headers.get('Idempotency-Key')).toMatch(/^project-create-[\x20-\x7e]+$/u);

    await expect(facade.createProject(workspaceId, input)).resolves.toBe(false);
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request !== post.request,
    );

    post.flush(successEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    const confirmation = http.expectOne(`/api/projects/${projectId}`);
    expect(confirmation.request.method).toBe('GET');
    expect(confirmation.request.withCredentials).toBe(true);
    confirmation.flush(projectConfirmation);
    await expect(create).resolves.toBe(true);
    expect(projects.retryProjects).toHaveBeenCalledOnce();
    expect(router.navigate).toHaveBeenCalledWith(['/projects', projectId]);
    expect(facade.createState()).toMatchObject({
      status: 'succeeded',
      createdProjectId: projectId,
      requestId: 'request-create',
    });
  });

  it('reuses one key for unchanged uncertain retries and rotates it for payload changes', async () => {
    await loadOptions();

    const first = facade.createProject(workspaceId, input);
    const firstPost = http.expectOne(`/api/workspaces/${workspaceId}/projects`);
    const firstKey = firstPost.request.headers.get('Idempotency-Key');
    firstPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await expect(first).resolves.toBe(false);
    expect(facade.createState().message).toContain('may have been created');

    facade.resetCreatePresentation();
    const retry = facade.createProject(workspaceId, { ...input, title: ` ${input.title} ` });
    const retryPost = http.expectOne(`/api/workspaces/${workspaceId}/projects`);
    expect(retryPost.request.headers.get('Idempotency-Key')).toBe(firstKey);
    retryPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await retry;

    facade.resetCreatePresentation();
    const changed = facade.createProject(workspaceId, { ...input, title: 'Changed title' });
    const changedPost = http.expectOne(`/api/workspaces/${workspaceId}/projects`);
    expect(changedPost.request.headers.get('Idempotency-Key')).not.toBe(firstKey);
    changedPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await changed;
  });

  it('retains the same key after a malformed HTTP 201', async () => {
    await loadOptions();

    const first = facade.createProject(workspaceId, input);
    const firstPost = http.expectOne(`/api/workspaces/${workspaceId}/projects`);
    const firstKey = firstPost.request.headers.get('Idempotency-Key');
    firstPost.flush(
      { requestId: 'request-create', warnings: [] },
      { status: 201, statusText: 'Created' },
    );
    await first;

    facade.resetCreatePresentation();
    const retry = facade.createProject(workspaceId, input);
    const retryPost = http.expectOne(`/api/workspaces/${workspaceId}/projects`);
    expect(retryPost.request.headers.get('Idempotency-Key')).toBe(firstKey);
    retryPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await retry;
  });

  it('uses refresh/navigation-only recovery after a strict 201 and never repeats POST', async () => {
    await loadOptions();
    router.navigate.mockResolvedValueOnce(false).mockResolvedValueOnce(true);

    const create = facade.createProject(workspaceId, input);
    http
      .expectOne(`/api/workspaces/${workspaceId}/projects`)
      .flush(successEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    http.expectOne(`/api/projects/${projectId}`).flush(projectConfirmation);
    await expect(create).resolves.toBe(false);
    expect(facade.createState().status).toBe('committedPendingNavigation');

    await expect(facade.createProject(workspaceId, input)).resolves.toBe(false);
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );

    const retryNavigation = facade.retryCreatedProjectNavigation();
    http.expectOne(`/api/projects/${projectId}`).flush(projectConfirmation);
    await expect(retryNavigation).resolves.toBe(true);
    expect(projects.retryProjects).toHaveBeenCalledTimes(2);
    expect(router.navigate).toHaveBeenCalledTimes(2);
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );
  });

  it('keeps committed navigation alive when own-command authorization invalidation wins the navigation race', async () => {
    await loadOptions();
    let resolveNavigation!: (value: boolean) => void;
    router.navigate.mockReturnValueOnce(
      new Promise<boolean>((resolve) => {
        resolveNavigation = resolve;
      }),
    );

    const create = facade.createProject(workspaceId, input);
    http
      .expectOne(`/api/workspaces/${workspaceId}/projects`)
      .flush(successEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    http.expectOne(`/api/projects/${projectId}`).flush(projectConfirmation);
    await Promise.resolve();
    expect(router.navigate).toHaveBeenCalledOnce();

    protectedClearer?.('authorization');
    expect(facade.options().status).toBe('idle');
    expect(facade.createState().status).toBe('submitting');
    resolveNavigation(true);
    await expect(create).resolves.toBe(true);
    expect(facade.createState()).toMatchObject({
      status: 'succeeded',
      createdProjectId: projectId,
      requestId: 'request-create',
    });
    expect(router.navigate).toHaveBeenCalledTimes(1);
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );
  });

  it('keeps a dispatched create alive when authorization invalidation precedes the strict 201', async () => {
    await loadOptions();

    const create = facade.createProject(workspaceId, input);
    const post = http.expectOne(`/api/workspaces/${workspaceId}/projects`);

    // An own-command authorization refresh can arrive after browser dispatch
    // but before the strict 201. Aborting here cannot undo a server commit, so
    // this exact invocation stays alive while protected options are cleared.
    protectedClearer?.('authorization');
    expect(post.cancelled).toBe(false);
    expect(facade.options()).toMatchObject({ status: 'error', workspaceId });
    expect(facade.createState().status).toBe('submitting');

    post.flush(successEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    http.expectOne(`/api/projects/${projectId}`).flush(projectConfirmation);
    await expect(create).resolves.toBe(true);
    expect(router.navigate).toHaveBeenCalledWith(['/projects', projectId]);
    expect(facade.createState()).toMatchObject({
      status: 'succeeded',
      createdProjectId: projectId,
      requestId: 'request-create',
    });
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );
  });

  it('requires options reauthorization when the registered authorization clearer stops create during a delayed CSRF preflight', async () => {
    await loadOptions();

    const api = TestBed.inject(ProjectCreateApi);
    const createSpy = vi.spyOn(api, 'createProject');
    let releaseDelayedCsrf!: () => void;
    csrfTokens.ensureToken.mockReturnValueOnce(
      new Observable<CsrfToken>((subscriber) => {
        releaseDelayedCsrf = () => {
          subscriber.next({
            token: 'csrf-project-create',
            headerName: 'X-CSRF-Token',
            cacheKey: 'tenant-a',
          });
          subscriber.complete();
        };
      }),
    );

    const scheduled = facade.createProject(workspaceId, input);
    expect(createSpy).toHaveBeenCalledOnce();
    expect(csrfTokens.ensureToken).toHaveBeenCalledWith('tenant-a');
    const scheduledKey = createSpy.mock.calls[0]?.[2];
    expect(scheduledKey).toMatch(/^project-create-[\x20-\x7e]+$/u);

    // This invokes the production registration while the intercepted POST is
    // still waiting for its cold CSRF preflight. Releasing that preflight
    // later must not cause a browser POST.
    protectedClearer?.('authorization');
    await expect(scheduled).resolves.toBe(false);
    expect(facade.options()).toMatchObject({ status: 'error', workspaceId });
    expect(facade.createState().message).toContain('stopped before it was sent');
    expect(facade.createState().message).not.toContain('may have been created');
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );
    releaseDelayedCsrf();
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );

    // The preserved canonical attempt can only continue after an
    // authoritative options refresh and an explicit same-details submission.
    const optionsReload = facade.loadOptions(workspaceId);
    http.expectOne(`/api/workspaces/${workspaceId}/projects/create-options`).flush(optionsEnvelope);
    await expect(optionsReload).resolves.toBe(true);
    facade.resetCreatePresentation();

    const retry = facade.createProject(workspaceId, { ...input, title: ` ${input.title} ` });
    const post = http.expectOne(`/api/workspaces/${workspaceId}/projects`);
    expect(createSpy).toHaveBeenCalledTimes(2);
    expect(post.request.headers.get('Idempotency-Key')).toBe(scheduledKey);
    expect(post.request.body).toEqual({
      title: input.title,
      description: input.description,
      groupId: input.groupId,
      visibility: input.visibility,
      startDate: input.startDate,
      endDate: input.endDate,
    });
    post.flush(successEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    http.expectOne(`/api/projects/${projectId}`).flush(projectConfirmation);
    await expect(retry).resolves.toBe(true);
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );
  });

  it('does not downgrade a previously dispatched key when a later retry is stopped during CSRF preflight', async () => {
    await loadOptions();

    const first = facade.createProject(workspaceId, input);
    const firstPost = http.expectOne(`/api/workspaces/${workspaceId}/projects`);
    const firstKey = firstPost.request.headers.get('Idempotency-Key');
    firstPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await expect(first).resolves.toBe(false);
    expect(facade.createState().message).toContain('may have been created');

    const firstOptionsReload = facade.loadOptions(workspaceId);
    http.expectOne(`/api/workspaces/${workspaceId}/projects/create-options`).flush(optionsEnvelope);
    await expect(firstOptionsReload).resolves.toBe(true);

    let releaseDelayedCsrf!: () => void;
    csrfTokens.ensureToken.mockReturnValueOnce(
      new Observable<CsrfToken>((subscriber) => {
        releaseDelayedCsrf = () => {
          subscriber.next({
            token: 'csrf-project-create',
            headerName: 'X-CSRF-Token',
            cacheKey: 'tenant-a',
          });
          subscriber.complete();
        };
      }),
    );

    // The key is historically uncertain, but this retry has not dispatched.
    // Authorization invalidation must therefore cancel this invocation without
    // downgrading the key's server-commit uncertainty.
    const stoppedRetry = facade.createProject(workspaceId, input);
    protectedClearer?.('authorization');
    await expect(stoppedRetry).resolves.toBe(false);
    releaseDelayedCsrf();
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );
    expect(facade.createState().message).toContain('may have been created');
    expect(facade.createState().message).not.toContain('stopped before it was sent');

    const finalOptionsReload = facade.loadOptions(workspaceId);
    http.expectOne(`/api/workspaces/${workspaceId}/projects/create-options`).flush(optionsEnvelope);
    await expect(finalOptionsReload).resolves.toBe(true);

    const finalRetry = facade.createProject(workspaceId, input);
    const finalPost = http.expectOne(`/api/workspaces/${workspaceId}/projects`);
    expect(finalPost.request.headers.get('Idempotency-Key')).toBe(firstKey);
    finalPost.flush(successEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    http.expectOne(`/api/projects/${projectId}`).flush(projectConfirmation);
    await expect(finalRetry).resolves.toBe(true);
  });

  it('keeps committed navigation alive even if another clearer already hid ActiveWorkspace, then destroys it on the actual Workspace boundary', async () => {
    await loadOptions();
    let resolveNavigation!: (value: boolean) => void;
    router.navigate.mockReturnValueOnce(
      new Promise<boolean>((resolve) => {
        resolveNavigation = resolve;
      }),
    );

    const create = facade.createProject(workspaceId, input);
    http
      .expectOne(`/api/workspaces/${workspaceId}/projects`)
      .flush(successEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    http.expectOne(`/api/projects/${projectId}`).flush(projectConfirmation);
    await Promise.resolve();

    // Protected clearers can run in a different registration order. The
    // authorization callback must not depend on another facade still exposing
    // ActiveWorkspace, and the already-authorized navigation may finish.
    activeWorkspace.set(null);
    protectedClearer?.('authorization');
    expect(facade.createState().status).toBe('submitting');
    resolveNavigation(true);
    await expect(create).resolves.toBe(true);
    expect(facade.createState().status).toBe('succeeded');

    protectedClearer?.('workspace');
    expect(facade.createState().status).toBe('idle');
    await expect(facade.retryCreatedProjectNavigation()).resolves.toBe(false);
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );
  });

  it('keeps one committed POST when confirmation GET fails across close and reopen', async () => {
    await loadOptions();

    const create = facade.createProject(workspaceId, input);
    http
      .expectOne(`/api/workspaces/${workspaceId}/projects`)
      .flush(successEnvelope, { status: 201, statusText: 'Created' });
    await Promise.resolve();
    http
      .expectOne(`/api/projects/${projectId}`)
      .flush(null, { status: 503, statusText: 'Service Unavailable' });
    await expect(create).resolves.toBe(false);
    expect(facade.createState().status).toBe('committedPendingNavigation');

    // The own-command invalidation can hide capability/options before the user
    // closes. Reopening must still expose GET/navigation-only recovery.
    protectedClearer?.('authorization');
    expect(facade.options().status).toBe('idle');
    facade.resetCreatePresentation();
    await expect(facade.createProject(workspaceId, input)).resolves.toBe(false);
    http.expectNone(
      (request) =>
        request.url === `/api/workspaces/${workspaceId}/projects` && request.method === 'POST',
    );

    const retry = facade.retryCreatedProjectNavigation();
    http.expectOne(`/api/projects/${projectId}`).flush(projectConfirmation);
    await expect(retry).resolves.toBe(true);
  });

  it('rejects disallowed Groups and date inversion before sending a request', async () => {
    await loadOptions();

    await expect(
      facade.createProject(workspaceId, {
        ...input,
        groupId: secondWorkspaceId,
        endDate: '2026-08-20',
      }),
    ).resolves.toBe(false);
    expect(facade.createState().fieldErrors).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ field: 'groupId' }),
        expect.objectContaining({ field: 'endDate' }),
      ]),
    );
    http.expectNone(`/api/workspaces/${workspaceId}/projects`);
  });

  it('cancels and clears an in-flight protected mutation at an authorization boundary', async () => {
    await loadOptions();
    const create = facade.createProject(workspaceId, input);
    const post = http.expectOne(`/api/workspaces/${workspaceId}/projects`);

    protectedClearer?.('workspace');
    await expect(create).resolves.toBe(false);
    expect(post.cancelled).toBe(true);
    expect(facade.options().status).toBe('idle');
    expect(facade.createState().status).toBe('idle');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('drops the active scope when authenticated identity or Workspace changes', async () => {
    await loadOptions();

    authSession.set({
      isAuthenticated: true,
      currentTenant: { tenantId: 'tenant-a' },
      currentUser: { userId: 'user-b' },
    });
    TestBed.flushEffects();
    expect(facade.options().status).toBe('idle');

    activeWorkspace.set({ id: secondWorkspaceId, label: 'Other Workspace' });
    TestBed.flushEffects();
    expect(facade.createState().status).toBe('idle');
  });
});
