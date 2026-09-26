import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { COGLATAS_WORKSPACES_DASHBOARD_MOCK } from '../workspaces.facade';
import { OWNER_WORKSPACE } from '../workspaces.mock';
import { WorkspaceDashboardViewModel } from '../workspaces.types';
import { WorkspaceDashboardPageComponent } from './workspace-dashboard-page.component';

const projectId = '22222222-2222-4222-8222-222222222222';
const reviewTaskId = '11111111-1111-4111-8111-111111111111';
const failedTaskId = '33333333-3333-4333-8333-333333333333';

const attentionDashboard = (resolved = false): WorkspaceDashboardViewModel => ({
  status: 'ready',
  title: 'ワークスペース',
  subtitle: '参加中のワークスペース',
  pageCapabilities: [],
  workspaces: [
    {
      ...OWNER_WORKSPACE,
      needsAttentionCount: resolved ? 0 : 2,
      needsAttentionItems: resolved
        ? []
        : [
            {
              id: 'attention-review',
              kind: 'ReviewRequired',
              label: '確認が必要なTaskがあります',
              targetRoute: `/projects/${projectId}/tasks/${reviewTaskId}`,
              occurredAtLabel: '2026/09/01 17:00',
            },
            {
              id: 'attention-failed',
              kind: 'ResearchFailed',
              label: 'Researchの実行に失敗しました',
              targetRoute: `/projects/${projectId}/tasks/${failedTaskId}`,
              occurredAtLabel: '2026/09/01 17:10',
            },
          ],
    },
  ],
});

const render = async (
  dashboard: WorkspaceDashboardViewModel,
): Promise<ComponentFixture<WorkspaceDashboardPageComponent>> => {
  await TestBed.configureTestingModule({
    imports: [WorkspaceDashboardPageComponent],
    providers: [
      provideRouter([]),
      { provide: COGLATAS_WORKSPACES_DASHBOARD_MOCK, useValue: dashboard },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(WorkspaceDashboardPageComponent);
  fixture.detectChanges();
  return fixture;
};

describe('Workspace dashboard needs attention', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows only actionable summaries with an explicit unresolved count and direct Task links', async () => {
    const fixture = await render(attentionDashboard());
    const root = fixture.nativeElement as HTMLElement;
    const panel = root.querySelector<HTMLElement>('[data-testid="workspace-needs-attention"]');

    expect(panel).not.toBeNull();
    expect(
      panel?.querySelector('[data-testid="workspace-needs-attention-count"]')?.textContent?.trim(),
    ).toBe('未処理 2件');
    const links = Array.from(
      panel?.querySelectorAll<HTMLAnchorElement>('[data-testid="workspace-needs-attention-link"]') ?? [],
    );
    expect(links).toHaveLength(2);
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      `/projects/${projectId}/tasks/${reviewTaskId}`,
      `/projects/${projectId}/tasks/${failedTaskId}`,
    ]);
    expect(links[0]?.textContent).toContain('確認が必要なTaskがあります');
    expect(links[1]?.textContent).toContain('Researchの実行に失敗しました');
    expect(root.querySelector('[data-testid="workspace-activity-feed"]')).toBeNull();
    expect(panel?.textContent).not.toContain('Activity history');
  });

  it('does not retain resolved items and explains the processed lifecycle', async () => {
    const fixture = await render(attentionDashboard(true));
    const root = fixture.nativeElement as HTMLElement;
    const panel = root.querySelector<HTMLElement>('[data-testid="workspace-needs-attention"]');

    expect(
      panel?.querySelector('[data-testid="workspace-needs-attention-count"]')?.textContent?.trim(),
    ).toBe('未処理 0件');
    expect(panel?.querySelectorAll('[data-testid="workspace-needs-attention-link"]')).toHaveLength(0);
    expect(panel?.querySelector('[data-testid="workspace-needs-attention-empty"]')?.textContent).toContain(
      '現在、対応が必要な項目はありません',
    );
    expect(panel?.textContent).toContain('処理済みになると、自動的にこの一覧から外れます');
  });
});
