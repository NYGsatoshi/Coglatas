import { signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { vi } from 'vitest';

import { FilesFacade } from '../../files/files.facade';
import { ProjectsFacade } from '../projects.facade';
import { TaskDetailPageComponent } from './task-detail-page.component';

describe('TaskDetailPageComponent local edit state', () => {
  let fixture: ComponentFixture<TaskDetailPageComponent>;
  let component: TaskDetailPageComponent;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let sections: WritableSignal<Record<string, { status: string; message?: string }>>;
  const setDetailEditing = vi.fn();
  const reloadTaskAfterConflict = vi.fn();
  const loadActivity = vi.fn();

  beforeEach(async () => {
    params = new BehaviorSubject(convertToParamMap({ projectId: 'project-a', taskId: 'task-a' }));
    sections = signal({ detail: { status: 'idle' }, activity: { status: 'idle' }, subtasks: { status: 'idle' }, checklist: { status: 'idle' }, comments: { status: 'idle' }, labels: { status: 'idle' }, watch: { status: 'idle' }, files: { status: 'idle' } });
    await TestBed.configureTestingModule({
      imports: [TaskDetailPageComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
        { provide: ProjectsFacade, useValue: {
          getTaskDetail: () => ({ status: 'empty', detailState: 'ready', detailSectionState: sections()['detail'], dependencies: [], capabilities: [], transitionNote: { owner: 'backendAuthoritativeDuringApiWiring', message: '' } }),
          getTaskMutationState: () => ({ status: 'idle' }), getTaskConflictReloadState: () => 'idle', getDetailSectionState: (section: string) => sections()[section],
          setDetailEditing, ensureTaskDetail: vi.fn(), releaseTaskDetail: vi.fn(), clearTaskMutationState: vi.fn(), reloadTaskAfterConflict, loadActivity
        } },
        { provide: FilesFacade, useValue: { pickerStateForTask: signal({ status: 'idle', workspaceId: null, files: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false }), clearPickerFiles: vi.fn(), cancelAttachmentDownloads: vi.fn(), loadPickerFilesForWorkspace: vi.fn(), loadMorePickerFiles: vi.fn(), retryPickerFiles: vi.fn() } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(TaskDetailPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    setDetailEditing.mockClear();
    loadActivity.mockClear();
  });

  it('notifies the facade while a comment has an unsaved value', () => {
    component.commentBody.set('Draft comment');
    fixture.detectChanges();
    expect(setDetailEditing).toHaveBeenLastCalledWith(true);
  });

  it('clears local drafts and editing state when the task route changes', () => {
    component.commentBody.set('Draft comment');
    fixture.detectChanges();
    params.next(convertToParamMap({ projectId: 'project-a', taskId: 'task-b' }));
    fixture.detectChanges();
    expect(component.commentBody()).toBe('');
    expect(setDetailEditing).toHaveBeenLastCalledWith(false);
  });

  it('treats an empty edited comment and an important-only edit as dirty, then resets it on cancel', () => {
    component.editComment({ id: 'comment-1', body: 'Original', isImportant: false });
    expect(component.detailEditing()).toBe(false);
    component.editingCommentBody.set('');
    expect(component.detailEditing()).toBe(true);
    component.editingCommentBody.set('Original');
    component.editingCommentImportant.set(true);
    expect(component.detailEditing()).toBe(true);
    component.editingCommentImportant.set(false);
    expect(component.detailEditing()).toBe(false);
    component.cancelEditComment();
    expect(component.detailEditing()).toBe(false);
  });

  it('resets original comment state when the task route changes', () => {
    component.editComment({ id: 'comment-1', body: 'Original', isImportant: false });
    component.editingCommentBody.set('Changed');
    expect(component.detailEditing()).toBe(true);
    params.next(convertToParamMap({ projectId: 'project-a', taskId: 'task-b' }));
    fixture.detectChanges();
    expect(component.editingCommentId()).toBeNull();
    expect(component.detailEditing()).toBe(false);
  });

  it('delegates an explicit conflict reload without changing ordinary cancel behavior', () => {
    component.reloadTaskAfterConflict();
    expect(reloadTaskAfterConflict).toHaveBeenCalledWith('task-a');
  });

  it('maps canonical Stage categories to phase states without inventing Failed', () => {
    expect(component.phaseStateLabel('todo', 'notStarted', false)).toBe('Waiting');
    expect(component.phaseStateLabel('inProgress', 'inProgress', false)).toBe('Running');
    expect(component.phaseStateLabel('review', 'review', false)).toBe('Needs review');
    expect(component.phaseStateLabel('done', 'done', false)).toBe('Completed');
    expect(component.phaseStateLabel('cancelled', 'cancelled', false)).toBe('Cancelled');
    expect(component.phaseStateLabel('inProgress', 'inProgress', true)).toBe('Blocked');
    expect(component.activityTypeLabel('issue')).toBe('Needs attention');
    expect(component.activityTypeLabel('statusUpdate')).toBe('Status update');
  });

  it('shows the Activity empty message only after a successful confirmed-empty response', () => {
    for (const status of ['idle', 'loading', 'error', 'permissionDenied', 'conflict']) {
      sections.update(current => ({ ...current, activity: { status, message: 'Activity unavailable.' } }));
      expect(component.activityHasConfirmedEmptyState()).toBe(false);
    }

    sections.update(current => ({ ...current, activity: { status: 'empty' } }));
    expect(component.activityHasConfirmedEmptyState()).toBe(true);
  });

  it('loads Activity from summary activation without depending on a details toggle event', () => {
    component.loadActivity();

    expect(loadActivity).toHaveBeenCalledWith('task-a');
  });

  it('localizes task-file metadata and backend status values without exposing internal values', () => {
    const storedLocale = window.localStorage.getItem('coglatas.locale');
    try {
      component.i18n.setLocale('ja');

      expect(component.taskFileMetadataLabel({ contentType: 'application/pdf', sizeBytes: 2 * 1024 })).toBe('PDF・2 KB');
      expect(component.taskFileScanLabel('Clean')).toBe('スキャン: 利用可');
      expect(component.taskFileScanLabel('Infected')).toBe('スキャン: ブロック済み');
      expect(component.taskFileAccessLabel('AccessRevoked')).toBe('アクセス: アクセス権がありません');
      expect(component.taskFileRestrictionLabel('ACCESS_REVOKED')).toBe('アクセス権がありません');
      expect(component.taskFileSectionMessage({ status: 'permissionDenied', message: 'Permission was denied.' })).toBe('この操作を行う権限がありません。');
      expect(component.taskFileSectionMessage({ status: 'error', message: 'Task command failed.' })).toBe('タスクのファイルを更新できませんでした。');
    } finally {
      component.i18n.setLocale(storedLocale === 'en' ? 'en' : 'ja');
      if (storedLocale === null) {window.localStorage.removeItem('coglatas.locale');}
    }
  });

  it('notifies the facade that editing ended on destroy', () => {
    component.commentBody.set('Draft comment');
    fixture.detectChanges();
    fixture.destroy();
    expect(setDetailEditing).toHaveBeenLastCalledWith(false);
  });
});
