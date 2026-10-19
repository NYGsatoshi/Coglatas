import { expect, type Locator, type Page, test } from '@playwright/test';

type HorizontalRegionEvidence = Readonly<{
  className: string;
  clientWidth: number;
  scrollWidth: number;
}>;

type MobileProfile = Readonly<{
  browserName: 'chromium' | 'webkit';
  viewport: Readonly<{ height: number; width: number }>;
}>;

type ScrollEvidence = Readonly<{
  after: number;
  before: number;
  clientWidth: number;
  scrollWidth: number;
}>;

const assertScrollEvidence = function assertScrollEvidence(evidence: ScrollEvidence | null): void {
    if (evidence === null) {
      throw new Error('Expected the allowlisted data grid to expose one horizontal scroll region.');
    }
    expect(evidence.after).toBeGreaterThan(evidence.before);
    expect(evidence.scrollWidth).toBeGreaterThan(evidence.clientWidth);
  },
  expectMinimumTouchTarget = async function expectMinimumTouchTarget(locator: Readonly<Locator>): Promise<void> {
    const bounds = await locator.boundingBox();
    if (bounds === null) {
      throw new Error('Touch target must have a rendered bounding box.');
    }
    expect(bounds.width).toBeGreaterThanOrEqual(44);
    expect(bounds.height).toBeGreaterThanOrEqual(44);
  },
  expectProjectProfile = function expectProjectProfile(
    browserName: string,
    page: Readonly<Page>,
    projectName: string,
  ): void {
    const profile = requireMobileProfile(projectName);
    expect(browserName).toBe(profile.browserName);
    expect(page.viewportSize()).toEqual(profile.viewport);
  },
  findHorizontalScrollRegions = async function findHorizontalScrollRegions(
    root: Readonly<Locator>,
  ): Promise<readonly HorizontalRegionEvidence[]> {
    return root.evaluate((element) => {
      const candidates = [element as HTMLElement, ...element.querySelectorAll<HTMLElement>('*')];
      return candidates
        .filter((candidate) => {
          const { overflowX } = getComputedStyle(candidate);
          return (
            candidate.clientWidth > 0 &&
            candidate.scrollWidth > candidate.clientWidth + 1 &&
            (overflowX === 'auto' || overflowX === 'scroll')
          );
        })
        .map(({ className, clientWidth, scrollWidth }) => ({ className, clientWidth, scrollWidth }));
    });
  },
  intentionalHorizontalScrollRegions = ['[data-testid="app-data-grid"]'] as const,
  mobileProfileContract = new Map<string, MobileProfile>([
    [
      'chromium-mobile',
      {
        browserName: 'chromium',
        viewport: { height: 727, width: 393 },
      },
    ],
    [
      'webkit-mobile',
      {
        browserName: 'webkit',
        viewport: { height: 664, width: 390 },
      },
    ],
    [
      'narrow-320',
      {
        browserName: 'chromium',
        viewport: { height: 800, width: 320 },
      },
    ],
  ]),
  requireMobileProfile = function requireMobileProfile(projectName: string): MobileProfile {
    const profile = mobileProfileContract.get(projectName);
    if (profile === undefined) {
      throw new Error(`Unexpected COMPAT-03 Playwright project: ${projectName}`);
    }
    return profile;
  },
  verifyDesktopGridScroll = async function verifyDesktopGridScroll(page: Readonly<Page>): Promise<void> {
    const allowlistedGrid = page.getByTestId('app-data-grid');
    await expect(allowlistedGrid).toBeVisible();
    await expect
      .poll(async () => (await findHorizontalScrollRegions(allowlistedGrid)).length)
      .toBeGreaterThan(0);
    assertScrollEvidence(await scrollFirstHorizontalRegion(allowlistedGrid));
  },
  verifyMobileMembersLayout = async function verifyMobileMembersLayout(page: Readonly<Page>): Promise<void> {
    await expect(page.getByTestId('workspace-members-page')).toBeVisible();
    await expect(page.locator('.workspace-members__desktop-grid')).toBeHidden();
    await expect(page.getByTestId('workspace-members-mobile-list')).toBeVisible();
  },
  // eslint-disable-next-line @typescript-eslint/prefer-readonly-parameter-types -- Playwright Page is an intentionally mutable external test handle.
  verifyMobileNavigation = async function verifyMobileNavigation(page: Page): Promise<void> {
    const mobileNavigation = page.getByTestId('mobile-navigation'),
      navigationToggle = page.getByTestId('mobile-nav-toggle'),
      workspaceLink = mobileNavigation.locator('a[href="/app/workspaces"]').first();
    await expectMinimumTouchTarget(navigationToggle);
    await navigationToggle.tap();
    await expect(mobileNavigation).toHaveAttribute('aria-hidden', 'false');
    await expect(navigationToggle).toHaveAttribute('aria-expanded', 'true');
    await expect(workspaceLink).toBeVisible();
    await workspaceLink.tap();
    await expect(mobileNavigation).toHaveAttribute('aria-hidden', 'true');
    await expect(navigationToggle).toHaveAttribute('aria-expanded', 'false');
  },
  // eslint-disable-next-line @typescript-eslint/prefer-readonly-parameter-types -- Playwright Page is an intentionally mutable external test handle.
  verifyRightPanel = async function verifyRightPanel(page: Page): Promise<void> {
    const panel = page.getByTestId('right-panel'),
      panelClose = page.getByTestId('right-panel-close'),
      panelToggle = page.getByTestId('right-panel-toggle');
    const utilitiesToggle = page.getByTestId('mobile-utilities-toggle');
    await expectMinimumTouchTarget(utilitiesToggle);
    await utilitiesToggle.tap();
    await expect(utilitiesToggle).toHaveAttribute('aria-expanded', 'true');
    await expect(panelToggle).toBeVisible();
    await expectMinimumTouchTarget(panelToggle);
    await panelToggle.tap();
    await expect(panel).toBeVisible();
    await expect(panel).toHaveAttribute('role', 'dialog');
    await expect(panelToggle).toHaveAttribute('aria-expanded', 'true');
    await expectMinimumTouchTarget(panelClose);
    await panelClose.tap();
    await expect(panel).toBeHidden();
    await expect(panelToggle).toHaveAttribute('aria-expanded', 'false');
  };

test.describe('COMPAT-03 mobile compatibility', () => {
  test('uses touch navigation and overlay controls without hover-only behavior', async ({
    browserName,
    page,
  }, testInfo) => {
    expectProjectProfile(browserName, page, testInfo.project.name);
    expect(testInfo.project.use.hasTouch).toBe(true);
    await page.goto('/app/workspaces');
    await expect(page.getByTestId('app-shell')).toBeVisible();
    await expect(page.getByTestId('mobile-header')).toBeVisible();
    await verifyMobileNavigation(page);
    await verifyRightPanel(page);
    await expectNoDocumentHorizontalOverflow(page);
  });

  test('keeps page overflow closed while an allowlisted data grid owns horizontal scrolling', async ({
    browserName,
    page,
  }, testInfo) => {
    expectProjectProfile(browserName, page, testInfo.project.name);
    await installWorkspaceMembersApi(page);
    await page.goto('/app/workspaces/static-workspace-1/members');
    await verifyMobileMembersLayout(page);
    await expectOnlyAllowlistedHorizontalScroll(page, []);
    await expectNoDocumentHorizontalOverflow(page);
    await page.setViewportSize({ height: 900, width: 900 });
    await verifyDesktopGridScroll(page);
    await expectOnlyAllowlistedHorizontalScroll(page, intentionalHorizontalScrollRegions);
    await expectNoDocumentHorizontalOverflow(page);
  });
});

// eslint-disable-next-line @typescript-eslint/prefer-readonly-parameter-types -- Playwright Page is an intentionally mutable external test handle.
async function installWorkspaceMembersApi(page: Page): Promise<void> {
  await page.route('**/api/workspaces/static-workspace-1/members', async (route) => {
    await route.fulfill({
      body: JSON.stringify([
        {
          displayName: 'Compatibility Member',
          joinedAt: '2026-09-06T00:00:00Z',
          role: 'Member',
          status: 'Active',
          userId: '59400000-0000-4000-8000-000000000001',
        },
      ]),
      contentType: 'application/json; charset=utf-8',
      status: 200,
    });
  });
}

async function scrollFirstHorizontalRegion(root: Locator): Promise<ScrollEvidence | null> {
  return root.evaluate((element) => {
    const candidates = [element as HTMLElement, ...element.querySelectorAll<HTMLElement>('*')],
      region = candidates.find((candidate) => {
        const { overflowX } = getComputedStyle(candidate);
        return (
          candidate.clientWidth > 0 &&
          candidate.scrollWidth > candidate.clientWidth + 1 &&
          (overflowX === 'auto' || overflowX === 'scroll')
        );
      });
    if (region === undefined) {
      return null;
    }

    const {
      clientWidth,
      scrollLeft: before,
      scrollWidth,
    } = region;
    region.scrollLeft = scrollWidth;
    return {
      after: region.scrollLeft,
      before,
      clientWidth,
      scrollWidth,
    };
  });
}

async function expectOnlyAllowlistedHorizontalScroll(
  page: Page,
  allowlistedSelectors: readonly string[],
): Promise<void> {
  const unexpected = await page.evaluate(
    (selectors) =>
      Array.from(document.querySelectorAll<HTMLElement>('body *'))
        .filter((element) => {
          const { overflowX } = getComputedStyle(element),
            scrollable =
              element.clientWidth > 0 &&
              element.scrollWidth > element.clientWidth + 1 &&
              (overflowX === 'auto' || overflowX === 'scroll');
          return scrollable && !selectors.some((selector) => element.closest(selector));
        })
        .map(({ className, dataset }) => ({
          className,
          testId: dataset.testid ?? '',
        })),
    allowlistedSelectors,
  );

  expect(unexpected).toEqual([]);
}

async function expectNoDocumentHorizontalOverflow(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => ({
    bodyScrollWidth: document.body.scrollWidth,
    documentScrollWidth: document.documentElement.scrollWidth,
    viewportWidth: document.documentElement.clientWidth,
  }));

  expect(overflow.documentScrollWidth).toBeLessThanOrEqual(overflow.viewportWidth);
  expect(overflow.bodyScrollWidth).toBeLessThanOrEqual(overflow.viewportWidth);
}
