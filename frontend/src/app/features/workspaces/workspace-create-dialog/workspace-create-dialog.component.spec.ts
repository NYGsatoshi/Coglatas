import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WorkspaceCreateViewModel } from '../workspaces.types';
import { WorkspaceCreateDialogComponent } from './workspace-create-dialog.component';

const idleState: WorkspaceCreateViewModel = { status: 'idle', fieldErrors: [] };

describe('WorkspaceCreateDialogComponent', () => {
  let fixture: ComponentFixture<WorkspaceCreateDialogComponent>;
  let component: WorkspaceCreateDialogComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [WorkspaceCreateDialogComponent] }).compileComponents();
    fixture = TestBed.createComponent(WorkspaceCreateDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('canCreate', true);
    fixture.componentRef.setInput('createState', idleState);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('marks Name as the enabled initial control and exposes no internal Workspace identifier field', () => {
    const root = fixture.nativeElement as HTMLElement;
    const name = root.querySelector<HTMLInputElement>('[data-testid="workspace-create-name"]');

    expect(name).not.toBeNull();
    expect(name?.disabled).toBe(false);
    expect(name?.tabIndex).toBeGreaterThanOrEqual(0);
    expect(root.querySelector('[name="id"], [name="workspaceId"], [name="slug"]')).toBeNull();
    expect(root.textContent).toContain('Workspace identity is generated automatically');
  });

  it('focuses a linked error summary when required Name is blank', async () => {
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const summary = root.querySelector<HTMLElement>('[data-testid="workspace-create-error-summary"]');
    const name = root.querySelector<HTMLInputElement>('[data-testid="workspace-create-name"]');
    expect(summary).not.toBeNull();
    expect(document.activeElement).toBe(summary);
    expect(summary?.textContent).toContain('Enter a Workspace name');
    expect(name?.getAttribute('aria-invalid')).toBe('true');
    expect(name?.getAttribute('aria-describedby')).toContain('workspace-create-name-errors');
  });

  it('uses native form submit and emits only the user-facing fields', () => {
    const submitted = vi.fn();
    component.submitted.subscribe(submitted);
    component.form.setValue({
      name: '  Research Lab  ',
      description: 'Evidence workspace',
      icon: '🔬',
    });
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')
      ?.click();

    expect(submitted).toHaveBeenCalledOnce();
    expect(submitted).toHaveBeenCalledWith({
      name: '  Research Lab  ',
      description: 'Evidence workspace',
      icon: '🔬',
    });
  });

  it('maps server field errors to their controls and shows a request ID', () => {
    fixture.componentRef.setInput('createState', {
      status: 'error',
      message: 'Check the Workspace details.',
      requestId: 'request-create-1',
      fieldErrors: [{ field: 'description', message: 'Description is too long.' }],
    } satisfies WorkspaceCreateViewModel);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const description = root.querySelector<HTMLTextAreaElement>(
      '[data-testid="workspace-create-description"]',
    );
    expect(description?.getAttribute('aria-invalid')).toBe('true');
    expect(root.textContent).toContain('Description is too long.');
    expect(root.textContent).toContain('request-create-1');
  });

  it('retries only activation after the create response was committed', () => {
    const submitted = vi.fn();
    const retry = vi.fn();
    component.submitted.subscribe(submitted);
    component.retryActivation.subscribe(retry);
    fixture.componentRef.setInput('createState', {
      status: 'committedPendingActivation',
      fieldErrors: [],
      createdWorkspaceId: 'workspace-created',
      requestId: 'request-created',
    } satisfies WorkspaceCreateViewModel);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="workspace-create-form"]')).toBeNull();
    root.querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')?.click();

    expect(retry).toHaveBeenCalledOnce();
    expect(submitted).not.toHaveBeenCalled();
  });

  it('disables another create attempt when the live capability is revoked', () => {
    fixture.componentRef.setInput('canCreate', false);
    fixture.componentRef.setInput('createState', {
      status: 'error',
      fieldErrors: [{ field: 'form', message: 'Permission is no longer available.' }],
      message: 'You do not currently have permission to create a Workspace.',
    } satisfies WorkspaceCreateViewModel);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector<HTMLButtonElement>('.coglatas-dialog__confirm')?.disabled).toBe(true);
    expect(root.querySelector('#workspace-create-error-summary')).not.toBeNull();
    expect(root.querySelector<HTMLAnchorElement>('a[href="#workspace-create-error-summary"]')).not.toBeNull();
  });
});
