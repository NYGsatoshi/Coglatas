import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { FileBrowserSidebarComponent } from './file-browser-sidebar.component';

describe('FileBrowserSidebarComponent', () => {
  let fixture: ComponentFixture<FileBrowserSidebarComponent>;
  beforeEach(async () => {
    window.localStorage.setItem('coglatas.locale', 'ja');
    await TestBed.configureTestingModule({ imports: [FileBrowserSidebarComponent] }).compileComponents();
    fixture = TestBed.createComponent(FileBrowserSidebarComponent);
    fixture.componentInstance.folders = [{ id: 'one', name: 'One', children: [
      { id: 'two', name: 'Two', children: [{ id: 'three', name: 'Three', children: [] }] },
    ] }];
    fixture.detectChanges();
  });

  afterEach(() => window.localStorage.removeItem('coglatas.locale'));

  it('shows only the first two levels initially and exposes independent shortcuts', () => {
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('[role="treeitem"]')).toHaveLength(2);
    expect([...element.querySelectorAll('.browser__shortcuts button')].map((button) => button.textContent?.trim()))
      .toEqual(['最近のファイル', 'スター付きのファイル', '共有されたファイル']);
    expect(element.querySelector('aside')?.getAttribute('aria-label')).toBe('ファイル ブラウザー');
  });

  it('expands and traverses with the keyboard without changing selection', () => {
    const items = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="treeitem"]')];
    items()[1].focus();
    items()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    expect(items()).toHaveLength(3);
    expect(items()[1].getAttribute('aria-selected')).toBe('false');
    items()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    fixture.detectChanges();
    expect(fixture.componentInstance.focusedId()).toBe('three');
  });

  it('keeps focus and selection as independent visual states', () => {
    fixture.componentInstance.selectedFolderId = 'one';
    fixture.componentInstance.focusedId.set('two');
    fixture.detectChanges();
    const items = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="treeitem"]')];
    expect(items[0].classList.contains('is-selected')).toBe(true);
    expect(items[0].classList.contains('is-focused')).toBe(false);
    expect(items[1].classList.contains('is-focused')).toBe(true);
  });

  it('renders folders delivered asynchronously after the initial empty tree', async () => {
    fixture.destroy();
    fixture = TestBed.createComponent(FileBrowserSidebarComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('[role="treeitem"]')).toHaveLength(0);

    await Promise.resolve();
    fixture.componentRef.setInput('folders', [{ id: 'destination', name: 'Destination', children: [] }]);
    fixture.detectChanges();

    expect([...element.querySelectorAll('.browser__folder')].map((button) => button.textContent?.trim()))
      .toEqual(['Destination']);
    expect(element.querySelector('[role="treeitem"]')?.getAttribute('tabindex')).toBe('0');
    expect(element.querySelector('.browser__empty')).toBeNull();
  });

  it('replaces folder metadata while retaining expansion, selection and keyboard focus', async () => {
    const element = fixture.nativeElement as HTMLElement;
    const items = () => [...element.querySelectorAll<HTMLElement>('[role="treeitem"]')];
    fixture.componentRef.setInput('selectedFolderId', 'one');
    items()[1].focus();
    items()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();

    fixture.componentRef.setInput('folders', [{ id: 'one', name: 'One updated', children: [
      { id: 'two', name: 'Two updated', children: [{ id: 'three', name: 'Three updated', children: [] }] },
    ] }, { id: 'four', name: 'Four', children: [] }]);
    fixture.detectChanges();

    expect([...element.querySelectorAll('.browser__folder')].map((button) => button.textContent?.trim()))
      .toEqual(['One updated', 'Two updated', 'Three updated', 'Four']);
    expect(items()[0].getAttribute('aria-selected')).toBe('true');
    expect(items()[1].getAttribute('aria-expanded')).toBe('true');
    expect(items()[1].classList.contains('is-focused')).toBe(true);
    expect(document.activeElement).toBe(items()[1]);

    items()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    fixture.detectChanges();
    await Promise.resolve();
    expect(fixture.componentInstance.focusedId()).toBe('four');
    expect(document.activeElement).toBe(items()[3]);
  });

  it('removes protected folder DOM when authorization clears the input', () => {
    const element = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('folders', []);
    fixture.detectChanges();

    expect(element.querySelectorAll('[role="treeitem"]')).toHaveLength(0);
    expect(element.querySelectorAll('.browser__folder')).toHaveLength(0);
    expect(element.querySelectorAll('.browser__move')).toHaveLength(0);
    expect(element.querySelector('.browser__empty')).not.toBeNull();
    expect(element.textContent).not.toContain('One');
    expect(element.textContent).not.toContain('Two');
  });
});
