import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AnnouncementEditorComponent } from './announcement-editor.component';

const nextRenderTick = (): Promise<void> => new Promise((resolve) => setTimeout(resolve));

const audience = {
  key: 'workspace:school',
  scope: 'workspace' as const,
  displayName: 'School Workspace',
  recipientCount: 1248,
  workspaceId: '11111111-1111-1111-1111-111111111111',
};

async function renderEditor(): Promise<ComponentFixture<AnnouncementEditorComponent>> {
  await TestBed.configureTestingModule({
    imports: [AnnouncementEditorComponent],
  }).compileComponents();

  const fixture = TestBed.createComponent(AnnouncementEditorComponent);
  fixture.componentRef.setInput('draft', {
    title: 'Application notice',
    body: 'Review the application details.',
    priority: 'important',
    audienceKey: audience.key,
    availableAudiences: [audience],
    requiresReadConfirmation: false,
  });
  fixture.detectChanges();
  return fixture;
}

function enter(host: HTMLElement, testId: string, value: string): HTMLInputElement {
  const input = host.querySelector<HTMLInputElement>(`[data-testid="${testId}"]`)!;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('blur', { bubbles: true }));
  return input;
}

describe('AnnouncementEditorComponent content links', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('updates CTA and attachment live preview and preserves them in a draft submission', async () => {
    const fixture = await renderEditor();
    const host = fixture.nativeElement as HTMLElement;
    let saved: unknown;
    fixture.componentInstance.saveDraftRequested.subscribe((submission) => {
      saved = submission;
    });

    enter(host, 'announcement-editor-cta-label', 'Open application');
    enter(host, 'announcement-editor-cta-url', '/forms/application');
    enter(host, 'announcement-editor-attachment-label', 'Guide PDF');
    enter(host, 'announcement-editor-attachment-url', 'https://example.jp/guide.pdf');
    fixture.detectChanges();

    host.querySelector<HTMLButtonElement>('[data-testid="announcement-preview-action"]')?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const preview = host.querySelector<HTMLElement>('[data-testid="announcement-local-preview"]');
    expect(preview?.textContent).toContain('Open application');
    expect(preview?.textContent).toContain('Guide PDF');
    expect(
      host.querySelector('[data-testid="announcement-preview-content-cta-inert"]')?.getAttribute('href'),
    ).toBeNull();

    host.querySelector<HTMLButtonElement>('[data-testid="announcement-edit-action"]')?.click();
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('[data-testid="announcement-save-draft-action"]')?.click();

    expect(saved).toMatchObject({
      cta: { label: 'Open application', url: '/forms/application' },
      attachment: { label: 'Guide PDF', url: 'https://example.jp/guide.pdf' },
    });
  });

  it('blocks publication and focuses an unsafe CTA URL', async () => {
    const fixture = await renderEditor();
    const host = fixture.nativeElement as HTMLElement;
    let publicationCount = 0;
    fixture.componentInstance.publishRequested.subscribe(() => {
      publicationCount += 1;
    });

    enter(host, 'announcement-editor-cta-label', 'Unsafe action');
    const url = enter(host, 'announcement-editor-cta-url', 'javascript:alert(1)');
    fixture.detectChanges();

    fixture.componentInstance.publish();
    fixture.detectChanges();
    await fixture.whenStable();
    await nextRenderTick();

    expect(host.querySelector('[data-testid="announcement-editor-error-summary"]')?.textContent).toContain(
      '安全なHTTPS URL',
    );
    expect(url.getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(url);
    expect(fixture.componentInstance.publicationReviewOpen()).toBe(false);
    expect(publicationCount).toBe(0);
  });

  it('blocks publication and focuses an attachment with a missing URL', async () => {
    const fixture = await renderEditor();
    const host = fixture.nativeElement as HTMLElement;
    let publicationCount = 0;
    fixture.componentInstance.publishRequested.subscribe(() => {
      publicationCount += 1;
    });

    enter(host, 'announcement-editor-attachment-label', 'Guide PDF');
    fixture.detectChanges();
    fixture.componentInstance.publish();
    fixture.detectChanges();
    await fixture.whenStable();
    await nextRenderTick();

    const url = host.querySelector<HTMLInputElement>(
      '[data-testid="announcement-editor-attachment-url"]',
    );
    expect(host.querySelector('[data-testid="announcement-attachment-url-error"]')?.textContent).toContain(
      'URLを入力してください',
    );
    expect(url?.getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(url);
    expect(fixture.componentInstance.publicationReviewOpen()).toBe(false);
    expect(publicationCount).toBe(0);
  });

  it('requires a label and URL as one optional pair', async () => {
    const fixture = await renderEditor();
    const host = fixture.nativeElement as HTMLElement;

    enter(host, 'announcement-editor-attachment-label', 'Guide PDF');
    fixture.detectChanges();
    fixture.componentInstance.saveDraft();
    fixture.detectChanges();
    await fixture.whenStable();
    await nextRenderTick();

    const url = host.querySelector<HTMLInputElement>(
      '[data-testid="announcement-editor-attachment-url"]',
    );

    expect(host.querySelector('[data-testid="announcement-attachment-url-error"]')?.textContent).toContain(
      'URLを入力してください',
    );
    expect(url?.getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(url);
    expect(fixture.componentInstance.publicationReviewOpen()).toBe(false);
  });
});
