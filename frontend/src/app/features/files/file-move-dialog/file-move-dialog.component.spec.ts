import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { I18nService } from '../../../core/i18n/i18n.service';
import { FileFolderStore } from '../file-folders.service';
import { FileMoveDialogComponent } from './file-move-dialog.component';

describe('File Move locale presentation', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'ja'));
  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('retains destination and failed command state while switching the mounted dialog', async () => {
    const operation = new Subject<void>();
    const moveFile = vi.fn(() => operation);
    await TestBed.configureTestingModule({
      imports: [FileMoveDialogComponent],
      providers: [
        {
          provide: FileFolderStore,
          useValue: {
            folders: signal([
              {
                id: 'folder-1',
                workspaceId: 'workspace',
                parentFolderId: null,
                name: 'Folders',
                sortOrder: 0,
                version: 1,
              },
            ]),
            loading: signal(false),
            failed: signal(false),
            moveFile,
          },
        },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(FileMoveDialogComponent);
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('fileObjectIds', ['file-1']);
    fixture.detectChanges();
    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = 'folder-1';
    select.dispatchEvent(new Event('change'));
    fixture.componentInstance.confirmMove();
    operation.error(new Error('server detail must not be displayed'));
    fixture.detectChanges();
    expect(fixture.componentInstance.errorMessage()).toContain('移動できませんでした');
    TestBed.inject(I18nService).setLocale('en');
    fixture.detectChanges();
    expect(fixture.componentInstance.errorMessage()).toContain('could not be moved');
    expect(fixture.componentInstance.destinationFolderId()).toBe('folder-1');
    expect(select.selectedOptions[0].textContent).toBe('Folders');
    expect(fixture.nativeElement.textContent).not.toContain('server detail');
    expect(moveFile).toHaveBeenCalledExactlyOnceWith('file-1', 'folder-1');
  });
});
