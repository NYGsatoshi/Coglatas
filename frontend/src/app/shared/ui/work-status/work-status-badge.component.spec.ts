import { TestBed } from '@angular/core/testing';

import { WorkStatusBadgeComponent } from './work-status-badge.component';

describe('WorkStatusBadgeComponent', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => window.localStorage.removeItem('coglatas.locale'));
  it('presents cancelled as a truthful icon-and-text status', () => {
    TestBed.configureTestingModule({ imports: [WorkStatusBadgeComponent] });
    const fixture = TestBed.createComponent(WorkStatusBadgeComponent);
    fixture.componentRef.setInput('status', 'cancelled');
    fixture.detectChanges();

    const badge = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.work-status');
    expect(badge?.getAttribute('data-work-status')).toBe('cancelled');
    expect(badge?.getAttribute('aria-label')).toBe('Status: Cancelled');
    expect(badge?.textContent).toContain('Cancelled');
    expect(badge?.querySelector('svg')).not.toBeNull();
  });
});
