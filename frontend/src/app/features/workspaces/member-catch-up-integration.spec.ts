import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { COGLATAS_AUTH_SESSION_MOCK, DEFAULT_AUTH_SESSION } from '../../core/auth/auth-session.facade';
import { RealtimeFacade } from '../../core/realtime/realtime.facade';
import { ActiveWorkspaceFacade } from '../../core/workspace/active-workspace.facade';
import { AppDataGridComponent } from '../../shared/grid/app-data-grid/app-data-grid.component';
import { AppDataGridActionEvent } from '../../shared/grid/app-data-grid/app-data-grid.types';
import { WorkspaceMemberGridRow } from './members.types';
import { WorkspaceMemberManagementFacade } from './member-management.facade';
import { WorkspaceMembersPageComponent } from './workspace-members-page/workspace-members-page.component';

const WORKSPACE = '00000000-0000-0000-0000-000000000101';
const USER = '00000000-0000-0000-0000-000000000201';
const BASE = `/api/workspaces/${WORKSPACE}/members`;
const OPTIONS = BASE + '/management/options';

@Component({ selector: 'app-data-grid', standalone: true, template: '' })
class DirectoryGridStub {
  @Input() rows: readonly WorkspaceMemberGridRow[] = [];
  @Input() columns: readonly unknown[] = [];
  @Input() defaultPageSize = 0;
  @Input() maximumPageSize = 0;
  @Input() rowIdField = '';
  @Input() ariaLabel = '';
  @Output() actionInvoked = new EventEmitter<AppDataGridActionEvent<WorkspaceMemberGridRow>>();
}

describe('Workspace member read and management catch-up separation', () => {
  let fixture: ComponentFixture<WorkspaceMembersPageComponent>;
  let http: HttpTestingController;
  let management: WorkspaceMemberManagementFacade;
  let callbacks: Map<string, () => Promise<void> | void>;

  beforeEach(async () => {
    window.localStorage.setItem('coglatas.locale', 'en');
    callbacks = new Map();
    const params = convertToParamMap({ workspaceId: WORKSPACE });
    await TestBed.configureTestingModule({ imports: [WorkspaceMembersPageComponent], providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: COGLATAS_AUTH_SESSION_MOCK, useValue: DEFAULT_AUTH_SESSION },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: params }, paramMap: of(params) } },
      { provide: ActiveWorkspaceFacade, useValue: { activeWorkspace: signal({ id: WORKSPACE, label: 'Current Workspace' }) } },
      { provide: RealtimeFacade, useValue: {
        registerProtectedStateClearer: () => () => undefined,
        registerSubscription: () => () => undefined,
        registerCatchUp: (owner: string, callback: () => Promise<void> | void) => {
          callbacks.set(owner, callback);
          return () => callbacks.delete(owner);
        },
      } },
    ] }).overrideComponent(WorkspaceMembersPageComponent, {
      remove: { imports: [AppDataGridComponent] }, add: { imports: [DirectoryGridStub] },
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WorkspaceMembersPageComponent);
    management = TestBed.inject(WorkspaceMemberManagementFacade);
    fixture.detectChanges();
    http.expectOne(OPTIONS).flush({ workspaceId: WORKSPACE, canAddMember: true, target: null });
    http.expectOne(BASE).flush([{ userId: USER, displayName: 'Listed member', role: 'Member' }]);
    fixture.detectChanges();
    const open = fixture.nativeElement.querySelector('[data-testid="member-mobile-action-openMemberDetail"]') as HTMLButtonElement;
    open.click();
    http.expectOne(BASE + '/' + USER).flush({ userId: USER, displayName: 'Old authorized member', role: 'Member' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="workspace-member-detail-name"]')?.textContent).toBe('Old authorized member');
  });

  afterEach(() => {
    try { http.verify(); } finally {
      fixture?.destroy();
      TestBed.resetTestingModule();
      window.localStorage.removeItem('coglatas.locale');
    }
  });

  function refreshMember(): Promise<void> | void {
    const completion = callbacks.get('workspace-members')!();
    callbacks.get('workspace-member-management')!();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="workspace-member-detail"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="workspace-member-detail-name"]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Old authorized member');
    http.expectOne(OPTIONS).flush({ workspaceId: WORKSPACE, canAddMember: true, target: null });
    http.expectOne(BASE).flush([{ userId: USER, displayName: 'New list-only name', role: 'ReadOnly' }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="workspace-member-detail-name"]')).toBeNull();
    http.expectOne(BASE + '/' + USER).flush({ userId: USER, displayName: 'Fresh authorized member', role: 'ReadOnly' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="workspace-member-detail-name"]')?.textContent).toBe('Fresh authorized member');
    return completion;
  }

  it('keeps the selected read panel pending without rendering old names or list-only data as detail authority', async () => {
    await refreshMember();
    expect(fixture.nativeElement.querySelector('[data-testid="workspace-member-detail-role"]')?.textContent).toBe('Read only');
    http.expectNone((request) => request.method !== 'GET');
  });

  it('never restores a protected editor or its confirmation with the independently reauthorized read panel', async () => {
    const change = fixture.nativeElement.querySelector('[data-testid="workspace-member-change-role"]') as HTMLButtonElement;
    change.click();
    http.expectOne((request) => request.url === OPTIONS && request.params.get('userId') === USER).flush({
      workspaceId: WORKSPACE, canAddMember: true,
      target: { userId: USER, currentRole: 3, membershipStatus: 1, canChangeRole: true, canRemove: true, allowedRoles: [3, 4] },
    });
    management.selectRole(4);
    management.confirmIdentity(true);
    expect(management.canSubmit()).toBe(true);
    await refreshMember();
    expect(management.editor()).toBeNull();
    expect(management.canSubmit()).toBe(false);
    expect(fixture.nativeElement.querySelector('[data-testid="workspace-member-editor"]')).toBeNull();
    http.expectNone((request) => request.method !== 'GET');
  });
});
