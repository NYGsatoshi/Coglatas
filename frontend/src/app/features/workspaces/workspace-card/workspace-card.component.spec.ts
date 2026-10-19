import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { MEMBER_WORKSPACE } from '../workspaces.mock';
import { WorkspaceCardComponent } from './workspace-card.component';

describe('WorkspaceCardComponent selection', () => {
  afterEach(() => TestBed.resetTestingModule());

  async function render(capabilities = MEMBER_WORKSPACE.capabilities) {
    await TestBed.configureTestingModule({
      imports: [WorkspaceCardComponent],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(WorkspaceCardComponent);
    fixture.componentRef.setInput('workspace', { ...MEMBER_WORKSPACE, capabilities });
    fixture.detectChanges();
    return fixture;
  }

  it('exposes a keyboard-native selection action only with the server capability', async () => {
    const fixture = await render(['openWorkspace']);
    const selected = vi.fn();
    fixture.componentInstance.workspaceSelected.subscribe(selected);
    const button = fixture.nativeElement.querySelector('[data-testid="select-workspace-action"]') as HTMLButtonElement;
    button.click();
    expect(selected).toHaveBeenCalledWith(MEMBER_WORKSPACE.id);

    fixture.componentRef.setInput('workspace', { ...MEMBER_WORKSPACE, capabilities: [] });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="select-workspace-action"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain(MEMBER_WORKSPACE.displayName);
  });

  it('announces selection and prevents a second operation while scope changes', async () => {
    const fixture = await render(['openWorkspace']);
    const selected = vi.fn();
    fixture.componentInstance.workspaceSelected.subscribe(selected);
    fixture.componentRef.setInput('selected', true);
    fixture.componentRef.setInput('selectionPending', true);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('[data-testid="select-workspace-action"]') as HTMLButtonElement;
    expect(button.getAttribute('aria-pressed')).toBe('true');
    expect(button.disabled).toBe(true);
    button.click();
    expect(selected).not.toHaveBeenCalled();
  });
});
