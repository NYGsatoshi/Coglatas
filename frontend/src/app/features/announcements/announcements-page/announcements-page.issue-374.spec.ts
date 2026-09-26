import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { COGLATAS_ANNOUNCEMENTS_PAGE_MOCK } from '../announcements.facade';
import { ANNOUNCEMENT_PAGE_SCENARIOS } from '../announcements.mock';
import { AnnouncementsPageViewModel } from '../announcements.types';
import { AnnouncementsPageComponent } from './announcements-page.component';

const renderAnnouncementsPage = async (
  page: AnnouncementsPageViewModel,
): Promise<ComponentFixture<AnnouncementsPageComponent>> => {
  await TestBed.configureTestingModule({
    imports: [AnnouncementsPageComponent],
    providers: [provideRouter([]), { provide: COGLATAS_ANNOUNCEMENTS_PAGE_MOCK, useValue: page }],
  }).compileComponents();

  const fixture = TestBed.createComponent(AnnouncementsPageComponent);
  fixture.detectChanges();
  return fixture;
};

const rootElement = (fixture: ComponentFixture<AnnouncementsPageComponent>): HTMLElement =>
  fixture.nativeElement as HTMLElement;

describe('AnnouncementsPageComponent issue #374 publication status', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows explicit text states in the list and includes scheduled time with timezone', async () => {
    const fixture = await renderAnnouncementsPage(ANNOUNCEMENT_PAGE_SCENARIOS.default);
    const listText = rootElement(fixture).querySelector('.announcement-list')?.textContent ?? '';

    expect(listText).toContain('下書き');
    expect(listText).toContain('予約済み');
    expect(listText).toContain('公開済み');
    expect(listText).toContain('更新済み');
    expect(listText).toContain('アーカイブ済み');
    expect(listText).toContain('2026年9月1日 08:00');
    expect(listText).toContain('Asia/Tokyo');
  });

  it('uses the same scheduled status vocabulary in detail and edit views', async () => {
    const fixture = await renderAnnouncementsPage({
      ...ANNOUNCEMENT_PAGE_SCENARIOS.default,
      selectedAnnouncementId: 'mock-announcement-004',
    });

    const detail = rootElement(fixture).querySelector('[data-testid="announcement-detail"]');
    expect(detail?.textContent).toContain('予約済み');
    expect(detail?.textContent).toContain('2026年9月1日 08:00');
    expect(detail?.textContent).toContain('Asia/Tokyo');

    fixture.componentInstance.showEditEditor();
    fixture.detectChanges();

    const editor = rootElement(fixture).querySelector('[data-testid="announcement-editor"]');
    expect(editor?.textContent).toContain('予約済み');
    expect(editor?.textContent).toContain('2026年9月1日 08:00');
    expect(editor?.textContent).toContain('Asia/Tokyo');
  });

  it('disables durable-draft and publication actions for an existing scheduled announcement', async () => {
    const fixture = await renderAnnouncementsPage({
      ...ANNOUNCEMENT_PAGE_SCENARIOS.default,
      selectedAnnouncementId: 'mock-announcement-004',
    });

    fixture.componentInstance.showEditEditor();
    fixture.detectChanges();

    const publish = rootElement(fixture).querySelector<HTMLButtonElement>(
      '[data-testid="announcement-publish-action"]',
    );
    const saveDraft = rootElement(fixture).querySelector<HTMLButtonElement>(
      '[data-testid="announcement-save-draft-action"]',
    );

    expect(saveDraft?.disabled).toBe(true);
    expect(publish?.disabled).toBe(true);
  });

  it('does not expose or enter edit mode for an archived announcement', async () => {
    const fixture = await renderAnnouncementsPage({
      ...ANNOUNCEMENT_PAGE_SCENARIOS.default,
      selectedAnnouncementId: 'mock-announcement-005',
    });

    expect(rootElement(fixture).textContent).toContain('アーカイブ済み');
    expect(
      rootElement(fixture).querySelector('[data-testid="edit-announcement-action"]'),
    ).toBeNull();

    fixture.componentInstance.showEditEditor();
    fixture.detectChanges();

    expect(rootElement(fixture).querySelector('[data-testid="announcement-editor"]')).toBeNull();
  });
});
