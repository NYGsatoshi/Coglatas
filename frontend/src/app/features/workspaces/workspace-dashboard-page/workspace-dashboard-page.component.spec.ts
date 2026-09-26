import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { COGLATAS_WORKSPACES_DASHBOARD_MOCK, WorkspacesFacade } from '../workspaces.facade';
import {
  DEFAULT_WORKSPACES,
  LONG_NAME_WORKSPACE,
  OWNER_WORKSPACE,
  READ_ONLY_WORKSPACE,
  SYSTEM_ADMIN_WORKSPACE,
  WORKSPACE_DASHBOARD_SCENARIOS,
} from '../workspaces.mock';
import { WorkspaceCreateViewModel, WorkspaceDashboardViewModel } from '../workspaces.types';
import { WorkspaceDashboardPageComponent } from './workspace-dashboard-page.component';

const renderDashboard = async (
  dashboard: WorkspaceDashboardViewModel,
): Promise<ComponentFixture<WorkspaceDashboardPageComponent>> => {
  await TestBed.configureTestingModule({
    imports: [WorkspaceDashboardPageComponent],
    providers: [provideRouter([]), { provide: COGLATAS_WORKSPACES_DASHBOARD_MOCK, useValue: dashboard }],
  }).compileComponents();

  const fixture = TestBed.createComponent(WorkspaceDashboardPageComponent);
  fixture.detectChanges();
  return fixture;
};

const textContent = (fixture: ComponentFixture<WorkspaceDashboardPageComponent>): string =>
  (fixture.nativeElement as HTMLElement).textContent ?? '';

const renderDashboardWithCreateState = async (
  dashboard: WorkspaceDashboardViewModel,
  createState: WorkspaceCreateViewModel,
) => {
  const dashboardState = signal(dashboard);
  const workspaceCreateState = signal(createState);
  const facade = {
    dashboard: dashboardState.asReadonly(),
    workspaceCreate: workspaceCreateState.asReadonly(),
    resetWorkspaceCreatePresentation: vi.fn(),
    createWorkspace: vi.fn().mockResolvedValue(false),
    retryWorkspaceActivation: vi.fn().mockResolvedValue(false),
  };

  await TestBed.configureTestingModule({
    imports: [WorkspaceDashboardPageComponent],
    providers: [provideRouter([]), { provide: WorkspacesFacade, useValue: facade }],
  }).compileComponents();

  const fixture = TestBed.createComponent(WorkspaceDashboardPageComponent);
  fixture.detectChanges();
  return { facade, fixture };
};

const workspaceCard = (
  fixture: ComponentFixture<WorkspaceDashboardPageComponent>,
  workspaceName: string,
): HTMLElement => {
  const cards = Array.from(
    (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
      '[data-testid="workspace-card"]',
    ),
  );
  const card = cards.find((candidate) => candidate.textContent?.includes(workspaceName));
  if (!card) {
    throw new Error(`Workspace card '${workspaceName}' was not rendered.`);
  }
  return card;
};

describe('WorkspaceDashboardPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('hides create action when capability is absent', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.default,
      pageCapabilities: [],
    });

    expect(
      (fixture.nativeElement as HTMLElement).querySelector(
        '[data-testid="create-workspace-action"]',
      ),
    ).toBeNull();
  });

  it('shows create action only when the page capability is present', async () => {
    const fixture = await renderDashboard(WORKSPACE_DASHBOARD_SCENARIOS.default);

    const action = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-testid="create-workspace-action"]',
    );
    expect(action).not.toBeNull();
    expect(action?.disabled).toBe(false);

    action?.click();
    fixture.detectChanges();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[data-testid="workspace-create-form"]'),
    ).not.toBeNull();
  });

  it('renders one primary research action with separate full-Project and file actions for an authorized Workspace', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.default,
      workspaces: [OWNER_WORKSPACE],
    });

    const card = workspaceCard(fixture, OWNER_WORKSPACE.displayName);
    expect(card.querySelector('[role="group"][aria-label="作成"]')).not.toBeNull();
    const primaryActions = card.querySelectorAll('[data-testid="start-research-action"]');
    expect(primaryActions).toHaveLength(1);
    expect(primaryActions[0]?.textContent?.trim()).toBe('新しいリサーチ');
    expect(primaryActions[0]?.getAttribute('href')).toBe(
      '/workspaces/sample-workspace-owner/research/new',
    );

    const fullCreate = card.querySelector<HTMLAnchorElement>('[data-testid="open-project-create-action"]');
    expect(fullCreate?.textContent?.trim()).toBe('Set up Project');
    expect(fullCreate?.getAttribute('href')).toBe(
      '/workspaces/sample-workspace-owner/projects?create=1',
    );

    const addFiles = card.querySelector<HTMLAnchorElement>('[data-testid="add-files-action"]');
    expect(addFiles?.textContent?.trim()).toBe('ファイルを追加');
    expect(addFiles?.getAttribute('href')).toBe('/workspaces/sample-workspace-owner/files#upload');
  });

  it('does not infer create actions from Workspace read access', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.default,
      workspaces: [{ ...DEFAULT_WORKSPACES[2], capabilities: ['openWorkspace'] }],
    });

    const card = workspaceCard(fixture, DEFAULT_WORKSPACES[2].displayName);
    expect(card.querySelector('[data-testid="start-research-action"]')).toBeNull();
    expect(card.querySelector('[data-testid="open-project-create-action"]')).toBeNull();
    expect(card.querySelector('[data-testid="add-files-action"]')).toBeNull();
    expect(card.querySelector('[data-testid="open-members-action"]')).toBeNull();
    expect(card.querySelector('[data-testid="open-projects-action"]')).toBeNull();
  });

  it('keeps lower-frequency navigation distinct from create actions', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.default,
      workspaces: [OWNER_WORKSPACE],
    });

    const card = workspaceCard(fixture, OWNER_WORKSPACE.displayName);
    expect(card.querySelector('[data-testid="open-projects-action"]')?.textContent?.trim()).toBe(
      'プロジェクト',
    );
    expect(card.querySelector('[data-testid="open-members-action"]')?.textContent?.trim()).toBe(
      'メンバー',
    );
    expect(card.querySelectorAll('[data-testid="start-research-action"]')).toHaveLength(1);
  });

  it('uses per-card action groups instead of duplicate navigation landmarks', async () => {
    const duplicateName = '同名Workspace';
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.default,
      workspaces: [
        { ...OWNER_WORKSPACE, id: 'duplicate-workspace-a', displayName: duplicateName },
        { ...OWNER_WORKSPACE, id: 'duplicate-workspace-b', displayName: duplicateName },
      ],
    });
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelectorAll('nav.workspace-actions__navigation')).toHaveLength(0);
    expect(
      root.querySelectorAll(
        '.workspace-actions__navigation[role="group"][aria-label="Workspace内を移動"]',
      ),
    ).toHaveLength(2);
  });

  it('uses one capability-gated create action inside the zero-Workspace state', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.noWorkspaceAccess,
      pageCapabilities: ['createWorkspace'],
    });
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-testid="create-workspace-action"]')).toBeNull();
    const emptyAction = root.querySelector<HTMLButtonElement>(
      '[data-testid="workspace-empty-create-action"]',
    );
    expect(emptyAction).not.toBeNull();
    emptyAction?.click();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="workspace-create-form"]')).not.toBeNull();
  });

  it.each([
    ['dashboard error', WORKSPACE_DASHBOARD_SCENARIOS.error],
    ['permission denied', WORKSPACE_DASHBOARD_SCENARIOS.permissionDenied],
    [
      'no Workspace access',
      {
        ...WORKSPACE_DASHBOARD_SCENARIOS.noWorkspaceAccess,
        pageCapabilities: ['createWorkspace'] as const,
      },
    ],
  ])('keeps committed Workspace activation resumable during %s', async (_label, dashboard) => {
    const { fixture } = await renderDashboardWithCreateState(dashboard, {
      status: 'committedPendingActivation',
      fieldErrors: [],
      createdWorkspaceId: 'workspace-created',
      requestId: 'request-created',
    });
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-testid="create-workspace-action"]')).toBeNull();
    expect(root.querySelector('[data-testid="workspace-empty-create-action"]')).toBeNull();
    const resume = root.querySelector<HTMLButtonElement>(
      '[data-testid="resume-workspace-activation-action"]',
    );
    expect(resume).not.toBeNull();

    resume?.click();
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="workspace-create-pending"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="workspace-create-form"]')).toBeNull();
  });

  it('closes and reopens committed activation without another create submission', async () => {
    const { facade, fixture } = await renderDashboardWithCreateState(
      WORKSPACE_DASHBOARD_SCENARIOS.error,
      {
        status: 'committedPendingActivation',
        fieldErrors: [],
        createdWorkspaceId: 'workspace-created',
        requestId: 'request-created',
      },
    );
    const root = fixture.nativeElement as HTMLElement;
    const createHandler = vi.spyOn(fixture.componentInstance, 'createWorkspace');

    root
      .querySelector<HTMLButtonElement>('[data-testid="resume-workspace-activation-action"]')
      ?.click();
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('.coglatas-dialog__actions button')?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(root.querySelector('[role="dialog"]')).toBeNull();
    expect(facade.resetWorkspaceCreatePresentation).not.toHaveBeenCalled();

    root
      .querySelector<HTMLButtonElement>('[data-testid="resume-workspace-activation-action"]')
      ?.click();
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="workspace-create-pending"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="workspace-create-form"]')).toBeNull();

    root.querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')?.click();
    await fixture.whenStable();

    expect(facade.retryWorkspaceActivation).toHaveBeenCalledOnce();
    expect(createHandler).not.toHaveBeenCalled();
    expect(facade.createWorkspace).not.toHaveBeenCalled();
  });

  it('submits the accessible dialog through the facade and announces activation', async () => {
    const fixture = await renderDashboard(WORKSPACE_DASHBOARD_SCENARIOS.default);
    const facade = TestBed.inject(WorkspacesFacade);
    const create = vi.spyOn(facade, 'createWorkspace').mockResolvedValue(true);
    const root = fixture.nativeElement as HTMLElement;
    fixture.componentInstance.updateSearch('does not match the new Workspace');
    root.querySelector<HTMLButtonElement>('[data-testid="create-workspace-action"]')?.click();
    fixture.detectChanges();

    const name = root.querySelector<HTMLInputElement>('[data-testid="workspace-create-name"]');
    name!.value = 'U-22 Research';
    name!.dispatchEvent(new Event('input'));
    root.querySelector<HTMLSelectElement>('[data-testid="workspace-create-icon"]')!.value = '🚀';
    root.querySelector<HTMLSelectElement>('[data-testid="workspace-create-icon"]')!.dispatchEvent(
      new Event('change'),
    );
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(create).toHaveBeenCalledWith({ name: 'U-22 Research', description: '', icon: '🚀' });
    expect(root.querySelector('[role="dialog"]')).toBeNull();
    expect(fixture.componentInstance.searchValue()).toBe('');
    expect(root.querySelector('[data-testid="workspace-created-announcement"]')?.textContent).toContain(
      'U-22 Research',
    );
  });

  it('cancels without sending a create request and returns focus to the opener', async () => {
    const fixture = await renderDashboard(WORKSPACE_DASHBOARD_SCENARIOS.default);
    const facade = TestBed.inject(WorkspacesFacade);
    const create = vi.spyOn(facade, 'createWorkspace');
    const root = fixture.nativeElement as HTMLElement;
    const opener = root.querySelector<HTMLButtonElement>('[data-testid="create-workspace-action"]')!;
    opener.focus();
    opener.click();
    fixture.detectChanges();

    root.querySelector<HTMLButtonElement>('.coglatas-dialog__actions button')?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(create).not.toHaveBeenCalled();
    expect(root.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(opener);
  });

  it('filters only provided mock workspace rows with page-local search', async () => {
    const fixture = await renderDashboard(WORKSPACE_DASHBOARD_SCENARIOS.default);

    fixture.componentInstance.updateSearch('教材準備');
    fixture.detectChanges();

    const text = textContent(fixture);
    expect(text).toContain('教材準備ワークスペースC');
    expect(text).not.toContain('サンプル共同ワークスペースA');

    fixture.componentInstance.updateSearch('未提供の外部ワークスペース');
    fixture.detectChanges();
    expect(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="workspace-card"]')
        .length,
    ).toBe(0);
  });

  it('does not render email addresses', async () => {
    const fixture = await renderDashboard(WORKSPACE_DASHBOARD_SCENARIOS.default);

    expect(textContent(fixture)).not.toMatch(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i);
  });

  it('does not render DM preview text', async () => {
    const fixture = await renderDashboard(WORKSPACE_DASHBOARD_SCENARIOS.default);

    expect(textContent(fixture)).not.toContain('DM');
    expect(textContent(fixture)).not.toContain('本文プレビュー');
  });

  it('does not render an action denied by the corresponding backend capability', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.default,
      workspaces: [READ_ONLY_WORKSPACE],
    });

    const card = workspaceCard(fixture, READ_ONLY_WORKSPACE.displayName);
    expect(card.querySelector('[data-testid="start-research-action"]')).toBeNull();
    expect(card.querySelector('[data-testid="open-project-create-action"]')).toBeNull();
    expect(card.querySelector('[data-testid="add-files-action"]')).toBeNull();
    expect(card.querySelector('[data-testid="open-members-action"]')).toBeNull();
    expect(card.querySelector('[data-testid="open-projects-action"]')).toBeNull();
  });

  it('renders zero and non-zero authoritative counts as text', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.default,
      workspaces: [OWNER_WORKSPACE, READ_ONLY_WORKSPACE],
    });

    const ownerCard = workspaceCard(fixture, OWNER_WORKSPACE.displayName);
    expect(
      ownerCard.querySelector('[data-testid="unread-announcement-count"]')?.textContent?.trim(),
    ).toBe('3');
    expect(
      ownerCard.querySelector('[data-testid="unread-conversation-count"]')?.textContent?.trim(),
    ).toBe('2');
    expect(
      ownerCard.querySelector('[data-testid="active-project-count"]')?.textContent?.trim(),
    ).toBe('5');

    const readOnlyCard = workspaceCard(fixture, READ_ONLY_WORKSPACE.displayName);
    expect(
      readOnlyCard.querySelector('[data-testid="unread-announcement-count"]')?.textContent?.trim(),
    ).toBe('0');
    expect(
      readOnlyCard.querySelector('[data-testid="unread-conversation-count"]')?.textContent?.trim(),
    ).toBe('0');
    expect(
      readOnlyCard.querySelector('[data-testid="active-project-count"]')?.textContent?.trim(),
    ).toBe('0');
  });

  it('keeps an unavailable count visibly distinct from numeric zero', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.default,
      workspaces: [
        READ_ONLY_WORKSPACE,
        {
          ...READ_ONLY_WORKSPACE,
          id: 'unavailable-workspace',
          displayName: '集計取得不可ワークスペース',
          unreadAnnouncementCount: null,
          availability: {
            ...READ_ONLY_WORKSPACE.availability,
            unreadAnnouncements: false,
          },
        },
      ],
    });

    const zeroCard = workspaceCard(fixture, READ_ONLY_WORKSPACE.displayName);
    expect(
      zeroCard.querySelector('[data-testid="unread-announcement-count"]')?.textContent?.trim(),
    ).toBe('0');

    const unavailableCard = workspaceCard(fixture, '集計取得不可ワークスペース');
    expect(
      unavailableCard
        .querySelector('[data-testid="unread-announcement-count"]')
        ?.textContent?.trim(),
    ).toBe('未提供');
  });

  it('presents SystemAdmin access without inventing a Workspace membership role or Project-create authority', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.systemAdmin,
      workspaces: [SYSTEM_ADMIN_WORKSPACE],
    });

    const card = workspaceCard(fixture, SYSTEM_ADMIN_WORKSPACE.displayName);
    const role = card.querySelector('[data-testid="workspace-role"]')?.textContent?.trim();
    expect(role).toBe('システム管理者アクセス');
    expect(role).not.toBe('メンバー');
    expect(card.querySelector('[data-testid="start-research-action"]')).toBeNull();
    expect(card.querySelector('[data-testid="add-files-action"]')).not.toBeNull();
  });

  it('does not show the obsolete API-unimplemented summary message for a complete projection', async () => {
    const fixture = await renderDashboard(WORKSPACE_DASHBOARD_SCENARIOS.default);

    expect(textContent(fixture)).not.toContain('一部の集計情報はまだAPI未実装です。');
    expect(textContent(fixture)).not.toContain('API未実装');
  });

  it.each([
    ['error', WORKSPACE_DASHBOARD_SCENARIOS.error],
    ['permission denied', WORKSPACE_DASHBOARD_SCENARIOS.permissionDenied],
    ['no Workspace access', WORKSPACE_DASHBOARD_SCENARIOS.noWorkspaceAccess],
  ])('keeps the %s state free of stale Workspace cards', async (_label, dashboard) => {
    const fixture = await renderDashboard(dashboard);

    expect(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="workspace-card"]'),
    ).toHaveLength(0);
    expect(
      (fixture.nativeElement as HTMLElement).querySelector(
        '[data-testid="create-workspace-action"]',
      ),
    ).toBeNull();
    expect(textContent(fixture)).toContain(dashboard.message ?? '');
  });

  it('wraps long workspace names safely', async () => {
    const fixture = await renderDashboard({
      ...WORKSPACE_DASHBOARD_SCENARIOS.longWorkspaceNames,
      workspaces: [LONG_NAME_WORKSPACE],
    });

    const name = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="workspace-name"]',
    );
    expect(name?.textContent).toContain('非常に長い表示名');
    expect(getComputedStyle(name as HTMLElement).overflowWrap).toBe('anywhere');
  });
});
