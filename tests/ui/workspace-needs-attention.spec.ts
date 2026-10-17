import { expect, test } from '@playwright/test';
import { expectNoAccessibilityViolations } from './a11y';

const projectId = '22222222-2222-4222-8222-222222222222';
const reviewTaskId = '11111111-1111-4111-8111-111111111111';
const failedTaskId = '33333333-3333-4333-8333-333333333333';

test.describe('Workspace Needs attention', () => {
  test('keeps actionable items reachable, keyboard navigable, and contained at 320px', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    await page.route('**/api/workspaces', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json; charset=utf-8',
        body: JSON.stringify([
          {
            id: 'static-workspace-1',
            name: 'Static Workspace',
            status: 'Active',
            createdAt: '2026-09-01T08:00:00Z',
            updatedAt: '2026-09-01T09:00:00Z',
            currentUserRole: 'Member',
            accessSource: 'WorkspaceMembership',
            canOpenWorkspace: true,
            canOpenMembers: true,
            canOpenProjects: true,
            canOpenProjectCreate: false,
            canCreateProject: false,
            canAddFiles: true,
            unreadAnnouncementCount: 0,
            unreadConversationCount: 0,
            inProgressProjectCount: 1,
            runningProjectCount: 1,
            needsReviewProjectCount: 0,
            needsAttentionCount: 2,
            needsAttentionItems: [
              {
                id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
                kind: 'ReviewRequired',
                targetRoute: `/projects/${projectId}/tasks/${reviewTaskId}`,
                occurredAt: '2026-09-01T08:30:00Z',
              },
              {
                id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
                kind: 'ResearchFailed',
                targetRoute: `/projects/${projectId}/tasks/${failedTaskId}`,
                occurredAt: '2026-09-01T08:45:00Z',
              },
            ],
            hasExternalShares: false,
            externalShareCount: null,
            canInspectSharing: false,
            canManageSharing: false,
            memberPreview: [],
          },
        ]),
      });
    });

    await page.goto('/app/workspaces');

    const panel = page.getByTestId('workspace-needs-attention');
    await expect(panel).toBeVisible();
    await expect(page.getByTestId('workspace-needs-attention-count')).toHaveText(/2\s+unresolved/u);
    await expect(page.getByTestId('workspace-needs-attention-link')).toHaveCount(2);
    await expect(panel).toContainText('There are Tasks that need review');
    await expect(panel).toContainText('Research execution failed');
    await expect(page.getByTestId('workspace-activity-feed')).toHaveCount(0);

    const box = await panel.boundingBox();
    expect(box).not.toBeNull();
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(320);
    const hasHorizontalOverflow = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
    );
    expect(hasHorizontalOverflow).toBe(false);
    await expectNoAccessibilityViolations(page);

    const firstTarget = page.getByTestId('workspace-needs-attention-link').first();
    await firstTarget.focus();
    await expect(firstTarget).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(new RegExp(`/app/projects/${projectId}/tasks/${reviewTaskId}$`, 'u'));
  });
});
