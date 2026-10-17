import { Component, inject } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, Subject } from 'rxjs';

import { AuthSessionFacade } from '../../core/auth/auth-session.facade';
import { authSessionInterceptor } from '../../core/auth/auth-session.interceptor';
import { CsrfToken, CsrfTokenService } from '../../core/auth/csrf-token.service';
import {
  ProtectedStateClearReason,
  RealtimeCatchUpCallback,
  RealtimeFacade,
} from '../../core/realtime/realtime.facade';
import { DurableRealtimeEvent } from '../../core/realtime/realtime.models';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { TaskCreateInput } from './task-create.api';
import { TaskCreateFacade } from './task-create.facade';

const projectId = '11111111-1111-4111-8111-111111111111';
const workspaceId = '22222222-2222-4222-8222-222222222222';
const milestoneId = '33333333-3333-4333-8333-333333333333';
const assigneeId = '44444444-4444-4444-8444-444444444444';
const taskId = '55555555-5555-4555-8555-555555555555';
const workflowStageId = '66666666-6666-4666-8666-666666666666';

const optionsEnvelope = {
  requestId: 'task-create-options-200',
  data: {
    projectId,
    workspaceId,
    projectTitle: 'Evidence Project',
    canCreateTask: true,
    canManageProject: true,
    milestones: [{ id: milestoneId, title: 'Evidence milestone' }],
    assignees: [{ userId: assigneeId, displayName: 'Project member' }],
    projectScope: {
      policy: { webEnabled: false, projectFilesEnabled: true },
      version: 1,
      canSetTaskOverride: true,
    },
  },
  warnings: [],
};

const input: TaskCreateInput = {
  title: 'Evidence review',
  description: 'Review the source scope decision.',
  priority: 'high',
  milestoneId,
  startDate: '2026-08-24',
  dueDate: '2026-08-28',
  goal: 'Review evidence',
  deliverable: 'Decision note',
  constraints: 'No raw source persistence',
  primaryAssigneeUserId: assigneeId,
  sourceScopeMode: 'Inherit',
  taskOverridePolicy: null,
};

const successEnvelope = {
  requestId: 'task-create-201',
  data: {
    taskId,
    projectId,
    workspaceId,
    milestoneId,
    primaryAssigneeUserId: assigneeId,
    title: input.title,
    priority: 2,
    status: 0,
    workflowStageId,
    version: 1,
    sourceScopeMode: 'Inherit',
    taskOverridePolicy: null,
  },
  warnings: [],
};

@Component({ standalone: true, template: '<p>{{ facade.createState().message }}</p>@for (error of facade.createState().fieldErrors; track $index) {<span>{{error.message}}</span>}<b>{{facade.options().data?.projectTitle}}</b>' })
class TaskCreateFacadeFeedbackHost { readonly facade = inject(TaskCreateFacade); }

describe('TaskCreateFacade', () => {
  let facade: TaskCreateFacade;
  let http: HttpTestingController;
  let authSession: WritableSignal<Record<string, unknown>>;
  let router: { navigate: ReturnType<typeof vi.fn> };
  let durableEvents: Subject<DurableRealtimeEvent>;
  let protectedStateClearer: ((reason: ProtectedStateClearReason) => void) | null;
  let realtimeCatchUp: RealtimeCatchUpCallback | null;

  beforeEach(() => {
    window.localStorage.setItem('coglatas.locale', 'en');
    authSession = signal({
      isAuthenticated: true,
      currentTenant: { tenantId: 'tenant-a' },
      currentUser: { userId: 'user-a' },
    });
    router = { navigate: vi.fn().mockResolvedValue(true) };
    durableEvents = new Subject<DurableRealtimeEvent>();
    protectedStateClearer = null;
    realtimeCatchUp = null;

    TestBed.configureTestingModule({
      imports: [TaskCreateFacadeFeedbackHost],
      providers: [
        provideHttpClient(withInterceptors([authSessionInterceptor])),
        provideHttpClientTesting(),
        TaskCreateFacade,
        { provide: Router, useValue: router },
        {
          provide: AuthSessionFacade,
          useValue: {
            session: authSession,
            csrfCacheKey: () => 'tenant-a',
            refreshCurrentUser: () => of(null),
            handleTerminal401: vi.fn(),
          },
        },
        {
          provide: CsrfTokenService,
          useValue: {
            ensureToken: vi.fn(() =>
              of<CsrfToken>({
                token: 'csrf-task-create',
                headerName: 'X-CSRF-Token',
                cacheKey: 'tenant-a',
              }),
            ),
            clearToken: vi.fn(),
          },
        },
        {
          provide: ActiveWorkspaceFacade,
          useValue: { activeWorkspace: signal({ id: workspaceId, label: 'Evidence Workspace' }) },
        },
        {
          provide: RealtimeFacade,
          useValue: {
            durableEvents$: durableEvents.asObservable(),
            registerProtectedStateClearer: vi.fn((_: string, clearer: (reason: ProtectedStateClearReason) => void) => {
              protectedStateClearer = clearer;
              return () => undefined;
            }),
            registerSubscription: vi.fn(() => () => undefined),
            registerCatchUp: vi.fn((_: string, catchUp: RealtimeCatchUpCallback) => {
              realtimeCatchUp = catchUp;
              return () => undefined;
            }),
          },
        },
      ],
    });
    facade = TestBed.inject(TaskCreateFacade);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    facade.release();
    http.verify();
    TestBed.resetTestingModule();
  });

  const loadOptions = async (): Promise<void> => {
    const load = facade.load(projectId);
    const request = http.expectOne(`/api/projects/${projectId}/tasks/create-options`);
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush(optionsEnvelope);
    await expect(load).resolves.toBe(true);
  };

  const catchUpAfterAuthorization = async (denied = false): Promise<void> => {
    expect(realtimeCatchUp).not.toBeNull();
    await realtimeCatchUp!({
      deniedOwners: denied ? new Set(['task-create']) : new Set(),
    });
  };


  it('hot-switches mounted Task validation without resetting options or sending a create', async () => {
    await loadOptions();
    await facade.createTask({ ...input, title: '' });
    const fixture = TestBed.createComponent(TaskCreateFacadeFeedbackHost);
    const i18n = TestBed.inject(I18nService);
    const options = facade.options().data;
    i18n.setLocale('ja'); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('タスクのタイトルを入力してください');
    expect(fixture.nativeElement.textContent).toContain('Evidence Project');
    i18n.setLocale('en'); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Enter a Task title.');
    expect(facade.options().data).toBe(options);
    expect(http.match(() => true)).toHaveLength(0);
  });

  it('uses server-owned choices and posts only the strict canonical command with one idempotency key', async () => {
    await loadOptions();

    const create = facade.createTask({ ...input, title: '  Evidence review  ' });
    const post = http.expectOne(`/api/projects/${projectId}/tasks/create`);
    expect(post.request.method).toBe('POST');
    expect(post.request.withCredentials).toBe(true);
    expect(post.request.headers.get('Idempotency-Key')).toMatch(/^task-create-[\x20-\x7e]+$/u);
    expect(post.request.headers.get('X-CSRF-Token')).toBe('csrf-task-create');
    expect(post.request.body).toEqual({
      title: 'Evidence review',
      description: input.description,
      priority: 2,
      milestoneId,
      startDate: input.startDate,
      dueDate: input.dueDate,
      goal: input.goal,
      deliverable: input.deliverable,
      constraints: input.constraints,
      primaryAssigneeUserId: assigneeId,
      sourceScopeMode: 'Inherit',
    });
    expect(post.request.body).not.toHaveProperty('projectId');
    expect(post.request.body).not.toHaveProperty('workspaceId');
    expect(post.request.body).not.toHaveProperty('webUrl');
    expect(post.request.body).not.toHaveProperty('provider');
    post.flush(successEnvelope, { status: 201, statusText: 'Created' });

    await expect(create).resolves.toBe(true);
    expect(router.navigate).toHaveBeenCalledWith(['/projects', projectId, 'tasks', taskId]);
    expect(facade.createState()).toMatchObject({
      status: 'succeeded',
      createdTaskId: taskId,
      requestId: 'task-create-201',
    });
  });

  it('keeps an unchanged retry on the same idempotency key after an uncertain outcome', async () => {
    await loadOptions();

    const first = facade.createTask(input);
    const firstPost = http.expectOne(`/api/projects/${projectId}/tasks/create`);
    const firstKey = firstPost.request.headers.get('Idempotency-Key');
    firstPost.flush(null, { status: 503, statusText: 'Service Unavailable' });
    await expect(first).resolves.toBe(false);
    expect(facade.createState().message).toContain('may have been created');

    facade.resetCreatePresentation();
    const retry = facade.createTask({ ...input, title: ` ${input.title} ` });
    const retryPost = http.expectOne(`/api/projects/${projectId}/tasks/create`);
    expect(retryPost.request.headers.get('Idempotency-Key')).toBe(firstKey);
    retryPost.flush(successEnvelope, { status: 201, statusText: 'Created' });
    await expect(retry).resolves.toBe(true);
  });

  it('uses navigation-only recovery after a strict 201 and never sends another create POST', async () => {
    await loadOptions();
    router.navigate.mockResolvedValueOnce(false).mockResolvedValueOnce(true);

    const create = facade.createTask(input);
    http
      .expectOne(`/api/projects/${projectId}/tasks/create`)
      .flush(successEnvelope, { status: 201, statusText: 'Created' });
    await expect(create).resolves.toBe(false);
    expect(facade.createState().status).toBe('committedPendingNavigation');

    await expect(facade.createTask(input)).resolves.toBe(false);
    http.expectNone((request) =>
      request.url === `/api/projects/${projectId}/tasks/create` && request.method === 'POST',
    );

    await expect(facade.retryCreatedTaskNavigation()).resolves.toBe(true);
    expect(router.navigate).toHaveBeenCalledTimes(2);
    http.expectNone((request) =>
      request.url === `/api/projects/${projectId}/tasks/create` && request.method === 'POST',
    );
  });

  it('clears an abandoned navigation recovery so a re-entered Project can create a new Task', async () => {
    await loadOptions();
    router.navigate.mockResolvedValueOnce(false).mockResolvedValueOnce(true);

    const first = facade.createTask(input);
    http
      .expectOne(`/api/projects/${projectId}/tasks/create`)
      .flush(successEnvelope, { status: 201, statusText: 'Created' });
    await expect(first).resolves.toBe(false);
    expect(facade.createState().status).toBe('committedPendingNavigation');

    // The page explicitly releases this root-scoped state when the user
    // chooses Return to Project rather than retrying navigation.
    facade.release();
    const reentered = facade.load(projectId);
    http.expectOne(`/api/projects/${projectId}/tasks/create-options`).flush(optionsEnvelope);
    await expect(reentered).resolves.toBe(true);

    const second = facade.createTask({ ...input, title: 'A different Task after recovery' });
    const secondPost = http.expectOne(`/api/projects/${projectId}/tasks/create`);
    expect(secondPost.request.body).toMatchObject({ title: 'A different Task after recovery' });
    secondPost.flush(
      {
        ...successEnvelope,
        data: { ...successEnvelope.data, taskId: '77777777-7777-4777-8777-777777777777', title: 'A different Task after recovery' },
      },
      { status: 201, statusText: 'Created' },
    );
    await expect(second).resolves.toBe(true);
  });

  it('refreshes authoritative options when a Project realtime event arrives', async () => {
    await loadOptions();
    durableEvents.next({
      eventId: 'event-1',
      eventType: 'Projects.TaskAssignmentChanged.v1',
      payloadSchemaVersion: 1,
      occurredAt: '2026-08-24T00:00:00Z',
      tenantId: 'tenant-a',
      aggregateType: 'Task',
      aggregateId: taskId,
      aggregateVersion: 2,
      actor: { actorType: 'System', actorId: null },
      correlationId: null,
      causationId: null,
      payload: { projectId },
    });

    const refresh = http.expectOne(`/api/projects/${projectId}/tasks/create-options`);
    expect(refresh.request.method).toBe('GET');
    refresh.flush({
      ...optionsEnvelope,
      requestId: 'task-create-options-refresh',
      data: { ...optionsEnvelope.data, assignees: [] },
    });
    await Promise.resolve();
    expect(facade.options()).toMatchObject({
      status: 'ready',
      data: { assignees: [] },
    });
  });

  it('refetches only fresh server-owned options after authorization recovery clears a mounted route', async () => {
    await loadOptions();
    expect(protectedStateClearer).not.toBeNull();

    protectedStateClearer!('authorization');
    expect(facade.options()).toEqual({ status: 'idle' });
    await expect(facade.createTask(input)).resolves.toBe(false);
    http.expectNone((request) => request.method === 'POST' && request.url.endsWith('/tasks/create'));

    const catchUp = catchUpAfterAuthorization();
    const freshOptions = http.expectOne(`/api/projects/${projectId}/tasks/create-options`);
    freshOptions.flush({
      ...optionsEnvelope,
      requestId: 'task-create-options-reauthorized',
      data: {
        ...optionsEnvelope.data,
        canManageProject: false,
        assignees: [],
      },
    });
    await catchUp;

    expect(facade.options()).toMatchObject({
      status: 'ready',
      requestId: 'task-create-options-reauthorized',
      data: {
        canManageProject: false,
        assignees: [],
      },
    });
  });

  it('ignores a cancelled options response until an allowed authorization catch-up finishes', async () => {
    const initialLoad = facade.load(projectId);
    const cancelledOptions = http.expectOne(`/api/projects/${projectId}/tasks/create-options`);
    expect(protectedStateClearer).not.toBeNull();

    protectedStateClearer!('authorization');
    expect(cancelledOptions.cancelled).toBe(true);
    await expect(initialLoad).resolves.toBe(false);
    expect(facade.options()).toEqual({ status: 'idle' });

    const catchUp = catchUpAfterAuthorization();
    const freshOptions = http.expectOne(`/api/projects/${projectId}/tasks/create-options`);
    freshOptions.flush({
      ...optionsEnvelope,
      requestId: 'task-create-options-fresh-after-cancel',
    });
    await catchUp;

    expect(facade.options()).toMatchObject({
      status: 'ready',
      requestId: 'task-create-options-fresh-after-cancel',
    });
  });

  it('keeps protected options cleared when the fresh authorization catch-up is denied by the server', async () => {
    await loadOptions();
    protectedStateClearer!('authorization');
    expect(facade.options()).toEqual({ status: 'idle' });

    const catchUp = catchUpAfterAuthorization();
    http
      .expectOne(`/api/projects/${projectId}/tasks/create-options`)
      .flush({ error: { code: 'Forbidden' } }, { status: 403, statusText: 'Forbidden' });
    await catchUp;

    expect(facade.options()).toMatchObject({
      status: 'denied',
      projectId,
      message: 'Task creation is not available for this Project.',
    });
    expect(facade.options().data).toBeUndefined();
    await expect(facade.createTask(input)).resolves.toBe(false);
    http.expectNone((request) => request.method === 'POST' && request.url.endsWith('/tasks/create'));
  });

  it('shows a generic denial when the route subscription is denied during authorization catch-up', async () => {
    await loadOptions();
    await catchUpAfterAuthorization(true);

    expect(facade.options()).toEqual({
      status: 'denied',
      projectId,
      message: 'Task creation is not available for this Project.',
    });
    await expect(facade.createTask(input)).resolves.toBe(false);
    http.expectNone(`/api/projects/${projectId}/tasks/create-options`);
    http.expectNone((request) => request.method === 'POST' && request.url.endsWith('/tasks/create'));
  });

  it('does not restore route intent after a workspace boundary clear', async () => {
    await loadOptions();
    expect(protectedStateClearer).not.toBeNull();

    protectedStateClearer!('workspace');
    await catchUpAfterAuthorization();

    expect(facade.options()).toEqual({ status: 'idle' });
    http.expectNone(`/api/projects/${projectId}/tasks/create-options`);
  });
});
