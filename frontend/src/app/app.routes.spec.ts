import { routes } from './app.routes';

describe('application Project Task routes', () => {
  it('keeps dedicated DM and Teams directories inside the authenticated shell', () => {
    const shell = routes.find((route) => route.path === '');
    expect(shell?.canActivate?.length).toBeGreaterThan(0);
    for (const directory of ['dm', 'teams']) {
      const route = shell?.children?.find((child) => child.path === directory);
      expect(route?.loadComponent).toBeDefined();
      expect(route?.data?.['conversationDirectory']).toBe(directory);
    }
    expect(shell?.children?.some((route) => route.path === 'dm/:conversationId')).toBe(true);
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
