import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { CoglatasDialogComponent } from '../../../shared/ui/coglatas-dialog/coglatas-dialog.component';
import {
  ProjectCreateOptions,
  PROJECT_VISIBILITY_MEMBERS_ONLY,
  PROJECT_VISIBILITY_RESTRICTED,
} from '../project-create.api';
import {
  EMPTY_PROJECT_CREATE_STATE,
  ProjectCreateOptionsViewModel,
  ProjectCreateViewModel,
} from '../project-create.facade';
import { ProjectCreateDialogComponent } from './project-create-dialog.component';

const workspaceId = '11111111-1111-4111-8111-111111111111';
const groupId = '44444444-4444-4444-8444-444444444444';

const options: ProjectCreateOptions = {
  requestId: 'request-options',
  workspaceId,
  canCreateUngrouped: true,
  allowedVisibilities: [PROJECT_VISIBILITY_MEMBERS_ONLY, PROJECT_VISIBILITY_RESTRICTED],
  groups: [{ id: groupId, name: 'Research Group' }],
};

const readyOptions: ProjectCreateOptionsViewModel = {
  status: 'ready',
  workspaceId,
  data: options,
};

describe('ProjectCreateDialogComponent', () => {
  let fixture: ComponentFixture<ProjectCreateDialogComponent>;
  let component: ProjectCreateDialogComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProjectCreateDialogComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(ProjectCreateDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('workspaceName', 'Evidence Workspace');
    fixture.componentRef.setInput('optionsState', readyOptions);
    fixture.componentRef.setInput('createState', EMPTY_PROJECT_CREATE_STATE);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('shows named choices and Draft inheritance without exposing identifier or member inputs', () => {
    const root = fixture.nativeElement as HTMLElement;
    const text = root.textContent ?? '';

    expect(text).toContain('Evidence Workspace');
    expect(text).toContain('Research Group');
    expect(text).toContain('Draft');
    expect(text).toContain('initial Project Owner');
    expect(text).toContain('Members are added after creation');
    expect(text).not.toContain(groupId);
    expect(text).not.toContain(workspaceId);
    expect(
      root.querySelector('[name="workspaceId"], [name="ownerUserId"], [name="members"]'),
    ).toBeNull();
    expect(
      root.querySelector<HTMLInputElement>('[data-testid="project-create-title"]'),
    ).not.toBeNull();
  });

  it('filters Group candidates by name without asking for an internal ID', () => {
    component.form.controls.groupSearch.setValue('research');
    fixture.detectChanges();
    expect(component.filteredGroups.map((group) => group.name)).toEqual(['Research Group']);

    component.form.controls.groupSearch.setValue('not available');
    fixture.detectChanges();
    expect(component.filteredGroups).toEqual([]);
  });

  it('retains the Project name when reauthorization starts during input before a named Group is selected', () => {
    const root = fixture.nativeElement as HTMLElement;
    const title = root.querySelector<HTMLInputElement>('[data-testid="project-create-title"]');

    title!.addEventListener(
      'input',
      () => {
        fixture.componentRef.setInput('optionsState', { status: 'loading', workspaceId });
        fixture.detectChanges();
      },
      { capture: true, once: true },
    );
    title!.value = 'Evidence Project';
    title!.dispatchEvent(new Event('input', { bubbles: true }));
    expect(
      (fixture.nativeElement as HTMLElement).querySelector<HTMLFormElement>(
        '[data-testid="project-create-form"]',
      )?.hidden,
    ).toBe(true);
    expect(component.confirmDisabled).toBe(true);
    expect(
      (fixture.nativeElement as HTMLElement).querySelector(
        '[data-testid="project-create-options-loading"]',
      ),
    ).not.toBeNull();

    fixture.componentRef.setInput('optionsState', readyOptions);
    fixture.detectChanges();

    const reauthorizedRoot = fixture.nativeElement as HTMLElement;
    const reauthorizedDescription = reauthorizedRoot.querySelector<HTMLTextAreaElement>(
      '[data-testid="project-create-description"]',
    );
    const reauthorizedGroup = reauthorizedRoot.querySelector<HTMLSelectElement>(
      '[data-testid="project-create-group"]',
    );

    reauthorizedDescription!.value = 'Retained description';
    reauthorizedDescription!.dispatchEvent(new Event('input', { bubbles: true }));
    reauthorizedGroup!.value = groupId;
    reauthorizedGroup!.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    expect(component.form.getRawValue()).toMatchObject({
      title: 'Evidence Project',
      description: 'Retained description',
      groupId,
    });
    expect(
      reauthorizedRoot.querySelector<HTMLInputElement>('[data-testid="project-create-title"]')?.value,
    ).toBe('Evidence Project');
  });

  it('preserves an allowed Visibility when protected options are reauthorized', () => {
    component.form.controls.visibility.setValue(PROJECT_VISIBILITY_RESTRICTED);
    fixture.componentRef.setInput('optionsState', { status: 'idle' });
    fixture.detectChanges();
    fixture.componentRef.setInput('optionsState', readyOptions);
    fixture.detectChanges();

    expect(component.form.controls.visibility.value).toBe(PROJECT_VISIBILITY_RESTRICTED);

    fixture.componentRef.setInput('optionsState', { status: 'idle' });
    fixture.detectChanges();
    fixture.componentRef.setInput('optionsState', {
      ...readyOptions,
      data: {
        ...options,
        allowedVisibilities: [PROJECT_VISIBILITY_MEMBERS_ONLY],
      },
    } satisfies ProjectCreateOptionsViewModel);
    fixture.detectChanges();

    expect(component.form.controls.visibility.value).toBe(PROJECT_VISIBILITY_MEMBERS_ONLY);
  });

  it('focuses a linked summary for required name and inverted dates', async () => {
    component.form.patchValue({
      title: '   ',
      startDate: '2026-08-28',
      endDate: '2026-08-24',
    });
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')
      ?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    const summary = root.querySelector<HTMLElement>('[data-testid="project-create-error-summary"]');
    expect(summary).not.toBeNull();
    expect(document.activeElement).toBe(summary);
    expect(summary?.textContent).toContain('Enter a Project name');
    expect(summary?.textContent).toContain('cannot be before the start date');
    expect(
      root
        .querySelector<HTMLInputElement>('[data-testid="project-create-end-date"]')
        ?.getAttribute('aria-invalid'),
    ).toBe('true');
  });

  it('emits only canonical user-facing fields and blocks duplicate native submits while busy', () => {
    const submitted = vi.fn();
    component.submitted.subscribe(submitted);
    component.form.setValue({
      title: 'Evidence review',
      description: 'Plain text evidence summary',
      groupSearch: 'Research',
      groupId,
      visibility: PROJECT_VISIBILITY_RESTRICTED,
      startDate: '2026-08-24',
      endDate: '2026-08-28',
    });
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')
      ?.click();
    expect(submitted).toHaveBeenCalledOnce();
    expect(submitted).toHaveBeenCalledWith({
      title: 'Evidence review',
      description: 'Plain text evidence summary',
      groupId,
      visibility: PROJECT_VISIBILITY_RESTRICTED,
      startDate: '2026-08-24',
      endDate: '2026-08-28',
    });

    fixture.componentRef.setInput('createState', {
      status: 'submitting',
      fieldErrors: [],
    } satisfies ProjectCreateViewModel);
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')
      ?.click();
    expect(submitted).toHaveBeenCalledOnce();
  });

  it('requires a named Group when the backend grants only Group-scoped creation', async () => {
    fixture.componentRef.setInput('optionsState', {
      ...readyOptions,
      data: { ...options, canCreateUngrouped: false },
    } satisfies ProjectCreateOptionsViewModel);
    component.form.patchValue({ title: 'Group Project', groupId: '' });
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')
      ?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Choose a Group available to you',
    );
  });

  it('maps server errors and request tracking to the linked field summary', async () => {
    fixture.componentRef.setInput('createState', {
      status: 'error',
      fieldErrors: [{ field: 'description', message: 'Description is too long.' }],
      message: 'Check the Project details.',
      requestId: 'request-create-error',
    } satisfies ProjectCreateViewModel);
    fixture.detectChanges();
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Description is too long.');
    expect(root.textContent).toContain('request-create-error');
    expect(
      root
        .querySelector<HTMLTextAreaElement>('[data-testid="project-create-description"]')
        ?.getAttribute('aria-invalid'),
    ).toBe('true');
    expect(document.activeElement).toBe(
      root.querySelector('[data-testid="project-create-error-summary"]'),
    );
  });

  it('keeps an unsent authorization-clear recovery visible and focuses Retry while options are rechecked', async () => {
    fixture.componentRef.setInput('optionsState', {
      status: 'error',
      workspaceId,
      message: 'Project creation options changed and must be checked again.',
    } satisfies ProjectCreateOptionsViewModel);
    fixture.componentRef.setInput('createState', {
      status: 'error',
      fieldErrors: [],
      message:
        'Project creation was stopped before it was sent. Recheck access and submit the same details after the options reload.',
    } satisfies ProjectCreateViewModel);
    fixture.detectChanges();
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    expect(
      root.querySelector('[data-testid="project-create-create-status"]')?.textContent,
    ).toContain('stopped before it was sent');
    const retry = root.querySelector<HTMLButtonElement>(
      '[data-testid="project-create-options-retry"]',
    );
    expect(retry).not.toBeNull();
    expect(document.activeElement).toBe(retry);
  });

  it('suppresses Escape cancellation while the create request is busy', () => {
    const cancelled = vi.fn();
    component.cancelled.subscribe(cancelled);
    fixture.componentRef.setInput('createState', {
      status: 'submitting',
      fieldErrors: [],
    } satisfies ProjectCreateViewModel);
    fixture.detectChanges();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(cancelled).not.toHaveBeenCalled();
  });

  it('uses only navigation recovery after the strict create response was committed', () => {
    const submitted = vi.fn();
    const navigationRetried = vi.fn();
    component.submitted.subscribe(submitted);
    component.navigationRetried.subscribe(navigationRetried);
    fixture.componentRef.setInput('createState', {
      status: 'committedPendingNavigation',
      fieldErrors: [],
      requestId: 'request-created',
    } satisfies ProjectCreateViewModel);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="project-create-form"]')).toBeNull();
    const dialog = fixture.debugElement.query(By.directive(CoglatasDialogComponent))
      .componentInstance as CoglatasDialogComponent;
    expect(dialog.focusReturnFallbackId).toBe('projects-resume-created-project');
    root.querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')?.click();
    expect(navigationRetried).toHaveBeenCalledOnce();
    expect(submitted).not.toHaveBeenCalled();
  });
});
