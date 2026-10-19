import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AnnouncementEditorDraft, AnnouncementEditorSubmission } from '../announcements.types';
import { AnnouncementEditorComponent } from './announcement-editor.component';

const workspaceAudience = {
  key: 'workspace:11111111-1111-1111-1111-111111111111',
  scope: 'workspace' as const,
  displayName: 'School Workspace',
  recipientCount: 1248,
  workspaceId: '11111111-1111-1111-1111-111111111111',
};

const teacherGroupAudience = {
  key: 'group:22222222-2222-2222-2222-222222222222',
  scope: 'group' as const,
  displayName: 'School Workspace / Teachers',
  recipientCount: 86,
  workspaceId: '11111111-1111-1111-1111-111111111111',
  groupId: '22222222-2222-2222-2222-222222222222',
};

const createDraft = (
  overrides: Partial<AnnouncementEditorDraft> = {},
): AnnouncementEditorDraft => ({
  title: 'School update',
  body: 'Announcement body',
  priority: 'normal',
  audienceKey: workspaceAudience.key,
  availableAudiences: [workspaceAudience, teacherGroupAudience],
  requiresReadConfirmation: false,
  ...overrides,
});

const nextRenderTick = (): Promise<void> =>
  new Promise((resolve) => {
    setTimeout(resolve);
  });

const renderEditor = async (
  draft: AnnouncementEditorDraft,
): Promise<ComponentFixture<AnnouncementEditorComponent>> => {
  await TestBed.configureTestingModule({
    imports: [AnnouncementEditorComponent],
  }).compileComponents();

  const fixture = TestBed.createComponent(AnnouncementEditorComponent);
  fixture.componentRef.setInput('draft', draft);
  fixture.detectChanges();
  return fixture;
};

const advanceToReview = (fixture: ComponentFixture<AnnouncementEditorComponent>): void => {
  fixture.componentInstance.nextStep();
  fixture.detectChanges();
  fixture.componentInstance.nextStep();
  fixture.detectChanges();
  fixture.componentInstance.nextStep();
  fixture.detectChanges();
};

describe('AnnouncementEditorComponent', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders only authorized audience options and falls back from an unauthorized draft audience', async () => {
    const fixture = await renderEditor(
      createDraft({
        audienceKey: 'group:unauthorized',
        availableAudiences: [teacherGroupAudience],
      }),
    );

    const host = fixture.nativeElement as HTMLElement;
    const select = host.querySelector(
      '[data-testid="announcement-editor-audience"]',
    ) as HTMLSelectElement;
    const options = Array.from(select.options);

    expect(options).toHaveLength(1);
    expect(options.map((option) => option.value)).toEqual([teacherGroupAudience.key]);
    expect(options.map((option) => option.textContent?.trim())).toEqual([
      'School Workspace / Teachers — 86名',
    ]);
    expect(select.value).toBe(teacherGroupAudience.key);
    expect(
      host.querySelector('[data-testid="announcement-audience-summary"]')?.textContent,
    ).toContain('School Workspace / Teachers');
    expect(
      host.querySelector('[data-testid="announcement-audience-summary"]')?.textContent,
    ).toContain('86名');
  });

  it('fails closed when no authorized audience is available', async () => {
    const fixture = await renderEditor(
      createDraft({
        audienceKey: '',
        availableAudiences: [],
      }),
    );

    const host = fixture.nativeElement as HTMLElement;
    const select = host.querySelector(
      '[data-testid="announcement-editor-audience"]',
    ) as HTMLSelectElement;
    expect(select.options).toHaveLength(0);
    expect(select.value).toBe('');
    expect(fixture.componentInstance.form.controls.audienceKey.invalid).toBe(true);
    expect(fixture.componentInstance.selectedAudience()).toBeNull();

    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    fixture.componentInstance.nextStep();
    fixture.detectChanges();

    expect(fixture.componentInstance.currentStep()).toBe('audience');
    expect(host.querySelector('[data-testid="announcement-audience-unavailable"]')).toBeTruthy();
    expect(host.querySelector('[data-testid="announcement-editor-error-summary"]')).toBeTruthy();
  });

  it('updates both immediate and review summaries when the selected audience changes', async () => {
    const fixture = await renderEditor(createDraft());

    fixture.componentInstance.form.controls.audienceKey.setValue(teacherGroupAudience.key);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const immediateSummary =
      host.querySelector('[data-testid="announcement-audience-summary"]')?.textContent ?? '';
    const reviewSummary =
      host.querySelector('[data-testid="announcement-review-summary"]')?.textContent ?? '';

    expect(immediateSummary).toContain('School Workspace / Teachers');
    expect(immediateSummary).toContain('86名');
    expect(reviewSummary).toContain('School Workspace / Teachers');
    expect(reviewSummary).toContain('86名');
  });

  it('requires an explicit confirmation before emitting the exact authorized publication', async () => {
    const fixture = await renderEditor(createDraft());
    let emitted: AnnouncementEditorSubmission | undefined;
    fixture.componentInstance.publishRequested.subscribe((value) => {
      emitted = value;
    });

    fixture.componentInstance.form.patchValue({
      title: '  Safety update  ',
      body: '  Review this announcement  ',
      audienceKey: teacherGroupAudience.key,
      priority: 'important',
      requiresReadConfirmation: true,
    });
    advanceToReview(fixture);
    fixture.componentInstance.publish();
    fixture.detectChanges();

    const confirmation = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="announcement-publication-confirmation"]',
    );
    expect(emitted).toBeUndefined();
    expect(confirmation?.textContent).toContain('School Workspace / Teachers');
    expect(confirmation?.textContent).toContain('86 recipients');
    expect(confirmation?.textContent).toContain('IMPORTANT');
    expect(confirmation?.textContent).toContain('Publish immediately');

    fixture.componentInstance.confirmPublication();

    expect(emitted).toEqual({
      title: 'Safety update',
      body: 'Review this announcement',
      priority: 'important',
      audience: teacherGroupAudience,
      requiresReadConfirmation: true,
      deliveryMode: 'now',
    });
  });

  it('keeps Save draft separate from Preview and reviews a scheduled local time before emitting it', async () => {
    const fixture = await renderEditor(createDraft());
    let saved: AnnouncementEditorSubmission | undefined;
    let published: AnnouncementEditorSubmission | undefined;
    fixture.componentInstance.saveDraftRequested.subscribe((value) => {
      saved = value;
    });
    fixture.componentInstance.publishRequested.subscribe((value) => {
      published = value;
    });

    fixture.componentInstance.form.patchValue({
      title: 'Scheduled safety update',
      body: 'Review this scheduled announcement.',
      deliveryMode: 'scheduled',
      scheduledLocalDateTime: '2026-09-02T09:45',
      timeZoneId: 'Asia/Tokyo',
    });
    fixture.detectChanges();

    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    fixture.componentInstance.nextStep();
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="announcement-save-draft-action"]')
      ?.click();
    expect(saved).toMatchObject({
      title: 'Scheduled safety update',
      deliveryMode: 'scheduled',
      scheduledLocalDateTime: '2026-09-02T09:45',
      timeZoneId: 'Asia/Tokyo',
    });
    expect(published).toBeUndefined();

    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="announcement-publish-action"]')
      ?.click();
    fixture.detectChanges();
    const confirmation = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="announcement-publication-confirmation"]',
    );
    expect(confirmation?.textContent).toContain('2026-09-02T09:45');
    expect(confirmation?.textContent).toContain('Asia/Tokyo');
    expect(published).toBeUndefined();

    fixture.componentInstance.confirmPublication();
    expect(published).toMatchObject({
      deliveryMode: 'scheduled',
      scheduledLocalDateTime: '2026-09-02T09:45',
      timeZoneId: 'Asia/Tokyo',
    });
  });

  it('uses visible progress labels as accessible names and retains ordinal descriptions', async () => {
    const fixture = await renderEditor(createDraft());
    const host = fixture.nativeElement as HTMLElement;

    for (const [index, name] of ['content', 'audience', 'delivery', 'review'].entries()) {
      const button = host.querySelector<HTMLButtonElement>(
        `[data-testid="announcement-editor-step-${name}"]`
      );
      expect(button).not.toBeNull();
      expect(button?.hasAttribute('aria-label')).toBe(false);
      expect(button?.getAttribute('aria-description')).toBe(`Step ${index + 1} of 4`);
      // Angular may elide inter-element whitespace in the DOM. The two
      // visible spans still contribute separate tokens to the accessible name.
      const parts = Array.from(button?.querySelectorAll('span') ?? [], (span) =>
        span.textContent?.trim()
      );
      expect(parts).toEqual([
        String(index + 1),
        `${name[0].toUpperCase()}${name.slice(1)}`,
      ]);
    }
  });

  it('validates each step, retains values through Back, and exposes an editable Review', async () => {
    const fixture = await renderEditor(createDraft({ title: '', body: '' }));
    const host = fixture.nativeElement as HTMLElement;

    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    await nextRenderTick();
    expect(fixture.componentInstance.currentStep()).toBe('content');
    expect(host.querySelector('[data-testid="announcement-editor-error-summary"]')).toBeTruthy();
    expect(document.activeElement).toBe(host.querySelector('[data-testid="announcement-editor-title"]'));

    fixture.componentInstance.form.patchValue({
      title: 'Step-by-step safety update',
      body: 'Keep this local form state while each step is reviewed.',
      deliveryMode: 'scheduled',
    });
    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    expect(fixture.componentInstance.currentStep()).toBe('audience');
    expect(host.querySelector('[data-testid="announcement-editor-current-step"]')?.textContent)
      .toContain('Step 2 of 4: Audience');

    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    expect(fixture.componentInstance.currentStep()).toBe('delivery');
    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    expect(fixture.componentInstance.currentStep()).toBe('delivery');
    expect(host.querySelector('[data-testid="announcement-schedule-local-time-error"]')).toBeTruthy();

    fixture.componentInstance.form.controls.scheduledLocalDateTime.setValue('2026-09-02T09:45');
    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    expect(fixture.componentInstance.currentStep()).toBe('review');
    expect(host.querySelector('[data-testid="announcement-editor-step-review"]')?.getAttribute('aria-current'))
      .toBe('step');
    expect(host.querySelector('[data-testid="announcement-review-summary"]')?.textContent)
      .toContain('Step-by-step safety update');
    const reviewPreview = host.querySelector<HTMLButtonElement>(
      '[data-testid="announcement-review-preview"]',
    );
    expect(reviewPreview).toBeTruthy();
    reviewPreview?.click();
    fixture.detectChanges();
    await nextRenderTick();
    expect(host.querySelector('[data-testid="announcement-local-preview"]')).toBeTruthy();
    fixture.componentInstance.closePreview(false);
    fixture.detectChanges();

    (host.querySelector('[data-testid="announcement-review-edit-content"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.componentInstance.currentStep()).toBe('content');
    expect(fixture.componentInstance.form.getRawValue()).toMatchObject({
      title: 'Step-by-step safety update',
      body: 'Keep this local form state while each step is reviewed.',
      deliveryMode: 'scheduled',
      scheduledLocalDateTime: '2026-09-02T09:45',
    });

    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    (host.querySelector('[data-testid="announcement-previous-step"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.componentInstance.currentStep()).toBe('content');
  });

  it('saves a valid draft from Audience without requiring Delivery or Review', async () => {
    const fixture = await renderEditor(createDraft());
    let saved: AnnouncementEditorSubmission | undefined;
    fixture.componentInstance.saveDraftRequested.subscribe((value) => {
      saved = value;
    });
    fixture.componentInstance.form.patchValue({
      title: 'Save while selecting an audience',
      body: 'The durable draft flow owns persistence outside this step UI.',
    });

    fixture.componentInstance.nextStep();
    fixture.detectChanges();
    expect(fixture.componentInstance.currentStep()).toBe('audience');
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('[data-testid="announcement-save-draft-action"]')
      ?.click();

    expect(saved).toMatchObject({
      title: 'Save while selecting an audience',
      body: 'The durable draft flow owns persistence outside this step UI.',
      audience: workspaceAudience,
      deliveryMode: 'now',
    });
    expect(fixture.componentInstance.currentStep()).toBe('audience');
  });

  it('renders a local-only live preview and returns to the preserved editor values', async () => {
    const fixture = await renderEditor(createDraft());
    let publishCount = 0;
    fixture.componentInstance.publishRequested.subscribe(() => {
      publishCount += 1;
    });

    fixture.componentInstance.form.patchValue({
      title: 'Preview this title',
      body: 'Preview this body without publishing it.',
      priority: 'critical',
      audienceKey: teacherGroupAudience.key,
      requiresReadConfirmation: true,
    });
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const previewAction = host.querySelector<HTMLButtonElement>(
      '[data-testid="announcement-preview-action"]',
    );
    expect(previewAction?.type).toBe('button');
    previewAction?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    await nextRenderTick();

    const preview = host.querySelector<HTMLElement>('[data-testid="announcement-local-preview"]');
    expect(preview?.textContent).toContain('Preview this title');
    expect(preview?.textContent).toContain('Preview this body without publishing it.');
    expect(preview?.textContent).toContain('CRITICAL');
    expect(preview?.textContent).toContain('School Workspace / Teachers');
    expect(preview?.textContent).toContain('86 recipients');
    expect(preview?.textContent).toContain('Required after publication');
    expect(host.querySelector('[data-testid="announcement-preview-cta-inert"]')).toBeTruthy();
    expect(host.querySelector('[data-testid="announcement-mark-read-action"]')).toBeNull();
    expect(publishCount).toBe(0);
    expect(document.activeElement).toBe(
      host.querySelector('[data-testid="announcement-preview-heading"]'),
    );

    const editAction = host.querySelector<HTMLButtonElement>(
      '[data-testid="announcement-edit-action"]',
    );
    expect(editAction?.type).toBe('button');
    editAction?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    await nextRenderTick();

    expect(host.querySelector('[data-testid="announcement-local-preview"]')).toBeNull();
    expect(fixture.componentInstance.form.getRawValue()).toMatchObject({
      title: 'Preview this title',
      body: 'Preview this body without publishing it.',
      priority: 'critical',
      audienceKey: teacherGroupAudience.key,
      requiresReadConfirmation: true,
      deliveryMode: 'now',
    });
    expect(document.activeElement).toBe(
      host.querySelector('[data-testid="announcement-editor-title"]'),
    );
    expect(publishCount).toBe(0);

    fixture.componentInstance.form.controls.body.setValue('Updated after returning to edit.');
    fixture.componentInstance.openPreview();
    fixture.detectChanges();
    await nextRenderTick();

    expect(host.querySelector('[data-testid="announcement-preview-body"]')?.textContent).toContain(
      'Updated after returning to edit.',
    );
    expect(publishCount).toBe(0);
  });

  it('closes an open preview without retaining revoked audience details', async () => {
    const fixture = await renderEditor(createDraft());
    fixture.componentInstance.form.patchValue({
      title: 'Local draft survives a revoked audience',
      body: 'The protected audience projection must not survive.',
    });
    fixture.componentInstance.form.markAsDirty();
    fixture.componentInstance.openPreview();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-testid="announcement-local-preview"]')?.textContent).toContain(
      'School Workspace',
    );

    fixture.componentRef.setInput(
      'draft',
      createDraft({
        availableAudiences: [],
      }),
    );
    fixture.detectChanges();
    await fixture.whenStable();
    await nextRenderTick();

    expect(fixture.componentInstance.previewOpen()).toBe(false);
    expect(host.querySelector('[data-testid="announcement-local-preview"]')).toBeNull();
    expect(host.textContent).not.toContain('School Workspace');
    expect(host.textContent).not.toContain('1,248');
    expect(fixture.componentInstance.form.controls.title.value).toBe(
      'Local draft survives a revoked audience',
    );
    expect(document.activeElement).toBe(
      host.querySelector('[data-testid="announcement-editor-title"]'),
    );
  });

  it('links field errors to invalid inputs and focuses the first invalid field after publish is requested', async () => {
    const fixture = await renderEditor(createDraft());
    fixture.componentInstance.form.patchValue({ title: '   ', body: ' ' });

    fixture.componentInstance.publish();
    fixture.detectChanges();
    await fixture.whenStable();
    await nextRenderTick();

    const host = fixture.nativeElement as HTMLElement;
    const title = host.querySelector<HTMLInputElement>('[data-testid="announcement-editor-title"]');
    const body = host.querySelector<HTMLTextAreaElement>(
      '[data-testid="announcement-editor-body"]',
    );
    const summary = host.querySelector<HTMLElement>(
      '[data-testid="announcement-editor-error-summary"]',
    );

    expect(summary?.getAttribute('role')).toBe('alert');
    expect(summary?.textContent).toContain(
      'タイトルを入力してください。空白だけでは公開できません。',
    );
    expect(summary?.textContent).toContain('本文を入力してください。空白だけでは公開できません。');
    expect(title?.getAttribute('aria-invalid')).toBe('true');
    expect(title?.getAttribute('aria-describedby')).toBe(
      'announcement-title-help announcement-title-error',
    );
    expect(body?.getAttribute('aria-invalid')).toBe('true');
    expect(body?.getAttribute('aria-describedby')).toBe(
      'announcement-body-help announcement-body-error',
    );
    expect(document.activeElement).toBe(title);

    (summary?.querySelector('a[href="#announcement-body"]') as HTMLAnchorElement).click();
    expect(document.activeElement).toBe(body);
  });

  it('announces a preserved submission failure inside the form', async () => {
    const fixture = await renderEditor(createDraft());
    fixture.componentRef.setInput('submissionError', '配信対象を再確認してください。');
    fixture.detectChanges();

    const error = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="announcement-editor-submission-error"]',
    );
    expect(error?.getAttribute('role')).toBe('alert');
    expect(error?.textContent).toContain('公開できませんでした。');
    expect(error?.textContent).toContain('入力内容はこのフォームに保持されています。');
  });

  it('uses the non-leaking count fallback when the authorized projection has no recipient estimate', async () => {
    const fixture = await renderEditor(
      createDraft({
        audienceKey: 'global',
        availableAudiences: [{ key: 'global', scope: 'global', displayName: 'Tenant-wide' }],
      }),
    );

    const host = fixture.nativeElement as HTMLElement;
    const immediateSummary =
      host.querySelector('[data-testid="announcement-audience-summary"]')?.textContent ?? '';
    const reviewSummary =
      host.querySelector('[data-testid="announcement-review-summary"]')?.textContent ?? '';

    expect(immediateSummary).toContain('受信者数は公開前の確認時に再計算されます。');
    expect(reviewSummary).toContain('未取得');
  });

  it('keeps live edits when a refreshed draft changes the authorized audience options', async () => {
    const fixture = await renderEditor(createDraft());
    let latestDraft: AnnouncementEditorDraft | undefined;
    fixture.componentInstance.draftChanged.subscribe((draft) => {
      latestDraft = draft;
    });

    fixture.componentInstance.form.patchValue({
      title: 'Live title edited after publish failed',
      body: 'Live body edited after publish failed',
    });
    fixture.componentInstance.form.markAsDirty();

    expect(latestDraft).toMatchObject({
      title: 'Live title edited after publish failed',
      body: 'Live body edited after publish failed',
      audienceKey: workspaceAudience.key,
    });

    fixture.componentRef.setInput(
      'draft',
      createDraft({
        title: 'Stale submitted title',
        body: 'Stale submitted body',
        audienceKey: teacherGroupAudience.key,
        availableAudiences: [teacherGroupAudience],
      }),
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.form.getRawValue()).toMatchObject({
      title: 'Live title edited after publish failed',
      body: 'Live body edited after publish failed',
      audienceKey: teacherGroupAudience.key,
    });
    expect(fixture.componentInstance.selectedAudience()).toEqual(teacherGroupAudience);
  });

  it('uses the authoritative announcement limits and exposes a separate durable draft-save action', async () => {
    const fixture = await renderEditor(createDraft());
    const host = fixture.nativeElement as HTMLElement;
    const title = host.querySelector<HTMLInputElement>('[data-testid="announcement-editor-title"]');
    const body = host.querySelector<HTMLTextAreaElement>(
      '[data-testid="announcement-editor-body"]',
    );

    expect(title?.maxLength).toBe(200);
    expect(body?.maxLength).toBe(20_000);
    expect(host.querySelector('[data-testid="announcement-preview-action"]')).toBeTruthy();
    expect(host.querySelector('[data-testid="announcement-save-draft-action"]')).toBeTruthy();

    fixture.componentInstance.form.controls.title.setValue('a'.repeat(201));
    fixture.componentInstance.form.controls.title.markAsTouched();
    fixture.componentInstance.form.controls.body.setValue('b'.repeat(20_001));
    fixture.componentInstance.form.controls.body.markAsTouched();

    expect(fixture.componentInstance.fieldError('title')).toBe(
      'タイトルは200文字以内で入力してください。',
    );
    expect(fixture.componentInstance.fieldError('body')).toBe(
      '本文は20,000文字以内で入力してください。',
    );
  });

  it('returns from a confirmation without changing the editable form values', async () => {
    const fixture = await renderEditor(createDraft());
    fixture.componentInstance.form.patchValue({
      title: 'Review before publication',
      body: 'Keep this draft after returning to edit.',
      priority: 'critical',
      audienceKey: teacherGroupAudience.key,
    });

    fixture.componentInstance.publish();
    fixture.detectChanges();
    expect(fixture.componentInstance.publicationReviewOpen()).toBe(true);

    fixture.componentInstance.cancelPublicationReview();
    fixture.detectChanges();

    expect(fixture.componentInstance.publicationReviewOpen()).toBe(false);
    expect(fixture.componentInstance.publicationReview()).toBeNull();
    expect(fixture.componentInstance.form.getRawValue()).toMatchObject({
      title: 'Review before publication',
      body: 'Keep this draft after returning to edit.',
      priority: 'critical',
      audienceKey: teacherGroupAudience.key,
      requiresReadConfirmation: false,
      deliveryMode: 'now',
    });
  });

  it('keeps confirmation single-flight and returns to the preserved draft after a failed response', async () => {
    const fixture = await renderEditor(createDraft());
    let publishCount = 0;
    fixture.componentInstance.publishRequested.subscribe(() => {
      publishCount += 1;
    });

    fixture.componentInstance.publish();
    fixture.componentInstance.confirmPublication();
    fixture.componentInstance.confirmPublication();

    expect(publishCount).toBe(1);
    expect(fixture.componentInstance.publicationConfirming()).toBe(true);

    fixture.componentRef.setInput('submissionError', 'Publication was not confirmed.');
    fixture.detectChanges();

    expect(fixture.componentInstance.publicationConfirming()).toBe(false);
    expect(fixture.componentInstance.publicationReviewOpen()).toBe(false);
    expect(fixture.componentInstance.form.controls.title.value).toBe('School update');
    expect(fixture.componentInstance.form.controls.body.value).toBe('Announcement body');
  });

  it('clears the busy confirmation state when the authoritative publishing command settles', async () => {
    const fixture = await renderEditor(createDraft());

    fixture.componentInstance.publish();
    fixture.componentInstance.confirmPublication();
    expect(fixture.componentInstance.publicationConfirming()).toBe(true);

    fixture.componentRef.setInput('publishing', true);
    fixture.detectChanges();
    fixture.componentRef.setInput('publishing', false);
    fixture.detectChanges();

    expect(fixture.componentInstance.publicationConfirming()).toBe(false);
    expect(fixture.componentInstance.publicationReviewOpen()).toBe(false);
    expect(fixture.componentInstance.form.controls.title.value).toBe('School update');
  });
});
