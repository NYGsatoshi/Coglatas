import { A11yModule, InteractivityChecker } from '@angular/cdk/a11y';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CoglatasDialogComponent } from './coglatas-dialog.component';

@Component({
  standalone: true,
  imports: [A11yModule, CoglatasDialogComponent],
  template: `
    @if (showTrigger()) {
      <button data-testid="dialog-trigger" type="button" (click)="open.set(true)">Open dialog</button>
    }
    <main id="dialog-fallback" tabindex="-1">Dialog destination</main>
    <app-coglatas-dialog
      [open]="open()"
      title="Create workspace"
      [description]="description()"
      confirmLabel="Create workspace"
      [confirmForm]="confirmForm()"
      [confirmDisabled]="confirmDisabled()"
      [busy]="busy()"
      focusReturnFallbackId="dialog-fallback"
      (confirm)="confirmCount += 1"
      (cancel)="handleCancel()"
      (closed)="closedCount += 1"
    >
      <form id="workspace-form" (submit)="handleSubmit($event)">
        <label for="workspace-name">Workspace name</label>
        <input id="workspace-name" cdkFocusInitial required />
      </form>
    </app-coglatas-dialog>
  `
})
class DialogHostComponent {
  readonly open = signal(false);
  readonly description = signal<string | null>('Give the workspace a recognizable name.');
  readonly confirmForm = signal<string | null>(null);
  readonly confirmDisabled = signal(false);
  readonly busy = signal(false);
  readonly showTrigger = signal(true);
  confirmCount = 0;
  cancelCount = 0;
  closedCount = 0;
  submitCount = 0;

  handleCancel(): void {
    this.cancelCount += 1;
    this.open.set(false);
  }

  handleSubmit(event: Event): void {
    event.preventDefault();
    this.submitCount += 1;
  }
}

describe('CoglatasDialogComponent', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => window.localStorage.removeItem('coglatas.locale'));

  async function createFixture(): Promise<ComponentFixture<DialogHostComponent>> {
    await TestBed.configureTestingModule({
      imports: [DialogHostComponent],
      providers: [
        {
          provide: InteractivityChecker,
          useValue: {
            isFocusable: (element: HTMLElement) => !element.hasAttribute('disabled'),
            isTabbable: (element: HTMLElement) => !element.hasAttribute('disabled') && element.tabIndex >= 0
          }
        }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(DialogHostComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    return fixture;
  }

  async function openDialog(fixture: ComponentFixture<DialogHostComponent>): Promise<HTMLElement> {
    const trigger = fixture.nativeElement.querySelector('[data-testid="dialog-trigger"]') as HTMLButtonElement;
    trigger.focus();
    trigger.click();
    fixture.detectChanges();
    await fixture.whenStable();

    return fixture.nativeElement.querySelector('[role="dialog"]') as HTMLElement;
  }

  it('labels the dialog and auto-focuses projected cdkFocusInitial content', async () => {
    const fixture = await createFixture();
    const dialog = await openDialog(fixture);

    const titleId = dialog.getAttribute('aria-labelledby');
    const descriptionId = dialog.getAttribute('aria-describedby');
    const title = titleId ? document.getElementById(titleId) : null;
    const description = descriptionId ? document.getElementById(descriptionId) : null;
    const nameInput = dialog.querySelector('#workspace-name');

    expect(title?.textContent).toContain('Create workspace');
    expect(description?.textContent).toContain('Give the workspace a recognizable name.');
    expect(nameInput).toBe(document.activeElement);
    expect(dialog.contains(document.activeElement)).toBe(true);
    expect(dialog.parentElement?.querySelectorAll('.cdk-focus-trap-anchor').length).toBe(2);

    fixture.destroy();
  });

  it('returns focus to the invoking control when open changes back to false', async () => {
    const fixture = await createFixture();
    const trigger = fixture.nativeElement.querySelector('[data-testid="dialog-trigger"]') as HTMLButtonElement;
    await openDialog(fixture);

    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    await Promise.resolve();

    expect(document.activeElement).toBe(trigger);
    fixture.destroy();
  });

  it('does not restore focus when it first receives a closed binding', async () => {
    const fixture = await createFixture();
    const trigger = fixture.nativeElement.querySelector('[data-testid="dialog-trigger"]') as HTMLButtonElement;

    trigger.focus();
    await Promise.resolve();

    expect(document.activeElement).toBe(trigger);
    fixture.destroy();
  });

  it('returns focus to a stable fallback when the invoking control was removed', async () => {
    const fixture = await createFixture();
    await openDialog(fixture);

    fixture.componentInstance.showTrigger.set(false);
    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    await Promise.resolve();

    expect(document.activeElement).toBe(document.getElementById('dialog-fallback'));
    fixture.destroy();
  });

  it('suppresses Escape, backdrop, cancel, and confirm while busy', async () => {
    const fixture = await createFixture();
    const dialog = await openDialog(fixture);
    fixture.componentInstance.busy.set(true);
    fixture.detectChanges();

    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    document.dispatchEvent(escape);

    const backdrop = dialog.parentElement as HTMLElement;
    backdrop.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));

    const buttons = Array.from(dialog.querySelectorAll('button'));
    const closeButton = buttons.find((button) => button.getAttribute('aria-label') === 'Close');
    const cancelButton = buttons.find((button) => button.textContent?.trim() === 'Cancel');
    const confirmButton = buttons.find((button) => button.textContent?.trim() === 'Working…');
    closeButton?.click();
    cancelButton?.click();
    confirmButton?.click();
    fixture.detectChanges();

    expect(escape.defaultPrevented).toBe(true);
    expect(dialog.getAttribute('aria-busy')).toBe('true');
    expect(closeButton?.disabled).toBe(true);
    expect(cancelButton?.disabled).toBe(true);
    expect(confirmButton?.disabled).toBe(true);
    expect(fixture.componentInstance.open()).toBe(true);
    expect(fixture.componentInstance.cancelCount).toBe(0);
    expect(fixture.componentInstance.closedCount).toBe(0);
    expect(fixture.componentInstance.confirmCount).toBe(0);

    fixture.destroy();
  });

  it('keeps cancel available while confirm is disabled by form validity', async () => {
    const fixture = await createFixture();
    fixture.componentInstance.confirmDisabled.set(true);
    const dialog = await openDialog(fixture);

    const buttons = Array.from(dialog.querySelectorAll('button'));
    const cancelButton = buttons.find((button) => button.textContent?.trim() === 'Cancel') as HTMLButtonElement;
    const confirmButton = buttons.find((button) => button.textContent?.trim() === 'Create workspace') as HTMLButtonElement;

    expect(cancelButton.disabled).toBe(false);
    expect(confirmButton.disabled).toBe(true);
    confirmButton.click();
    expect(fixture.componentInstance.confirmCount).toBe(0);

    cancelButton.click();
    fixture.detectChanges();
    await Promise.resolve();

    expect(fixture.componentInstance.cancelCount).toBe(1);
    expect(fixture.componentInstance.closedCount).toBe(1);
    expect(fixture.componentInstance.open()).toBe(false);

    fixture.destroy();
  });

  it('preserves the existing confirm output in button mode', async () => {
    const fixture = await createFixture();
    const dialog = await openDialog(fixture);
    const confirmButton = Array.from(dialog.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Create workspace'
    ) as HTMLButtonElement;

    expect(confirmButton.type).toBe('button');
    expect(confirmButton.hasAttribute('form')).toBe(false);

    confirmButton.click();

    expect(fixture.componentInstance.confirmCount).toBe(1);
    expect(fixture.componentInstance.submitCount).toBe(0);
    expect(fixture.componentInstance.open()).toBe(true);

    fixture.destroy();
  });

  it('can associate the footer confirmation button with a projected native form', async () => {
    const fixture = await createFixture();
    fixture.componentInstance.confirmForm.set('workspace-form');
    const dialog = await openDialog(fixture);
    const confirmButton = Array.from(dialog.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Create workspace'
    ) as HTMLButtonElement;

    expect(confirmButton.type).toBe('submit');
    expect(confirmButton.getAttribute('form')).toBe('workspace-form');

    confirmButton.click();

    expect(fixture.componentInstance.confirmCount).toBe(0);
    expect(fixture.componentInstance.submitCount).toBe(0);

    const nameInput = dialog.querySelector('#workspace-name') as HTMLInputElement;
    nameInput.value = 'Research workspace';
    confirmButton.click();

    expect(fixture.componentInstance.submitCount).toBe(1);

    fixture.destroy();
  });
});
