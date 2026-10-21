import { routes } from './app.routes';

describe('application Project Task routes', () => {
  it('separates Workspace channel directory, joined-channel activity, and DM inside the authenticated shell', () => {
    const shell = routes.find((route) => route.path === '');
    expect(shell?.canActivate?.length).toBeGreaterThan(0);
    const children = shell?.children ?? [];

    const dm = children.find((child) => child.path === 'dm');
    expect(dm?.data?.['conversationDirectory']).toBe('dm');
    expect(dm?.loadComponent).toBeDefined();

    const teams = children.find((child) => child.path === 'teams');
    expect(teams?.loadComponent).toBeDefined();
    // The selected-Workspace channel directory is not an inbox mode.
    expect(teams?.data?.['conversationDirectory']).toBeUndefined();
    expect(children.findIndex((child) => child.path === 'teams/activity'))
      .toBeLessThan(children.findIndex((child) => child.path === 'teams'));

    const activity = children.find((child) => child.path === 'teams/activity');
    expect(activity?.data?.['conversationDirectory']).toBe('teams');
    expect(activity?.loadComponent).toBeDefined();

    expect(children.some((route) => route.path === 'dm/:conversationId')).toBe(true);
    expect(children.some((route) => route.path === 'workspaces/:workspaceId/channels/:conversationId')).toBe(true);
  });

  it('places the New Task route before generic Task detail so "new" is never treated as an ID', () => {
    const shell = routes.find((route) => route.path === '');
    const children = shell?.children ?? [];
    const createIndex = children.findIndex((route) => route.path === 'projects/:projectId/tasks/new');
    const detailIndex = children.findIndex((route) => route.path === 'projects/:projectId/tasks/:taskId');

    expect(createIndex).toBeGreaterThanOrEqual(0);
    expect(detailIndex).toBeGreaterThan(createIndex);
    expect(children[createIndex]?.loadComponent).toBeDefined();
  });
});
