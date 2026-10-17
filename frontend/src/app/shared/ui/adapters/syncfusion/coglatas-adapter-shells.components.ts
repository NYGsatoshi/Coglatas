import { injectProjectsI18n } from '../../../../core/i18n/providers/projects-i18n';
import { PROJECTS_ENGLISH_MESSAGES } from '../../../../core/i18n/messages/projects.messages';
import { A11yModule } from '@angular/cdk/a11y';
import { NgTemplateOutlet } from '@angular/common';
import {
  AfterRenderRef,
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ComponentRef,
  Directive,
  ElementRef,
  EventEmitter,
  Input,
  Injector,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
  ViewContainerRef,
  afterNextRender,
  inject,
  signal
} from '@angular/core';

import {
  CoglatasAdapterPresentation,
  CoglatasAdapterState,
  CoglatasDataGridContract,
  CoglatasDateTimePickerContract,
  CoglatasDialogContract,
  CoglatasFileUploaderContract,
  CoglatasGanttContract,
  CoglatasGanttDependency,
  CoglatasGanttEditIntent,
  CoglatasGanttItem,
  CoglatasGanttWarning,
  CoglatasKanbanContract,
  CoglatasKanbanMoveRequest,
  CoglatasSchedulerContract,
  CoglatasTreeGridContract
} from '../../contracts/coglatas-complex-adapter.contracts';
import { CoglatasAdapterShellComponent } from './coglatas-adapter-shell.component';
import type { SyncfusionGanttComponent } from './syncfusion-gantt.component';

@Directive()
abstract class CoglatasAdapterShellInput {
  @Input() presentation: CoglatasAdapterPresentation = 'desktop';
  @Input() state: CoglatasAdapterState = 'ready';
}

@Component({ selector: 'coglatas-data-grid', standalone: true, imports: [CoglatasAdapterShellComponent], template: '<coglatas-adapter-shell adapter="data-grid" [ariaLabel]="contract.ariaLabel" [presentation]="presentation" [state]="state" label="Data grid fallback" />', changeDetection: ChangeDetectionStrategy.OnPush })
export class CoglatasDataGridComponent extends CoglatasAdapterShellInput { @Input({ required: true }) contract!: CoglatasDataGridContract<object>; }

@Component({ selector: 'coglatas-dialog', standalone: true, imports: [CoglatasAdapterShellComponent], template: '<coglatas-adapter-shell adapter="dialog" [ariaLabel]="contract.ariaLabel" [presentation]="presentation" [state]="state" label="Dialog fallback" />', changeDetection: ChangeDetectionStrategy.OnPush })
export class CoglatasDialogComponent extends CoglatasAdapterShellInput { @Input({ required: true }) contract!: CoglatasDialogContract; }

@Component({ selector: 'coglatas-file-uploader', standalone: true, imports: [CoglatasAdapterShellComponent], template: '<coglatas-adapter-shell adapter="file-uploader" [ariaLabel]="contract.ariaLabel" [presentation]="presentation" [state]="state" label="File uploader fallback" />', changeDetection: ChangeDetectionStrategy.OnPush })
export class CoglatasFileUploaderComponent extends CoglatasAdapterShellInput { @Input({ required: true }) contract!: CoglatasFileUploaderContract; }

@Component({ selector: 'coglatas-date-time-picker', standalone: true, imports: [CoglatasAdapterShellComponent], template: '<coglatas-adapter-shell adapter="date-time-picker" [ariaLabel]="contract.ariaLabel" [presentation]="presentation" [state]="state" label="Date and time picker fallback" />', changeDetection: ChangeDetectionStrategy.OnPush })
export class CoglatasDateTimePickerComponent extends CoglatasAdapterShellInput { @Input({ required: true }) contract!: CoglatasDateTimePickerContract; }

@Component({
  selector: 'coglatas-kanban', standalone: true, imports: [CoglatasAdapterShellComponent],
  template: `<coglatas-adapter-shell adapter="kanban" [ariaLabel]="contract.ariaLabel" [presentation]="presentation" [state]="state" [label]="i18n.translate('projects.copy.kanban')">
    <p class="coglatas-kanban__feedback" aria-live="polite" role="status">{{ contract.feedback }}</p>
    <div class="coglatas-kanban" [class.coglatas-kanban--narrow]="presentation === 'narrow'" data-testid="coglatas-kanban-board">
      @for (column of contract.columns; track column.id) {
        <section class="coglatas-kanban__column" [attr.aria-label]="i18n.translate('projects.copy.stageCountCards', {stage: column.label, count: i18n.formatNumber(column.cardCount)})" (dragover)="allowDrop($event)" (drop)="dropAtEnd(column.id, $event)">
          <header><h3>{{ column.label }}</h3><span>{{ i18n.translate('projects.copy.countCards', {count: i18n.formatNumber(column.cardCount)}) }}</span></header>
          @if (column.hasWipWarning) {
            <p class="coglatas-kanban__warning" role="status">{{ i18n.translate('projects.copy.warningWIPLimitCountExceeded', {count: i18n.formatNumber(column.wipWarningLimit ?? 0)}) }}</p>
          }
          @let columnItems = itemsFor(column.id);
          <ul>
            @for (item of columnItems; track contract.itemIdentity(item); let itemIndex = $index) {
              @if (startsSwimlane(columnItems, itemIndex)) {
                <li class="coglatas-kanban__swimlane" role="presentation"><h4>{{ swimlaneLabel(item) }}</h4></li>
              }
              <li
                class="coglatas-kanban__card"
                tabindex="0"
                [attr.data-kanban-card-id]="contract.itemIdentity(item)"
                [attr.aria-label]="i18n.translate('projects.copy.titleCurrentStageStage', {title: contract.itemTitle(item), stage: column.label})"
                [class.coglatas-kanban__card--busy]="contract.busyItemId === contract.itemIdentity(item)"
                [draggable]="canDrag(item)"
                (dragstart)="startDrag(item)"
                (dragend)="endDrag()"
                (dragover)="allowDrop($event)"
                (drop)="dropBefore(item, column.id, $event)">
                @if (contract.itemKindLabel) { <span class="coglatas-kanban__kind">{{ contract.itemKindLabel(item) }}</span> }
                <strong>{{ contract.itemTitle(item) }}</strong>
                @if (contract.itemDescription) { <span>{{ contract.itemDescription(item) }}</span> }
                @if (contract.itemMetadata) {
                  <ul class="coglatas-kanban__metadata" [attr.aria-label]="i18n.translate('projects.copy.taskIndicators')">
                    @for (metadata of contract.itemMetadata(item); track metadata) { <li>{{ metadata }}</li> }
                  </ul>
                }
                <div class="coglatas-kanban__actions">
                  @if (contract.canOpenItem(item)) { <button type="button" (click)="activate(item)">{{ i18n.translate('projects.copy.openDetails') }}</button> }
                  @if (contract.canMoveItem(item) && contract.busyItemId !== contract.itemIdentity(item)) {
                    <button type="button" [attr.aria-expanded]="movingItemId === contract.itemIdentity(item)" (click)="openMove(item)">{{ i18n.translate('projects.copy.move') }}</button>
                  }
                  @if (contract.busyItemId === contract.itemIdentity(item)) { <span role="status">{{ i18n.translate('projects.copy.savingMove') }}</span> }
                </div>
                @if (movingItemId === contract.itemIdentity(item)) {
                  <form class="coglatas-kanban__move" (submit)="submitMove(item, $event)" (keydown.escape)="cancelMove(item, $event)">
                    <p>{{ i18n.translate('projects.copy.currentStage') }} <strong>{{ column.label }}</strong></p>
                    <label>{{ i18n.translate('projects.copy.targetStage') }}
                      <select [value]="moveTargetStatus" (change)="changeTarget($any($event.target).value)">
                        @for (target of contract.columns; track target.id) {
                          <option [value]="target.id" [selected]="target.id === moveTargetStatus" [disabled]="!contract.canRequestTransition(item, target.id)">{{ target.label }}</option>
                        }
                      </select>
                    </label>
                    <label>{{ i18n.translate('projects.copy.position') }}
                      <select [value]="movePosition" (change)="movePosition = $any($event.target).value">
                        <option value="end" [selected]="movePosition === 'end'">{{ i18n.translate('projects.copy.endOfStage') }}</option>
                        <option value="start" [selected]="movePosition === 'start'">{{ i18n.translate('projects.copy.startOfStage') }}</option>
                        @for (neighbor of positionItems(item); track contract.itemIdentity(neighbor)) {
                          <option [value]="'before:' + contract.itemIdentity(neighbor)" [selected]="movePosition === 'before:' + contract.itemIdentity(neighbor)">{{ i18n.translate('projects.copy.beforeTitle', {title: contract.itemTitle(neighbor)}) }}</option>
                        }
                      </select>
                    </label>
                    @if (targetRequiresReason()) {
                      <label>{{ i18n.translate('projects.copy.reason') }}<textarea required maxlength="1000" [value]="moveReason" (input)="moveReason = $any($event.target).value"></textarea></label>
                    }
                    <div><button type="submit" [disabled]="targetRequiresReason() && !moveReason.trim()">{{ i18n.translate('projects.copy.applyMove') }}</button><button type="button" (click)="cancelMove(item)">{{ i18n.translate('common.cancel') }}</button></div>
                  </form>
                }
              </li>
            }
          </ul>
        </section>
      }
    </div></coglatas-adapter-shell>`,
  styleUrl: './coglatas-adapter-shell.component.scss', changeDetection: ChangeDetectionStrategy.OnPush
})
export class CoglatasKanbanComponent extends CoglatasAdapterShellInput implements AfterViewChecked, OnChanges {
  readonly i18n = injectProjectsI18n();
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  @Input({ required: true }) contract!: CoglatasKanbanContract<object>;
  @Output() readonly moveRequested = new EventEmitter<CoglatasKanbanMoveRequest<object>>();
  @Output() readonly itemActivated = new EventEmitter<object>();
  @Output() readonly interactionActiveChange = new EventEmitter<boolean>();
  private draggedItem: object | null = null;
  movingItemId: string | null = null;
  moveTargetStatus = '';
  movePosition = 'end';
  moveReason = '';
  private moveSource: 'drag' | 'keyboard' = 'keyboard';
  private restoredFocusId: string | null = null;
  private restoredFocusElement: HTMLElement | null = null;
  private restoreFocusAfterItemsChange = false;

  itemsFor(status: string): readonly object[] {
    return [...this.contract.items
      .filter((item) => this.contract.itemStatus(item) === status)]
      .sort((left, right) => {
        const leftLane = this.contract.itemSwimlane?.(left);
        const rightLane = this.contract.itemSwimlane?.(right);
        const laneOrder = leftLane && rightLane
          ? leftLane.label.localeCompare(rightLane.label) || leftLane.key.localeCompare(rightLane.key)
          : 0;
        return laneOrder || this.contract.itemOrder(left) - this.contract.itemOrder(right) || this.contract.itemIdentity(left).localeCompare(this.contract.itemIdentity(right));
      });
  }
  startsSwimlane(items: readonly object[], index: number): boolean {
    if (!this.contract.itemSwimlane) {return false;}
    if (index === 0) {return true;}
    return this.contract.itemSwimlane(items[index]).key !== this.contract.itemSwimlane(items[index - 1]).key;
  }
  swimlaneLabel(item: object): string { return this.contract.itemSwimlane?.(item).label ?? ''; }
  canDrag(item: object): boolean { return this.contract.canMoveItem(item) && !this.contract.busyItemId; }
  startDrag(item: object): void { this.draggedItem = item; this.interactionActiveChange.emit(true); }
  endDrag(): void {
    this.draggedItem = null;
    if (this.movingItemId === null) {this.interactionActiveChange.emit(false);}
  }
  allowDrop(event: DragEvent): void { event.preventDefault(); }
  dropAtEnd(status: string, event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    const draggedItem = this.draggedItem;
    if (draggedItem && this.contract.canRequestTransition(draggedItem, status)) {
      // An empty neighbor pair is the canonical "end of Stage" intent. It
      // remains correct when a swimlane changes visual grouping order.
      if (this.requiresReason(status)) {
        this.beginMove(draggedItem, status, 'end', 'drag');
      } else {
        this.emit(draggedItem, status, null, null, null, 'drag');
      }
    }
    this.endDrag();
  }
  dropBefore(neighbor: object, status: string, event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    const draggedItem = this.draggedItem;
    if (draggedItem && this.contract.itemIdentity(draggedItem) !== this.contract.itemIdentity(neighbor) && this.contract.canRequestTransition(draggedItem, status)) {
      const beforeItemId = this.contract.itemIdentity(neighbor);
      if (this.requiresReason(status)) {
        this.beginMove(draggedItem, status, `before:${beforeItemId}`, 'drag');
      } else {
        this.emit(draggedItem, status, beforeItemId, null, null, 'drag');
      }
    }
    this.endDrag();
  }
  activate(item: object): void { if (this.contract.canOpenItem(item)) {this.itemActivated.emit(item);} }
  openMove(item: object): void {
    this.beginMove(item, this.contract.itemStatus(item), 'end', 'keyboard');
  }
  changeTarget(status: string): void { this.moveTargetStatus = status; this.movePosition = 'end'; this.moveReason = ''; }
  positionItems(item: object): readonly object[] {
    // Ordering is Stage-wide even when swimlanes group the presentation.
    // Neighbor intents therefore use canonical rank order, not lane label order.
    return [...this.contract.items
      .filter((candidate) =>
        this.contract.itemStatus(candidate) === this.moveTargetStatus &&
        this.contract.itemIdentity(candidate) !== this.contract.itemIdentity(item))]
      .sort((left, right) =>
        this.contract.itemOrder(left) - this.contract.itemOrder(right) ||
        this.contract.itemIdentity(left).localeCompare(this.contract.itemIdentity(right)));
  }
  targetRequiresReason(): boolean { return this.requiresReason(this.moveTargetStatus); }
  submitMove(item: object, event: Event): void {
    event.preventDefault();
    if (!this.contract.canRequestTransition(item, this.moveTargetStatus)) {return;}
    if (this.targetRequiresReason() && !this.moveReason.trim()) {return;}
    const candidates = this.positionItems(item);
    const before = this.movePosition.startsWith('before:') ? this.movePosition.slice('before:'.length) : this.movePosition === 'start' ? (candidates[0] ? this.contract.itemIdentity(candidates[0]) : null) : null;
    // End of Stage is represented by the empty neighbor pair even when the
    // bounded board snapshot does not contain every card in the target Stage.
    this.emit(item, this.moveTargetStatus, before, null, this.moveReason.trim() || null, this.moveSource);
    this.closeMove();
  }
  cancelMove(item: object, event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    this.restoredFocusId = null;
    this.restoredFocusElement = null;
    this.restoreFocusAfterItemsChange = false;
    const id = this.contract.itemIdentity(item);
    this.closeMove();
    queueMicrotask(() => this.focus(id));
  }
  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['contract'];
    const previous = change?.previousValue as CoglatasKanbanContract<object> | undefined;
    const current = change?.currentValue as CoglatasKanbanContract<object> | undefined;
    const focusId = current?.focusItemId ?? null;
    this.restoreFocusAfterItemsChange = Boolean(
      previous &&
      current &&
      focusId &&
      previous.focusItemId === focusId &&
      previous.items !== current.items &&
      this.restoredFocusId === focusId &&
      this.restoredFocusElement?.ownerDocument.activeElement === this.restoredFocusElement
    );
  }
  ngAfterViewChecked(): void {
    const focusId = this.contract.focusItemId ?? null;
    if (!focusId) {
      this.restoredFocusId = null;
      this.restoredFocusElement = null;
      this.restoreFocusAfterItemsChange = false;
      return;
    }
    if (this.restoredFocusId === focusId && !this.restoreFocusAfterItemsChange) {return;}
    this.restoredFocusId = focusId;
    this.restoreFocusAfterItemsChange = false;
    queueMicrotask(() => this.restoredFocusElement = this.focus(focusId));
  }
  private beginMove(item: object, status: string, position: string, source: 'drag' | 'keyboard'): void {
    this.movingItemId = this.contract.itemIdentity(item);
    this.moveTargetStatus = status;
    this.movePosition = position;
    this.moveReason = '';
    this.moveSource = source;
    this.interactionActiveChange.emit(true);
  }
  private closeMove(): void {
    this.movingItemId = null;
    this.moveSource = 'keyboard';
    this.interactionActiveChange.emit(false);
  }
  private requiresReason(status: string): boolean {
    return this.contract.columns.find((column) => column.id === status)?.requiresReason === true;
  }
  private emit(item: object, status: string, before: string | null, after: string | null, reason: string | null, source: 'drag' | 'keyboard'): void {
    this.moveRequested.emit({ item, targetStatus: status, targetBeforeItemId: before, targetAfterItemId: after, reason, source });
  }
  private focus(itemId: string): HTMLElement | null {
    const card = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[data-kanban-card-id]'))
      .find((element) => element.dataset['kanbanCardId'] === itemId);
    card?.focus();
    return card ?? null;
  }
}

@Component({
  selector: 'coglatas-gantt',
  standalone: true,
  imports: [A11yModule, CoglatasAdapterShellComponent, NgTemplateOutlet],
  template: `
    <coglatas-adapter-shell
      adapter="gantt"
      [ariaLabel]="contract.ariaLabel"
      [presentation]="presentation"
      [state]="state"
      [label]="i18n.translate('projects.copy.projectSchedule')">
      <section
        class="coglatas-gantt"
        [class.coglatas-gantt--narrow]="presentation === 'narrow'"
        data-testid="coglatas-gantt-projection">
        <p class="coglatas-gantt__feedback" aria-live="polite" role="status">{{ contract.feedback }}</p>
        @if (vendorLoadError(); as loadError) {
          <p class="coglatas-gantt__warning" role="alert" data-testid="coglatas-gantt-vendor-error">{{ i18n.translate(loadError) }}</p>
        }

        @if (hasCanonicalProjection) {
          <p data-testid="coglatas-gantt-readonly">
            {{ contract.readOnly ? i18n.translate('projects.copy.scheduleIsReadOnlyForTheCurrentActor') : i18n.translate('projects.copy.authorizedManualScheduleChangesAreAvailable') }}
          </p>

          @if (vendorEligible) {
            <section class="coglatas-gantt__visual" [attr.aria-label]="i18n.translate('projects.copy.optionalVisualTimeline')">
              <h3>{{ i18n.translate('projects.copy.timelineChart') }}</h3>
              <ng-container #syncfusionGanttHost />
            </section>
          }

          @if (contract.calendar; as calendar) {
            <section class="coglatas-gantt__summary" aria-labelledby="coglatas-gantt-calendar-heading">
              <h3 id="coglatas-gantt-calendar-heading">{{ i18n.translate('projects.copy.calendar') }}</h3>
              <p>{{ i18n.translate('projects.copy.workspaceTimezone') }} <strong>{{ calendar.timeZone }}</strong></p>
              <p>{{ i18n.translate('projects.copy.workingDaysDays', {days: calendar.workingDays.length ? calendar.workingDays.join(', ') : i18n.translate('projects.copy.noWorkingDaySummaryAvailable')}) }}</p>
              <p>{{ i18n.translate('projects.copy.holidayDataValue', {value: calendar.holidaysAvailable ? i18n.translate('projects.copy.available') : i18n.translate('projects.copy.unavailable')}) }}</p>
              @if (calendar.limitations.length) {
                <ul [attr.aria-label]="i18n.translate('projects.copy.calendarLimitations')">
                  @for (limitation of calendar.limitations; track limitation) { <li>{{ limitation }}</li> }
                </ul>
              }
            </section>
          }

          <section aria-labelledby="coglatas-gantt-scheduled-heading">
            <h3 id="coglatas-gantt-scheduled-heading">{{ i18n.translate('projects.copy.scheduledWork') }}</h3>
            @if (scheduledTasks.length) {
              <ol class="coglatas-gantt__items">
                @for (item of scheduledTasks; track item.taskId) {
                  <li><ng-container [ngTemplateOutlet]="canonicalItem" [ngTemplateOutletContext]="{ $implicit: item }"></ng-container></li>
                }
              </ol>
            } @else {
              <p>{{ i18n.translate('projects.copy.noScheduledTasksAreAvailable') }}</p>
            }
          </section>

          <section aria-labelledby="coglatas-gantt-milestones-heading">
            <h3 id="coglatas-gantt-milestones-heading">{{ i18n.translate('projects.copy.milestones') }}</h3>
            @if (canonicalMilestones.length) {
              <ol class="coglatas-gantt__items">
                @for (item of canonicalMilestones; track item.taskId) {
                  <li><ng-container [ngTemplateOutlet]="canonicalItem" [ngTemplateOutletContext]="{ $implicit: item }"></ng-container></li>
                }
              </ol>
            } @else {
              <p>{{ i18n.translate('projects.copy.noMilestonesAreAvailable') }}</p>
            }
          </section>

          <section aria-labelledby="coglatas-gantt-unscheduled-heading">
            <h3 id="coglatas-gantt-unscheduled-heading">{{ i18n.translate('projects.copy.unscheduledWork') }}</h3>
            @if (unscheduledItems.length) {
              <ol class="coglatas-gantt__items">
                @for (item of unscheduledItems; track item.taskId) {
                  <li><ng-container [ngTemplateOutlet]="canonicalItem" [ngTemplateOutletContext]="{ $implicit: item }"></ng-container></li>
                }
              </ol>
            } @else {
              <p>{{ i18n.translate('projects.copy.noUnscheduledTasksAreAvailable') }}</p>
            }
          </section>

          <section aria-labelledby="coglatas-gantt-dependencies-heading">
            <h3 id="coglatas-gantt-dependencies-heading">{{ i18n.translate('projects.copy.dependencies') }}</h3>
            @if (dependencies.length) {
              <ol class="coglatas-gantt__dependencies">
                @for (dependency of dependencies; track dependency.dependencyId) {
                  <li
                    tabindex="-1"
                    [attr.data-gantt-dependency-id]="dependency.dependencyId">
                    <strong>{{ dependencyTitle(dependency) }}</strong>
                    <span>{{ i18n.translate('projects.copy.dependencyTypeSummary', {type: dependencyTypeLabel(dependency), editability: i18n.translate(dependency.editable ? 'projects.copy.editable' : 'projects.copy.readOnly')}) }}</span>
                    @if (dependency.warnings.length) {
                      <ul class="coglatas-gantt__warnings" aria-label="Dependency warnings">
                        @for (warning of dependency.warnings; track warning.code + ':' + warning.targetId) {
                          <li>{{ warning.severity }}: {{ warning.message }} ({{ warning.code }})</li>
                        }
                      </ul>
                    }
                    @if (canRemoveDependency(dependency)) {
                      <button type="button" [disabled]="isDependencyBusy(dependency)" (click)="openRemoveDependency(dependency, $event)">{{ i18n.translate('projects.copy.removeFSDependency') }}</button>
                    }
                  </li>
                }
              </ol>
            } @else {
              <p>{{ i18n.translate('projects.copy.noDependenciesAreAvailable') }}</p>
            }
          </section>

          <section aria-labelledby="coglatas-gantt-warnings-heading">
            <h3 id="coglatas-gantt-warnings-heading">{{ i18n.translate('projects.copy.scheduleWarnings') }}</h3>
            @if (warnings.length) {
              <ul class="coglatas-gantt__warnings">
                @for (warning of warnings; track warning.code + ':' + warning.targetId + ':' + warning.field) {
                  <li>{{ warning.severity }}: {{ warning.message }} ({{ warning.code }})</li>
                }
              </ul>
            } @else {
              <p>{{ i18n.translate('projects.copy.noScheduleWarnings') }}</p>
            }
          </section>
        } @else {
          <p data-testid="coglatas-gantt-readonly">
            {{ i18n.translate(contract.readOnly ? 'projects.copy.scheduleReadOnlyCompatibility' : 'projects.copy.scheduleChangesAvailable') }}
          </p>
          @if (contract.milestones.length) {
            <h3>{{ i18n.translate('projects.copy.milestones') }}</h3>
            <ul>
              @for (milestone of contract.milestones; track milestone.id) {
                <li><strong>{{ milestone.title }}</strong> {{ milestone.dueDate ?? i18n.translate('projects.copy.noDueDate') }} - {{ milestone.status }}</li>
              }
            </ul>
          }
          <h3>{{ i18n.translate('projects.copy.tasks') }}</h3>
          <ol>
            @for (task of contract.tasks; track contract.taskIdentity(task)) {
              <li>{{ contract.taskLabel(task) }}</li>
            }
          </ol>
        }
      </section>

      <ng-template #canonicalItem let-item>
        <article
          class="coglatas-gantt__item"
          [class.coglatas-gantt__item--blocked]="item.isBlocked"
          [class.coglatas-gantt__item--derived]="isDerivedParent(item)"
          tabindex="-1"
          [attr.data-gantt-item-id]="item.taskId"
          [attr.aria-busy]="isBusy(item)">
          <header>
            <div>
              <span class="coglatas-gantt__kind">{{ i18n.translate(item.kind === 'milestone' ? 'projects.copy.milestone' : isDerivedParent(item) ? 'projects.copy.derivedParentTask' : 'projects.copy.task') }}</span>
              <h4>{{ item.title }}</h4>
            </div>
            @if (isBusy(item)) { <span role="status">{{ i18n.translate('projects.copy.saving') }}</span> }
          </header>
          <dl class="coglatas-gantt__metadata">
            <div><dt>{{ i18n.translate('projects.copy.stage') }}</dt><dd>{{ item.workflowStageName ?? i18n.translate('projects.copy.noStage') }} ({{ stageLabel(item) }})</dd></div>
            <div><dt>{{ i18n.translate('projects.copy.priority') }}</dt><dd>{{ priorityLabel(item) }}</dd></div>
            <div><dt>{{ i18n.translate('projects.copy.blocked') }}</dt><dd>{{ item.isBlocked ? i18n.translate('projects.copy.blocked') : i18n.translate('projects.copy.notBlocked') }}</dd></div>
            <div><dt>{{ i18n.translate('projects.copy.dates') }}</dt><dd>{{ dateLabel(item) }}</dd></div>
            <div><dt>{{ i18n.translate('projects.copy.progress') }}</dt><dd>{{ i18n.formatNumber(item.progressPercent) }}%{{ item.progressIsDerived ? i18n.translate('projects.copy.derived') : '' }}</dd></div>
            <div><dt>{{ i18n.translate('projects.copy.assignee') }}</dt><dd>{{ item.primaryAssignee?.displayName ?? i18n.translate('projects.copy.unassigned') }}</dd></div>
            <div><dt>{{ i18n.translate('projects.copy.parent') }}</dt><dd>{{ parentTitle(item) }}</dd></div>
          </dl>
          @if (item.warnings.length) {
            <ul class="coglatas-gantt__warnings" [attr.aria-label]="i18n.translate('projects.copy.workItemWarnings')">
              @for (warning of item.warnings; track warning.code + ':' + warning.field) {
                <li>{{ warning.severity }}: {{ warning.message }} ({{ warning.code }})</li>
              }
            </ul>
          }
          <div class="coglatas-gantt__actions">
            @if (canOpen(item)) {
              <button type="button" (click)="openDetails(item)">{{ i18n.translate('projects.copy.openDetails') }}</button>
            }
            @if (canEditSchedule(item)) {
              <button type="button" [disabled]="isBusy(item)" (click)="openSchedule(item, $event)">
                {{ item.kind === 'milestone' ? i18n.translate('projects.copy.editMilestoneDate') : i18n.translate('projects.copy.editDates') }}
              </button>
            }
            @if (canEditProgress(item)) {
              <button type="button" [disabled]="isBusy(item)" (click)="openProgress(item, $event)">{{ i18n.translate('projects.copy.editProgress') }}</button>
            }
            @if (canClearSchedule(item)) {
              <button type="button" [disabled]="isBusy(item)" (click)="openSchedule(item, $event, true)">{{ i18n.translate('projects.copy.moveToUnscheduled') }}</button>
            }
            @if (canAddDependency(item)) {
              <button type="button" [disabled]="isBusy(item)" (click)="openAddDependency(item, $event)">{{ i18n.translate('projects.copy.addFSPredecessor') }}</button>
            }
          </div>
        </article>
      </ng-template>

      @if (activeEditor !== null) {
        <div class="coglatas-gantt__dialog-backdrop">
          <section
            class="coglatas-gantt__dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="coglatas-gantt-editor-title"
            [cdkTrapFocus]="true"
            [cdkTrapFocusAutoCapture]="true"
            (keydown.escape)="cancelEditor($event)">
            <h3 id="coglatas-gantt-editor-title">{{ editorTitle }}</h3>
            @if (formError) { <p class="coglatas-gantt__form-error" role="alert">{{ i18n.translate(formError) }}</p> }

            @if (activeEditor === 'schedule' && activeItem; as item) {
              <form class="coglatas-gantt__form" (submit)="submitSchedule($event)">
                @if (item.kind === 'milestone') {
                  <label>{{ i18n.translate('projects.copy.milestoneDate') }}
                    <input
                      type="date"
                      name="milestoneDate"
                      required
                      [value]="milestoneDate"
                      (input)="milestoneDate = $any($event.target).value" />
                  </label>
                } @else {
                  <label>{{ i18n.translate('projects.copy.plannedStart') }}
                    <input
                      type="date"
                      name="plannedStartDate"
                      [value]="plannedStartDate"
                      (input)="plannedStartDate = $any($event.target).value" />
                  </label>
                  <label>{{ i18n.translate('projects.copy.plannedEnd') }}
                    <input
                      type="date"
                      name="plannedEndDate"
                      [value]="plannedEndDate"
                      (input)="plannedEndDate = $any($event.target).value" />
                  </label>
                }
                <div class="coglatas-gantt__form-actions">
                  <button type="submit" [disabled]="editorBusy || !canEditSchedule(item)">{{ i18n.translate('projects.copy.applySchedule') }}</button>
                  @if (canClearSchedule(item)) {
                    <button type="button" [disabled]="editorBusy" (click)="submitClearSchedule()">{{ i18n.translate('projects.copy.clearSchedule') }}</button>
                  }
                  <button type="button" (click)="cancelEditor()">{{ i18n.translate('common.cancel') }}</button>
                </div>
              </form>
            }

            @if (activeEditor === 'progress' && activeItem; as item) {
              <form class="coglatas-gantt__form" (submit)="submitProgress($event)">
                <label>{{ i18n.translate('projects.copy.progressPercent') }}
                  <input
                    type="number"
                    name="progressPercent"
                    min="0"
                    max="100"
                    step="1"
                    required
                    [value]="progressPercent"
                    (input)="progressPercent = +$any($event.target).value" />
                </label>
                <div class="coglatas-gantt__form-actions">
                  <button type="submit" [disabled]="editorBusy">{{ i18n.translate('projects.copy.applyProgress') }}</button>
                  <button type="button" (click)="cancelEditor()">{{ i18n.translate('common.cancel') }}</button>
                </div>
              </form>
            }

            @if (activeEditor === 'addDependency' && activeItem; as item) {
              <form class="coglatas-gantt__form" (submit)="submitAddDependency($event)">
                <p>{{ i18n.translate('projects.copy.successor') }} <strong>{{ item.title }}</strong></p>
                <label>{{ i18n.translate('projects.copy.finishToStartPredecessor') }}
                  <select
                    name="predecessorTaskId"
                    required
                    [value]="predecessorTaskId"
                    (change)="predecessorTaskId = $any($event.target).value">
                    <option value="">{{ i18n.translate('projects.copy.selectAPredecessor') }}</option>
                    @for (candidate of dependencyCandidates(item); track candidate.taskId) {
                      <option [value]="candidate.taskId">{{ candidate.title }}</option>
                    }
                  </select>
                </label>
                <p>{{ i18n.translate('projects.copy.datesWillNotBeMovedAutomatically') }}</p>
                <div class="coglatas-gantt__form-actions">
                  <button type="submit" [disabled]="editorBusy || !predecessorTaskId">{{ i18n.translate('projects.copy.addDependency') }}</button>
                  <button type="button" (click)="cancelEditor()">{{ i18n.translate('common.cancel') }}</button>
                </div>
              </form>
            }

            @if (activeEditor === 'removeDependency' && activeDependency; as dependency) {
              <form class="coglatas-gantt__form" (submit)="submitRemoveDependency($event)">
                <p>{{ i18n.translate('projects.copy.remove') }} <strong>{{ dependencyTitle(dependency) }}</strong>?</p>
                <p>{{ i18n.translate('projects.copy.datesWillNotBeMovedAutomatically') }}</p>
                <div class="coglatas-gantt__form-actions">
                  <button type="submit" [disabled]="editorBusy">{{ i18n.translate('projects.copy.removeDependency') }}</button>
                  <button type="button" (click)="cancelEditor()">{{ i18n.translate('common.cancel') }}</button>
                </div>
              </form>
            }
          </section>
        </div>
      }
    </coglatas-adapter-shell>
  `,
  styleUrl: './coglatas-gantt.component.scss', changeDetection: ChangeDetectionStrategy.OnPush
})
export class CoglatasGanttComponent extends CoglatasAdapterShellInput implements OnChanges, OnDestroy {
  readonly i18n = injectProjectsI18n();
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly injector = inject(Injector);
  private vendorHost?: ViewContainerRef;
  private vendorComponent?: ComponentRef<SyncfusionGanttComponent>;
  private vendorLoading = false;
  private destroyed = false;
  private editorTrigger: HTMLElement | null = null;
  private restoredFocusId: string | null = null;
  private itemSignature = '';
  private focusAfterRender: AfterRenderRef | null = null;

  @Input({ required: true }) contract!: CoglatasGanttContract<object>;
  @Output() readonly editRequested = new EventEmitter<CoglatasGanttEditIntent>();
  @Output() readonly itemActivated = new EventEmitter<CoglatasGanttItem>();
  @Output() readonly interactionActiveChange = new EventEmitter<boolean>();
  @Output() readonly vendorFailed = new EventEmitter<void>();

  readonly vendorLoadError = signal<keyof typeof PROJECTS_ENGLISH_MESSAGES | null>(null);
  activeEditor: 'schedule' | 'progress' | 'addDependency' | 'removeDependency' | null = null;
  activeItem: CoglatasGanttItem | null = null;
  activeDependency: CoglatasGanttDependency | null = null;
  plannedStartDate = '';
  plannedEndDate = '';
  milestoneDate = '';
  progressPercent = 0;
  predecessorTaskId = '';
  formError: keyof typeof PROJECTS_ENGLISH_MESSAGES | '' = '';

  @ViewChild('syncfusionGanttHost', { read: ViewContainerRef })
  set syncfusionGanttHost(host: ViewContainerRef | undefined) {
    this.vendorHost = host;
    if (host) {void this.loadVendor();}
  }

  get hasCanonicalProjection(): boolean {
    return this.contract.scheduledItems !== undefined
      || this.contract.unscheduledItems !== undefined
      || this.contract.canonicalMilestones !== undefined;
  }

  get scheduledTasks(): readonly CoglatasGanttItem[] {
    return (this.contract.scheduledItems ?? []).filter((item) => item.kind === 'task');
  }

  get canonicalMilestones(): readonly CoglatasGanttItem[] {
    const unique = new Map<string, CoglatasGanttItem>();
    for (const item of [
      ...(this.contract.canonicalMilestones ?? []),
      ...(this.contract.scheduledItems ?? []).filter((candidate) => candidate.kind === 'milestone')
    ]) {unique.set(item.taskId, item);}
    return [...unique.values()];
  }

  get unscheduledItems(): readonly CoglatasGanttItem[] {
    return this.contract.unscheduledItems ?? [];
  }

  get dependencies(): readonly CoglatasGanttDependency[] {
    return this.contract.dependencies ?? [];
  }

  get warnings(): readonly CoglatasGanttWarning[] {
    return this.contract.warnings ?? [];
  }

  get allCanonicalItems(): readonly CoglatasGanttItem[] {
    const unique = new Map<string, CoglatasGanttItem>();
    for (const item of [
      ...this.scheduledTasks,
      ...this.canonicalMilestones,
      ...this.unscheduledItems
    ]) {unique.set(item.taskId, item);}
    return [...unique.values()];
  }

  get vendorEligible(): boolean {
    return this.presentation === 'desktop'
      && this.hasCanonicalProjection
      && ['ready', 'empty', 'degraded', 'conflict', 'rollback'].includes(this.state);
  }

  get editorTitle(): string {
    if (this.activeEditor === 'progress') {return this.i18n.translate('projects.copy.editProgress');}
    if (this.activeEditor === 'addDependency') {return this.i18n.translate('projects.copy.addFinishToStartDependency');}
    if (this.activeEditor === 'removeDependency') {return this.i18n.translate('projects.copy.removeFinishToStartDependency');}
    return this.activeItem?.kind === 'milestone' ? this.i18n.translate('projects.copy.editMilestoneDate') : this.i18n.translate('projects.copy.editSchedule');
  }

  get editorBusy(): boolean {
    return this.activeItem !== null && this.isBusy(this.activeItem);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['contract']) {
      const signature = this.allCanonicalItems
        .map((item) => `${item.taskId}:${item.version}:${item.plannedStartDate}:${item.plannedEndDate}:${item.milestoneDate}:${item.progressPercent}`)
        .join('|');
      const signatureChanged = this.itemSignature !== '' && signature !== this.itemSignature;
      const focusId = this.contract.focusItemId ?? null;
      const active = document.activeElement;
      const focusContextRemainsLogical =
        active === this.host.nativeElement.ownerDocument.body ||
        (active instanceof HTMLElement && this.host.nativeElement.contains(active));
      this.itemSignature = signature;
      this.updateVendorInput();

      if (!focusId) {
        this.restoredFocusId = null;
        this.cancelFocusAfterRender();
      } else if (this.restoredFocusId !== focusId ||
                 (signatureChanged && focusContextRemainsLogical)) {
        this.scheduleFocusAfterRender(focusId);
      }
    }

    if (!this.vendorEligible) {this.destroyVendor();}
    else if (this.vendorHost) {void this.loadVendor();}
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.cancelFocusAfterRender();
    this.destroyVendor();
  }

  openDetails(item: CoglatasGanttItem): void {
    if (this.canOpen(item)) {this.itemActivated.emit(item);}
  }

  openSchedule(item: CoglatasGanttItem, event: Event, clear = false): void {
    if (this.isBusy(item)
      || (!this.canEditSchedule(item) && !(clear && this.canClearSchedule(item)))) {return;}
    this.beginEditor('schedule', item, event);
    this.plannedStartDate = clear ? '' : item.plannedStartDate ?? '';
    this.plannedEndDate = clear ? '' : item.plannedEndDate ?? '';
    this.milestoneDate = item.milestoneDate ?? '';
  }

  openProgress(item: CoglatasGanttItem, event: Event): void {
    if (!this.canEditProgress(item) || this.isBusy(item)) {return;}
    this.beginEditor('progress', item, event);
    this.progressPercent = item.progressPercent;
  }

  openAddDependency(item: CoglatasGanttItem, event: Event): void {
    if (!this.canAddDependency(item) || this.isBusy(item)) {return;}
    this.beginEditor('addDependency', item, event);
    this.predecessorTaskId = '';
  }

  openRemoveDependency(dependency: CoglatasGanttDependency, event: Event): void {
    if (!this.canRemoveDependency(dependency) || this.isDependencyBusy(dependency)) {return;}
    this.activeEditor = 'removeDependency';
    this.activeDependency = dependency;
    this.activeItem = this.allCanonicalItems.find((item) => item.taskId === dependency.successorTaskId) ?? null;
    this.editorTrigger = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    this.formError = '';
    this.interactionActiveChange.emit(true);
  }

  cancelEditor(event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    this.closeEditor();
  }

  submitSchedule(event: Event): void {
    event.preventDefault();
    const item = this.activeItem;
    if (!item || !this.canEditSchedule(item)) {return;}

    if (item.kind === 'milestone') {
      if (!this.validDateOnly(this.milestoneDate)) {
        this.formError = 'projects.copy.aMilestoneDateIsRequired';
        return;
      }
    } else {
      if ((this.plannedStartDate && !this.validDateOnly(this.plannedStartDate))
        || (this.plannedEndDate && !this.validDateOnly(this.plannedEndDate))) {
        this.formError = 'projects.copy.useValidCalendarDates';
        return;
      }
      if (this.plannedStartDate && this.plannedEndDate
        && this.plannedEndDate < this.plannedStartDate) {
        this.formError = 'projects.copy.plannedEndCannotBeBeforePlannedStart';
        return;
      }
      if (!this.plannedStartDate && !this.plannedEndDate && !this.canClearSchedule(item)) {
        this.formError = 'projects.copy.youDoNotHavePermissionToClearThisSchedule';
        return;
      }
    }

    this.dispatchEdit({
      kind: 'schedule',
      taskId: item.taskId,
      plannedStartDate: item.kind === 'task' ? this.plannedStartDate || null : null,
      plannedEndDate: item.kind === 'task' ? this.plannedEndDate || null : null,
      milestoneDate: item.kind === 'milestone' ? this.milestoneDate : null,
      expectedVersion: item.version,
      source: 'form'
    });
    this.closeEditor();
  }

  submitClearSchedule(): void {
    const item = this.activeItem;
    if (!item || !this.canClearSchedule(item)) {return;}
    this.dispatchEdit({
      kind: 'schedule',
      taskId: item.taskId,
      plannedStartDate: null,
      plannedEndDate: null,
      milestoneDate: null,
      expectedVersion: item.version,
      source: 'form'
    });
    this.closeEditor();
  }

  submitProgress(event: Event): void {
    event.preventDefault();
    const item = this.activeItem;
    const value = Number(this.progressPercent);
    if (!item || !this.canEditProgress(item)) {return;}
    if (!Number.isInteger(value) || value < 0 || value > 100) {
      this.formError = 'projects.copy.progressMustBeAWholeNumberFrom0To100';
      return;
    }
    if (item.kind === 'milestone' && value !== 0 && value !== 100) {
      this.formError = 'projects.copy.milestoneProgressMustBe0Or100';
      return;
    }
    this.dispatchEdit({
      kind: 'progress',
      taskId: item.taskId,
      progressPercent: value,
      expectedVersion: item.version,
      source: 'form'
    });
    this.closeEditor();
  }

  submitAddDependency(event: Event): void {
    event.preventDefault();
    const item = this.activeItem;
    if (!item || !this.canAddDependency(item)
      || !this.dependencyCandidates(item).some((candidate) => candidate.taskId === this.predecessorTaskId)) {
      this.formError = 'projects.copy.selectAnAuthorizedPredecessor';
      return;
    }
    this.dispatchEdit({
      kind: 'addDependency',
      predecessorTaskId: this.predecessorTaskId,
      successorTaskId: item.taskId,
      type: 'finishToStart',
      expectedVersion: item.version,
      source: 'form'
    });
    this.closeEditor();
  }

  submitRemoveDependency(event: Event): void {
    event.preventDefault();
    const dependency = this.activeDependency;
    if (!dependency || !this.canRemoveDependency(dependency)) {return;}
    this.dispatchEdit({
      kind: 'removeDependency',
      dependencyId: dependency.dependencyId,
      successorTaskId: dependency.successorTaskId,
      expectedVersion: dependency.version,
      source: 'form'
    });
    this.closeEditor();
  }

  canOpen(item: CoglatasGanttItem): boolean {
    return item.kind === 'task'
      && (this.contract.permissions?.canOpen ?? false)
      && item.scheduleEditPermissions.canOpen;
  }

  canEditSchedule(item: CoglatasGanttItem): boolean {
    return !this.contract.readOnly
      && !this.isDerivedParent(item)
      && (this.contract.permissions?.canEditSchedule ?? false)
      && item.scheduleEditPermissions.canEditSchedule;
  }

  canEditProgress(item: CoglatasGanttItem): boolean {
    return !this.contract.readOnly
      && !this.isDerivedParent(item)
      && (this.contract.permissions?.canEditProgress ?? false)
      && item.scheduleEditPermissions.canEditProgress;
  }

  canClearSchedule(item: CoglatasGanttItem): boolean {
    return item.kind === 'task'
      && !this.contract.readOnly
      && !this.isDerivedParent(item)
      && (this.contract.permissions?.canClearSchedule ?? false)
      && item.scheduleEditPermissions.canClearSchedule;
  }

  canAddDependency(item: CoglatasGanttItem): boolean {
    return item.kind === 'task'
      && !this.contract.readOnly
      && (this.contract.permissions?.canManageDependencies ?? false)
      && item.scheduleEditPermissions.canManageDependencies
      && this.dependencyCandidates(item).length > 0;
  }

  canRemoveDependency(dependency: CoglatasGanttDependency): boolean {
    const successor = this.allCanonicalItems.find((item) => item.taskId === dependency.successorTaskId);
    const predecessor = this.allCanonicalItems.find((item) => item.taskId === dependency.predecessorTaskId);
    return dependency.type === 'finishToStart'
      && dependency.editable
      && predecessor?.kind === 'task'
      && successor?.kind === 'task'
      && !this.contract.readOnly
      && (this.contract.permissions?.canManageDependencies ?? false)
      && successor?.scheduleEditPermissions.canManageDependencies === true;
  }

  dependencyCandidates(successor: CoglatasGanttItem): readonly CoglatasGanttItem[] {
    return this.allCanonicalItems.filter((candidate) =>
      candidate.kind === 'task'
      && candidate.taskId !== successor.taskId
      && !this.dependencies.some((dependency) =>
        dependency.predecessorTaskId === candidate.taskId
        && dependency.successorTaskId === successor.taskId));
  }

  isBusy(item: CoglatasGanttItem): boolean {
    return this.contract.busyItemId === item.taskId;
  }

  isDependencyBusy(dependency: CoglatasGanttDependency): boolean {
    return this.contract.busyItemId === dependency.predecessorTaskId
      || this.contract.busyItemId === dependency.successorTaskId;
  }

  isDerivedParent(item: CoglatasGanttItem): boolean {
    return item.progressIsDerived
      || this.allCanonicalItems.some((candidate) => candidate.parentTaskId === item.taskId);
  }

  parentTitle(item: CoglatasGanttItem): string {
    if (!item.parentTaskId) {return this.i18n.translate('projects.copy.none');}
    return this.allCanonicalItems.find((candidate) => candidate.taskId === item.parentTaskId)?.title
      ?? this.i18n.translate('projects.copy.authorizedParentUnavailable');
  }

  dateLabel(item: CoglatasGanttItem): string {
    if (item.kind === 'milestone') {return item.milestoneDate ? this.i18n.formatDateOnly(item.milestoneDate) : this.i18n.translate('projects.copy.milestoneDateRequired');}
    if (!item.plannedStartDate && !item.plannedEndDate) {return this.i18n.translate('projects.copy.unscheduled');}
    return this.i18n.translate('projects.copy.calendarDateRange', {start: item.plannedStartDate ? this.i18n.formatDateOnly(item.plannedStartDate) : this.i18n.translate('projects.copy.noStart'), end: item.plannedEndDate ? this.i18n.formatDateOnly(item.plannedEndDate) : this.i18n.translate('projects.copy.noEnd')});
  }

  priorityLabel(item: CoglatasGanttItem): string {
    const keys = {low: 'projects.copy.low', medium: 'projects.copy.medium', high: 'projects.copy.high', critical: 'projects.copy.critical'} as const;
    return this.i18n.translate(keys[item.priority]);
  }

  stageLabel(item: CoglatasGanttItem): string {
    const keys = {backlog: 'projects.copy.backlog', todo: 'projects.copy.todo', inProgress: 'projects.copy.inProgress', review: 'projects.copy.review', done: 'projects.copy.done', cancelled: 'projects.copy.cancelled'} as const;
    return this.i18n.locale() === 'en' ? item.stageCategory.replace(/([A-Z])/gu, ' $1').toLowerCase() : this.i18n.translate(keys[item.stageCategory]);
  }

  dependencyTitle(dependency: CoglatasGanttDependency): string {
    const predecessor = this.allCanonicalItems.find((item) => item.taskId === dependency.predecessorTaskId)?.title
      ?? this.i18n.translate('projects.copy.authorizedPredecessorUnavailable');
    const successor = this.allCanonicalItems.find((item) => item.taskId === dependency.successorTaskId)?.title
      ?? this.i18n.translate('projects.copy.authorizedSuccessorUnavailable');
    return this.i18n.translate('projects.copy.dependencyEndpoints', { predecessor, successor });
  }

  dependencyTypeLabel(dependency: CoglatasGanttDependency): string {
    return dependency.type === 'finishToStart'
      ? this.i18n.translate('projects.copy.finishToStart')
      : this.i18n.translate('projects.copy.legacyDependencyType', { type: dependency.type });
  }

  private beginEditor(
    editor: 'schedule' | 'progress' | 'addDependency',
    item: CoglatasGanttItem,
    event: Event
  ): void {
    this.activeEditor = editor;
    this.activeItem = item;
    this.activeDependency = null;
    this.editorTrigger = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    this.formError = '';
    this.interactionActiveChange.emit(true);
  }

  private closeEditor(): void {
    const trigger = this.editorTrigger;
    this.activeEditor = null;
    this.activeItem = null;
    this.activeDependency = null;
    this.editorTrigger = null;
    this.formError = '';
    this.interactionActiveChange.emit(false);
    queueMicrotask(() => {
      if (trigger?.isConnected) {trigger.focus();}
      else if (this.contract.focusItemId) {this.focusItem(this.contract.focusItemId);}
    });
  }

  private dispatchEdit(intent: CoglatasGanttEditIntent): void {
    if (this.editRequested.observed || !this.contract.requestEdit) {this.editRequested.emit(intent);}
    else {this.contract.requestEdit(intent);}
  }

  private validDateOnly(value: string): boolean {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(value);
    if (!match) {return false;}
    const year = Number(match[1]);
    const month = Number(match[2]);
    const day = Number(match[3]);
    if (year < 1 || month < 1 || month > 12 || day < 1) {return false;}
    const monthLengths = [
      31,
      year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0) ? 29 : 28,
      31,
      30,
      31,
      30,
      31,
      31,
      30,
      31,
      30,
      31
    ];
    return day <= monthLengths[month - 1];
  }

  private async loadVendor(): Promise<void> {
    if (this.destroyed || !this.vendorEligible || !this.vendorHost || this.vendorComponent || this.vendorLoading) {return;}
    this.vendorLoading = true;
    try {
      const { SyncfusionGanttComponent } = await import('./syncfusion-gantt.component');
      if (this.destroyed || !this.vendorEligible || !this.vendorHost) {return;}
      this.vendorHost.clear();
      const component = this.vendorHost.createComponent(SyncfusionGanttComponent);
      this.vendorComponent = component;
      component.instance.editRequested.subscribe((intent) => this.dispatchEdit(intent));
      component.instance.interactionActiveChange.subscribe((active) => this.interactionActiveChange.emit(active));
      component.instance.vendorFailed.subscribe(() => {
        this.vendorLoadError.set('projects.copy.theVisualTimelineCouldNotBeLoadedTheCompleteScheduleListAndFormsRemainAvailable');
        this.vendorFailed.emit();
        this.destroyVendor(false);
      });
      component.setInput('contract', this.contract);
      component.changeDetectorRef.detectChanges();
      this.vendorLoadError.set(null);
    } catch {
      if (!this.destroyed) {
        this.vendorLoadError.set('projects.copy.theVisualTimelineCouldNotBeLoadedTheCompleteScheduleListAndFormsRemainAvailable');
        this.vendorFailed.emit();
        this.destroyVendor(false);
      }
    } finally {
      this.vendorLoading = false;
    }
  }

  private updateVendorInput(): void {
    this.vendorComponent?.setInput('contract', this.contract);
  }

  private destroyVendor(clearError = true): void {
    this.vendorComponent?.destroy();
    this.vendorComponent = undefined;
    if (clearError) {this.vendorLoadError.set(null);}
  }

  private focusItem(itemId: string): HTMLElement | null {
    const item = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[data-gantt-item-id]'))
      .find((element) => element.dataset['ganttItemId'] === itemId);
    item?.focus();
    return item ?? null;
  }

  private scheduleFocusAfterRender(itemId: string): void {
    this.cancelFocusAfterRender();
    this.focusAfterRender = afterNextRender({
      write: () => {
        this.focusAfterRender = null;
        if (this.destroyed ||
            this.activeEditor !== null ||
            this.contract.focusItemId !== itemId)
          {return;}
        this.restoredFocusId = itemId;
        this.focusItem(itemId);
      }
    }, { injector: this.injector, manualCleanup: true });
  }

  private cancelFocusAfterRender(): void {
    this.focusAfterRender?.destroy();
    this.focusAfterRender = null;
  }
}

@Component({ selector: 'coglatas-tree-grid', standalone: true, imports: [CoglatasAdapterShellComponent], template: '<coglatas-adapter-shell adapter="tree-grid" [ariaLabel]="contract.ariaLabel" [presentation]="presentation" [state]="state" label="Tree grid fallback" />', changeDetection: ChangeDetectionStrategy.OnPush })
export class CoglatasTreeGridComponent extends CoglatasAdapterShellInput { @Input({ required: true }) contract!: CoglatasTreeGridContract<object>; }

@Component({ selector: 'coglatas-scheduler', standalone: true, imports: [CoglatasAdapterShellComponent], template: '<coglatas-adapter-shell adapter="scheduler" [ariaLabel]="contract.ariaLabel" [presentation]="presentation" [state]="state" label="Scheduler fallback" />', changeDetection: ChangeDetectionStrategy.OnPush })
export class CoglatasSchedulerComponent extends CoglatasAdapterShellInput { @Input({ required: true }) contract!: CoglatasSchedulerContract<object>; }
