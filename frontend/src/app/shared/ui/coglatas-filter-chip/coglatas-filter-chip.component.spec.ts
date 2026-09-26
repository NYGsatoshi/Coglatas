import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CoglatasFilterChipComponent } from './coglatas-filter-chip.component';

describe('CoglatasFilterChipComponent', () => {
  let fixture: ComponentFixture<CoglatasFilterChipComponent>;

  beforeEach(async () => {
    window.localStorage.setItem('coglatas.locale', 'en');
    await TestBed.configureTestingModule({ imports: [CoglatasFilterChipComponent] }).compileComponents();
    fixture = TestBed.createComponent(CoglatasFilterChipComponent);
    fixture.componentRef.setInput('label', 'Type');
    fixture.componentRef.setInput('value', 'PDF');
    fixture.detectChanges();
  });

  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('exposes a native keyboard-operable removal action with its filter context', () => {
    const removed = vi.fn();
    fixture.componentInstance.removed.subscribe(removed);
    const button = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;

    expect(button.getAttribute('aria-label')).toBe('Remove filter Type: PDF');
    button.click();
    expect(removed).toHaveBeenCalledOnce();
  });
});
