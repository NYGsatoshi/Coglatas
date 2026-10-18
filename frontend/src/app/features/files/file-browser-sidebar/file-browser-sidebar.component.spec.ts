import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { FileBrowserFolderNode, FileBrowserSidebarComponent } from './file-browser-sidebar.component';

describe('FileBrowserSidebarComponent', () => {
  let fixture: ComponentFixture<FileBrowserSidebarComponent>;
  const sidebar = {
    async expectKeyboardEndFocus(): Promise<void> {
      sidebar.pressTreeKey(1, 'End');
      await fixture.whenStable();
      expect(fixture.componentInstance.focusedId()).toBe('four');
      expect(document.activeElement).toBe(sidebar.treeItems()[3]);
    },
    expectRetainedFolderState(): void {
      expect(sidebar.treeItems()[0].getAttribute('aria-selected')).toBe('true');
      expect(sidebar.treeItems()[1].getAttribute('aria-expanded')).toBe('true');
      expect(sidebar.treeItems()[1].classList.contains('is-focused')).toBe(true);
      expect(document.activeElement).toBe(sidebar.treeItems()[1]);
    },
    folderNames(): readonly (string | undefined)[] {
      const names: (string | undefined)[] = [];
      for (const item of sidebar.treeItems()) {
        names.push(item.querySelector('.browser__folder')?.textContent?.trim());
      }
      return names;
    },
    pressTreeKey(index: number, key: string): void {
      sidebar.treeItems()[index].dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key }));
      fixture.detectChanges();
    },
    setFolders(folders: readonly FileBrowserFolderNode[]): void {
      fixture.componentRef.setInput('folders', folders);
      fixture.detectChanges();
    },
    treeItems(): readonly HTMLElement[] {
      return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="treeitem"]')];
    },
  };
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
    expect(element.querySelector('[role="tree"]')).toBeNull();
    expect(element.querySelector('[role="group"]')).not.toBeNull();

    await Promise.resolve();
    sidebar.setFolders([{ children: [], id: 'destination', name: 'Destination' }]);

    expect(sidebar.folderNames()).toEqual(['Destination']);
    expect(element.querySelector('[role="tree"]')).not.toBeNull();
    expect(element.querySelector('[role="treeitem"]')?.getAttribute('tabindex')).toBe('0');
    expect(element.querySelector('.browser__empty')).toBeNull();
  });

  it('replaces folder metadata while retaining expansion, selection and keyboard focus', async () => {
    fixture.componentRef.setInput('selectedFolderId', 'one');
    sidebar.treeItems()[1].focus();
    sidebar.pressTreeKey(1, 'ArrowRight');

    sidebar.setFolders([{ children: [
      { children: [{ children: [], id: 'three', name: 'Three updated' }], id: 'two', name: 'Two updated' },
    ], id: 'one', name: 'One updated' }, { children: [], id: 'four', name: 'Four' }]);

    expect(sidebar.folderNames()).toEqual(['One updated', 'Two updated', 'Three updated', 'Four']);
    sidebar.expectRetainedFolderState();

    await sidebar.expectKeyboardEndFocus();
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

  it('reports default root expansion truthfully and lets the keyboard collapse and reopen it', async () => {
    expect(sidebar.treeItems()[0].getAttribute('aria-expanded')).toBe('true');
    sidebar.treeItems()[0].focus();
    sidebar.pressTreeKey(0, 'ArrowLeft');
    expect(sidebar.treeItems()).toHaveLength(1);
    expect(sidebar.treeItems()[0].getAttribute('aria-expanded')).toBe('false');
    sidebar.pressTreeKey(0, 'ArrowRight');
    expect(sidebar.treeItems()).toHaveLength(2);
    expect(sidebar.treeItems()[0].getAttribute('aria-expanded')).toBe('true');
    sidebar.pressTreeKey(0, 'ArrowRight');
    await fixture.whenStable();
    expect(document.activeElement).toBe(sidebar.treeItems()[1]);
  });

  it('restores the tree tab stop and focus when refreshed metadata removes the focused folder', async () => {
    sidebar.treeItems()[1].focus();
    sidebar.setFolders([{ id: 'one', name: 'One', children: [] }]);
    await fixture.whenStable();
    expect(sidebar.treeItems()).toHaveLength(1);
    expect(sidebar.treeItems()[0].getAttribute('tabindex')).toBe('0');
    expect(document.activeElement).toBe(sidebar.treeItems()[0]);
    expect(fixture.componentInstance.focusedId()).toBe('one');
    expect(fixture.componentInstance.expandedIds().has('two')).toBe(false);
  });

  it('clears retained folder identities on authorization reset before a new hierarchy arrives', () => {
    sidebar.treeItems()[1].focus();
    sidebar.pressTreeKey(1, 'ArrowRight');
    sidebar.setFolders([]);
    expect(fixture.componentInstance.focusedId()).toBeNull();
    expect([...fixture.componentInstance.expandedIds()]).toEqual([]);
    sidebar.setFolders([{ id: 'other', name: 'Other', children: [] }]);
    expect(sidebar.treeItems()[0].getAttribute('tabindex')).toBe('0');
    expect(fixture.componentInstance.focusedId()).toBe('other');
  });

  it('does not intercept keyboard activation of the independent Move button', () => {
    const onSelect = vi.fn();
    const onMove = vi.fn();
    fixture.componentInstance.folderSelected.subscribe(onSelect);
    fixture.componentInstance.folderMoveRequested.subscribe(onMove);
    const move = sidebar.treeItems()[0].querySelector<HTMLButtonElement>('.browser__move')!;
    const event = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    move.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
    expect(onSelect).not.toHaveBeenCalled();
    move.click();
    expect(onMove).toHaveBeenCalledExactlyOnceWith('one');
  });

  it('distinguishes loading and failed folder reads from an authorized empty list and offers retry', () => {
    sidebar.setFolders([]);
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[role="status"]')?.textContent).toContain('フォルダーを読み込んでいます');
    expect(host.textContent).not.toContain('フォルダーがありません');
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('failed', true);
    fixture.detectChanges();
    const onRetry = vi.fn();
    fixture.componentInstance.retryRequested.subscribe(onRetry);
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('アクセスできません');
    expect(host.textContent).not.toContain('フォルダーがありません');
    host.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    expect(onRetry).toHaveBeenCalledOnce();
  });
});
