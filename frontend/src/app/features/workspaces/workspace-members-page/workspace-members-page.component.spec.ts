import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';

import { AppDataGridComponent } from '../../../shared/grid/app-data-grid/app-data-grid.component';
import {
  AppDataGridActionEvent,
  AppDataGridColumnDef,
  clampAppDataGridPageSize
} from '../../../shared/grid/app-data-grid/app-data-grid.types';
import { COGLATAS_WORKSPACE_MEMBERS_MOCK, WorkspaceMembersFacade } from '../members.facade';
import {
  WORKSPACE_MEMBERS_OTHER_WORKSPACE_ID,
  WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID,
  WORKSPACE_MEMBERS_SCENARIOS,
} from '../members.mock';
import { WorkspaceMemberGridRow, WorkspaceMembersScenario } from '../members.types';
import { WorkspaceMembersPageComponent } from './workspace-members-page.component';
import { ActiveWorkspaceFacade } from '../../../core/workspace/active-workspace.facade';

@Component({
  selector: 'app-data-grid',
  standalone: true,
  imports: [FormsModule],
  template: `
    <section data-testid="app-data-grid">
      <p data-testid="stub-page-size">{{ defaultPageSize }}/{{ maximumPageSize }}</p>
      @for (row of rows; track row.id) {
        <article data-testid="member-row">
          <span data-testid="member-display-name">{{ row.displayName }}</span>
          <span>{{ row.roleLabel }}</span>
          <span>{{ row.groupProjectLabel }}</span>
          <span>{{ row.accountStatusLabel }}</span>
          <span>{{ row.joinedAtLabel }}</span>
          @for (action of row.rowActions; track action.id) {
            <button
              type="button"
              [attr.data-testid]="'member-action-' + action.id"
              [attr.aria-disabled]="action.disabled"
              [attr.title]="action.disabledReason ?? null"
              (click)="emitAction(action.id, row)"
            >
              {{ action.label }}
            </button>
          }
        </article>
      }
    </section>
  `
})
class StubDataGridComponent {
  @Input() rows: readonly WorkspaceMemberGridRow[] = [];
  @Input() columns: readonly AppDataGridColumnDef<WorkspaceMemberGridRow>[] = [];
  @Input() defaultPageSize = 0;
  @Input() maximumPageSize = 0;
  @Input() rowIdField = 'id';
  @Input() ariaLabel = '';
  @Output() actionInvoked = new EventEmitter<AppDataGridActionEvent<WorkspaceMemberGridRow>>();

  emitAction(actionId: string, row: WorkspaceMemberGridRow): void {
    this.actionInvoked.emit({ actionId, row });
  }
}

const routeStub = (workspaceId = WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID) => ({
  snapshot: {
    paramMap: convertToParamMap({ workspaceId })
  },
  paramMap: of(convertToParamMap({ workspaceId })),
});

const renderMembers = async (
  scenario: WorkspaceMembersScenario = WORKSPACE_MEMBERS_SCENARIOS.default,
  workspaceId = WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID
): Promise<ComponentFixture<WorkspaceMembersPageComponent>> => {
  await TestBed.configureTestingModule({
    imports: [WorkspaceMembersPageComponent],
    providers: [
      { provide: ActivatedRoute, useValue: routeStub(workspaceId) },
      { provide: COGLATAS_WORKSPACE_MEMBERS_MOCK, useValue: scenario }
    ]
  })
    .overrideComponent(WorkspaceMembersPageComponent, {
      remove: { imports: [AppDataGridComponent] },
      add: { imports: [StubDataGridComponent] }
    })
    .compileComponents();

  const fixture = TestBed.createComponent(WorkspaceMembersPageComponent);
  fixture.detectChanges();
  return fixture;
};

const textContent = (fixture: ComponentFixture<WorkspaceMembersPageComponent>): string =>
  (fixture.nativeElement as HTMLElement).textContent ?? '';

const query = <T extends HTMLElement>(fixture: ComponentFixture<WorkspaceMembersPageComponent>, selector: string): T | null =>
  (fixture.nativeElement as HTMLElement).querySelector<T>(selector);

const queryAll = <T extends HTMLElement>(fixture: ComponentFixture<WorkspaceMembersPageComponent>, selector: string): T[] =>
  Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<T>(selector));

describe('WorkspaceMembersPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('does not directly depend on AgGridAngular or enterprise grid packages', () => {
    const dependencies = ((WorkspaceMembersPageComponent as unknown as { ɵcmp: { dependencies?: unknown[] } }).ɵcmp
      .dependencies ?? []) as Array<{ name?: string; type?: { name?: string } }>;
    const dependencyNames = dependencies.map((dependency) => dependency.type?.name ?? dependency.name ?? '');
    const enterprisePackageName = 'ag-grid' + '-enterprise';

    expect(dependencyNames).not.toContain('AgGridAngular');
    expect(dependencyNames.join(' ')).not.toContain(enterprisePackageName);
  });

  it('renders the shared grid wrapper for ready mock rows', async () => {
    const fixture = await renderMembers();

    expect(query(fixture, '[data-testid="app-data-grid"]')).not.toBeNull();
    expect(queryAll(fixture, '[data-testid="member-row"]').length).toBeGreaterThan(0);
  });

  it('does not render email addresses', async () => {
    const fixture = await renderMembers();

    expect(textContent(fixture)).not.toMatch(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i);
  });

  it('does not render members from another workspace', async () => {
    const fixture = await renderMembers();

    expect(textContent(fixture)).not.toContain('別ワークスペース参加者');
    expect(textContent(fixture)).not.toContain('非表示領域');
  });

  it('loads the distinct Workspace when a reused members route changes from A to B', async () => {
    const routeParams = new BehaviorSubject(
      convertToParamMap({ workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID }),
    );
    await TestBed.configureTestingModule({
      imports: [WorkspaceMembersPageComponent],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({
                workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID,
              }),
            },
            paramMap: routeParams.asObservable(),
          },
        },
        {
          provide: COGLATAS_WORKSPACE_MEMBERS_MOCK,
          useValue: WORKSPACE_MEMBERS_SCENARIOS.default,
        },
      ],
    })
      .overrideComponent(WorkspaceMembersPageComponent, {
        remove: { imports: [AppDataGridComponent] },
        add: { imports: [StubDataGridComponent] },
      })
      .compileComponents();
    const facade = TestBed.inject(WorkspaceMembersFacade);
    const activeWorkspace = TestBed.inject(ActiveWorkspaceFacade);
    activeWorkspace.setActiveWorkspace({
      id: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID,
      label: 'Workspace A',
    });
    const ensureLoaded = vi.spyOn(facade, 'ensureLoaded');
    const fixture = TestBed.createComponent(WorkspaceMembersPageComponent);
    fixture.detectChanges();

    expect(ensureLoaded).toHaveBeenCalledTimes(1);
    expect(ensureLoaded).toHaveBeenLastCalledWith(WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID);

    routeParams.next(convertToParamMap({ workspaceId: WORKSPACE_MEMBERS_PRIMARY_WORKSPACE_ID }));
    expect(ensureLoaded).toHaveBeenCalledTimes(1);

    routeParams.next(convertToParamMap({ workspaceId: WORKSPACE_MEMBERS_OTHER_WORKSPACE_ID }));
    fixture.detectChanges();

    expect(ensureLoaded).toHaveBeenCalledTimes(1);

    activeWorkspace.setActiveWorkspace({
      id: WORKSPACE_MEMBERS_OTHER_WORKSPACE_ID,
      label: 'Workspace B',
    });
    TestBed.flushEffects();
    fixture.detectChanges();

    expect(ensureLoaded).toHaveBeenCalledTimes(2);
    expect(ensureLoaded).toHaveBeenLastCalledWith(WORKSPACE_MEMBERS_OTHER_WORKSPACE_ID);
    expect(fixture.componentInstance.workspaceId).toBe(WORKSPACE_MEMBERS_OTHER_WORKSPACE_ID);
    expect(fixture.componentInstance.vm().workspaceId).toBe(WORKSPACE_MEMBERS_OTHER_WORKSPACE_ID);
    expect(fixture.componentInstance.vm().rows.map((row) => row.id)).toEqual([
      'member-hidden-other-001',
    ]);
  });

  it('enforces the maximum page size at 100', async () => {
    const fixture = await renderMembers(WORKSPACE_MEMBERS_SCENARIOS.manyRowsBoundedPage);

    expect(query(fixture, '[data-testid="stub-page-size"]')?.textContent).toContain('50/100');
    expect(clampAppDataGridPageSize(250, 500)).toBe(100);
  });

  it('capability-filters row actions before rendering', async () => {
    const fixture = await renderMembers(WORKSPACE_MEMBERS_SCENARIOS.noRoleChangeCapability);

    expect(query(fixture, '[data-testid="member-action-changeRole"]')).toBeNull();
    expect(query(fixture, '[data-testid="member-action-openMemberDetail"]')).not.toBeNull();
  });

  it('hides unbound admin and destructive member actions in MVP0', async () => {
    const fixture = await renderMembers();

    expect(query(fixture, '[data-testid="member-action-changeRole"]')).toBeNull();
    expect(query(fixture, '[data-testid="member-action-disableMember"]')).toBeNull();
    expect(query(fixture, '[data-testid="member-action-openMemberDetail"]')).not.toBeNull();
  });

  it('client search filters only the provided authorized rows', async () => {
    const fixture = await renderMembers();

    fixture.componentInstance.updateSearch('別ワークスペース参加者');
    fixture.detectChanges();
    expect(queryAll(fixture, '[data-testid="member-row"]').length).toBe(0);

    fixture.componentInstance.updateSearch('サンプル参加者 02');
    fixture.detectChanges();
    expect(queryAll(fixture, '[data-testid="member-row"]').length).toBe(1);
    expect(textContent(fixture)).toContain('サンプル参加者 02');
  });

  it('mobile layout does not expose hidden actions', async () => {
    const fixture = await renderMembers(WORKSPACE_MEMBERS_SCENARIOS.noRoleChangeCapability);

    (fixture.nativeElement as HTMLElement).style.width = '320px';
    fixture.detectChanges();

    expect(query(fixture, '[data-testid="member-action-changeRole"]')).toBeNull();
  });
});
