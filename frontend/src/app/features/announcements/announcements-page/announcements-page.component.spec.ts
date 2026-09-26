import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, ParamMap, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

import { COGLATAS_ANNOUNCEMENTS_PAGE_MOCK } from '../announcements.facade';
import {
  ANNOUNCEMENT_PAGE_SCENARIOS,
  DEFAULT_ANNOUNCEMENTS,
  HIDDEN_ANNOUNCEMENT_BODY,
  HIDDEN_ANNOUNCEMENT_TITLE
} from '../announcements.mock';
import { AnnouncementsPageViewModel } from '../announcements.types';
import { AnnouncementsPageComponent } from './announcements-page.component';

const renderAnnouncementsPage = async (
  page: AnnouncementsPageViewModel,
  announcementId: string | null = null,
): Promise<ComponentFixture<AnnouncementsPageComponent>> => {
  routeParams = new BehaviorSubject<ParamMap>(
    convertToParamMap(announcementId ? { announcementId } : {}),
  );
  routerNavigate = vi.fn(async () => true);
  await TestBed.configureTestingModule({
    imports: [AnnouncementsPageComponent],
    providers: [
      { provide: COGLATAS_ANNOUNCEMENTS_PAGE_MOCK, useValue: page },
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: { paramMap: routeParams.value },
          paramMap: routeParams,
        } as unknown as Partial<ActivatedRoute>,
      },
      { provide: Router, useValue: { navigate: routerNavigate } },
    ]
  }).compileComponents();

  const fixture = TestBed.createComponent(AnnouncementsPageComponent);
  fixture.detectChanges();
  return fixture;
};

let routeParams: BehaviorSubject<ParamMap>;
let routerNavigate: ReturnType<typeof vi.fn>;
const originalMatchMediaDescriptor = Object.getOwnPropertyDescriptor(window, 'matchMedia');

const textContent = (fixture: ComponentFixture<AnnouncementsPageComponent>): string =>
  (fixture.nativeElement as HTMLElement).textContent ?? '';

const setMobileMatchMedia = (): void => {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: (query: string) =>
      ({
        matches: query === '(max-width: 860px)',
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(() => true),
      }) as MediaQueryList,
  });
};

describe('AnnouncementsPageComponent', () => {
  afterEach(() => {
    document.getElementById('app-shell-main-content')?.remove();
    if (originalMatchMediaDescriptor) {
      Object.defineProperty(window, 'matchMedia', originalMatchMediaDescriptor);
    } else {
      Reflect.deleteProperty(window, 'matchMedia');
    }
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('hides create button without capability', async () => {
    const fixture = await renderAnnouncementsPage(ANNOUNCEMENT_PAGE_SCENARIOS.noCreatePermission);

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="create-announcement-action"]')).toBeNull();
  });

  it('does not show hidden announcement title or body in denied state', async () => {
    const fixture = await renderAnnouncementsPage({
      ...ANNOUNCEMENT_PAGE_SCENARIOS.permissionDenied,
      announcements: [
        {
          ...DEFAULT_ANNOUNCEMENTS[0],
          id: 'hidden-announcement',
          title: HIDDEN_ANNOUNCEMENT_TITLE,
          body: HIDDEN_ANNOUNCEMENT_BODY,
          capabilities: []
        }
      ]
    });

    const text = textContent(fixture);
    expect(text).toContain('このお知らせを表示する権限がありません。');
    expect(text).not.toContain(HIDDEN_ANNOUNCEMENT_TITLE);
    expect(text).not.toContain(HIDDEN_ANNOUNCEMENT_BODY);
  });

  it('renders body as text instead of unsafe html', async () => {
    const fixture = await renderAnnouncementsPage(ANNOUNCEMENT_PAGE_SCENARIOS.unsafeBody);
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-testid="announcement-body-text"]')?.textContent).toContain('<img src=x');
    expect(root.querySelector('[data-testid="announcement-body-text"] img')).toBeNull();
  });

  it('filters only already provided authorized announcements with page-local search', async () => {
    const fixture = await renderAnnouncementsPage({
      ...ANNOUNCEMENT_PAGE_SCENARIOS.default,
      announcements: [
        DEFAULT_ANNOUNCEMENTS[0],
        {
          ...DEFAULT_ANNOUNCEMENTS[1],
          title: '検索で表示されるお知らせ'
        },
        {
          ...DEFAULT_ANNOUNCEMENTS[2],
          title: '権限なしの検索対象',
          body: 'この本文は検索しても表示されません。',
          capabilities: []
        }
      ]
    });

    fixture.componentInstance.updateSearch('検索');
    fixture.detectChanges();

    const text = textContent(fixture);
    expect(text).toContain('検索で表示されるお知らせ');
    expect(text).not.toContain('権限なしの検索対象');
    expect(text).not.toContain('この本文は検索しても表示されません。');
  });

  it('renders the accessible priority label from frontend semantics', async () => {
    const fixture = await renderAnnouncementsPage(ANNOUNCEMENT_PAGE_SCENARIOS.default);
    const priority = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="announcement-priority-label"]'
    );

    expect(priority?.textContent).toContain('IMPORTANT');
  });

  it('wraps long title and body safely', async () => {
    const fixture = await renderAnnouncementsPage(ANNOUNCEMENT_PAGE_SCENARIOS.longBody);
    const title = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[data-testid="announcement-detail-title"]');
    const body = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[data-testid="announcement-body-text"]');

    expect(title?.textContent).toContain('とても長いタイトル');
    expect(body?.textContent?.length).toBeGreaterThan(300);
    expect(getComputedStyle(title as HTMLElement).overflowWrap).toBe('anywhere');
    expect(getComputedStyle(body as HTMLElement).overflowWrap).toBe('anywhere');
  });

  it('does not expose hidden actions in mobile layout', async () => {
    const fixture = await renderAnnouncementsPage(ANNOUNCEMENT_PAGE_SCENARIOS.noCreatePermission);

    (fixture.nativeElement as HTMLElement).style.width = '320px';
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="create-announcement-action"]')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="edit-announcement-action"]')).toBeNull();
  });

  it('uses the mobile list/detail route hierarchy, resets AppShell scroll, and restores the origin row after Back', async () => {
    setMobileMatchMedia();
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback: FrameRequestCallback) => {
      callback(0);
      return 1;
    });
    const appScrollHost = document.createElement('main');
    appScrollHost.id = 'app-shell-main-content';
    appScrollHost.scrollTop = 384;
    Object.defineProperty(appScrollHost, 'scrollTo', {
      configurable: true,
      value: vi.fn((options: ScrollToOptions) => {
        appScrollHost.scrollTop = Number(options.top ?? 0);
      }),
    });
    document.body.append(appScrollHost);

    const fixture = await renderAnnouncementsPage(ANNOUNCEMENT_PAGE_SCENARIOS.default);
    const component = fixture.componentInstance;
    const announcementId = DEFAULT_ANNOUNCEMENTS[0].id;

    component.selectAnnouncement(announcementId);
    expect(routerNavigate).toHaveBeenCalledWith(['/announcements', announcementId]);

    routeParams.next(convertToParamMap({ announcementId }));
    fixture.detectChanges();
    await Promise.resolve();

    expect(appScrollHost.scrollTop).toBe(0);
    expect(document.activeElement).toBe(
      (fixture.nativeElement as HTMLElement).querySelector('[data-testid="announcement-detail-title"]'),
    );

    const back = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-testid="announcement-mobile-back"]',
    );
    back?.click();
    expect(routerNavigate).toHaveBeenLastCalledWith(['/announcements'], { replaceUrl: true });

    routeParams.next(convertToParamMap({}));
    fixture.detectChanges();
    await Promise.resolve();

    const returnedRow = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      `[data-announcement-id="${announcementId}"]`,
    );
    expect(appScrollHost.scrollTop).toBe(384);
    expect(document.activeElement).toBe(returnedRow);
  });

  it('uses the list heading as the safe Back focus fallback for an unavailable direct route', async () => {
    setMobileMatchMedia();
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback: FrameRequestCallback) => {
      callback(0);
      return 1;
    });

    const fixture = await renderAnnouncementsPage(
      ANNOUNCEMENT_PAGE_SCENARIOS.default,
      'unavailable-announcement',
    );
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="announcement-detail-empty"]')).not.toBeNull();

    root.querySelector<HTMLButtonElement>('[data-testid="announcement-mobile-back"]')?.click();
    expect(routerNavigate).toHaveBeenLastCalledWith(['/announcements'], { replaceUrl: true });
    routeParams.next(convertToParamMap({}));
    fixture.detectChanges();
    await Promise.resolve();

    expect(document.activeElement).toBe(root.querySelector('[data-testid="announcement-list-heading"]'));
  });

  it('focuses the valid direct-detail title after the first mobile route parameter emission', async () => {
    setMobileMatchMedia();
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback: FrameRequestCallback) => {
      callback(0);
      return 1;
    });

    const fixture = await renderAnnouncementsPage(
      ANNOUNCEMENT_PAGE_SCENARIOS.default,
      DEFAULT_ANNOUNCEMENTS[0].id,
    );
    fixture.detectChanges();
    await Promise.resolve();

    const root = fixture.nativeElement as HTMLElement;
    const title = root.querySelector('[data-testid="announcement-detail-title"]');
    expect(root.querySelector('.announcements-page__content--detail-route')).not.toBeNull();
    expect(title?.textContent).toContain(DEFAULT_ANNOUNCEMENTS[0].title);
    expect(document.activeElement).toBe(title);
  });

  it('shows the reactive editor when create is allowed', async () => {
    const fixture = await renderAnnouncementsPage(ANNOUNCEMENT_PAGE_SCENARIOS.default);

    fixture.componentInstance.showCreateEditor();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="announcement-editor"]')).not.toBeNull();
  });

  it('shows the create editor even when the authorized announcement list is empty', async () => {
    const fixture = await renderAnnouncementsPage(ANNOUNCEMENT_PAGE_SCENARIOS.empty);

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="announcements-empty"]')).not.toBeNull();
    fixture.componentInstance.showCreateEditor();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="announcement-editor"]')).not.toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="announcements-empty"]')).toBeNull();
  });
});
