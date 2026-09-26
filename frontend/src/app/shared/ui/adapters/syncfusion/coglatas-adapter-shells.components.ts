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
  template: `<coglatas-adapter-shell adapter="kanban" [ariaLabel]="contract.ariaLabel" [presentation]="presentation" [state]="state" label="Kanban">
    <p class="coglatas-kanban__feedback" aria-live="polite" role="status">{{ contract.feedback }}</p>
    <div class="coglatas-kanban" [class.coglatas-kanban--narrow]="presentation === 'narrow'" data-testid="coglatas-kanban-board">
      @for (column of contract.columns; track column.id) {
        <section class="coglatas-kanban__column" [attr.aria-label]="column.label + ', ' + column.cardCount + ' cards'" (dragover)="allowDrop($event)" (drop)="dropAtEnd(column.id, $event)">
          <header><h3>{{ column.label }}</h3><span>{{ column.cardCount }} cards</span></header>
          @if (column.hasWipWarning) {
            <p class="coglatas-kanban__warning" role="status">Warning: WIP limit {{ column.wipWarningLimit }} exceeded.</p>
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
                [attr.aria-label]="contract.itemTitle(item) + ', current stage ' + column.label"
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
                  <ul class="coglatas-kanban__metadata" aria-label="Task indicators">
                    @for (metadata of contract.itemMetadata(item); track metadata) { <li>{{ metadata }}</li> }
                  </ul>
                }
                <div class="coglatas-kanban__actions">
                  @if (contract.canOpenItem(item)) { <button type="button" (click)="activate(item)">Open details</button> }
                  @if (contract.canMoveItem(item) && contract.busyItemId !== contract.itemIdentity(item)) {
                    <button type="button" [attr.aria-expanded]="movingItemId === contract.itemIdentity(item)" (click)="openMove(item)">Move</button>
                  }
                  @if (contract.busyItemId === contract.itemIdentity(item)) { <span role="status">Saving move…</span> }
                </div>
                @if (movingItemId === contract.itemIdentity(item)) {
                  <form class="coglatas-kanban__move" (submit)="submitMove(item, $event)" (keydown.escape)="cancelMove(item, $event)">
                    <p>Current stage: <strong>{{ column.label }}</strong></p>
                    <label>Target stage
                      <select [value]="moveTargetStatus" (change)="changeTarget($any($event.target).value)">
                        @for (target of contract.columns; track target.id) {
                          <option [value]="target.id" [selected]="target.id === moveTargetStatus" [disabled]="!contract.canRequestTransition(item, target.id)">{{ target.label }}</option>
                        }
                      </select>
                    </label>
                    <label>Position
                      <select [value]="movePosition" (change)="movePosition = $any($event.target).value">
                        <option value="end" [selected]="movePosition === 'end'">End of stage</option>
                        <option value="start" [selected]="movePosition === 'start'">Start of stage</option>
                        @for (neighbor of positionItems(item); track contract.itemIdentity(neighbor)) {
                          <option [value]="'before:' + contract.itemIdentity(neighbor)" [selected]="movePosition === 'before:' + contract.itemIdentity(neighbor)">Before {{ contract.itemTitle(neighbor) }}</option>
                        }
                      </select>
                    </label>
                    @if (targetRequiresReason()) {
                      <label>Reason<textarea required maxlength="1000" [value]="moveReason" (input)="moveReason = $any($event.target).value"></textarea></label>
                    }
                    <div><button type="submit" [disabled]="targetRequiresReason() && !moveReason.trim()">Apply move</button><button type="button" (click)="cancelMove(item)">Cancel</button></div>
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
      label="Project schedule">
      <section
        class="coglatas-gantt"
        [class.coglatas-gantt--narrow]="presentation === 'narrow'"
        data-testid="coglatas-gantt-projection">
        <p class="coglatas-gantt__feedback" aria-live="polite" role="status">{{ contract.feedback }}</p>
        @if (vendorLoadError(); as loadError) {
          <p class="coglatas-gantt__warning" role="alert" data-testid="coglatas-gantt-vendor-error">{{ loadError }}</p>
        }

        @if (hasCanonicalProjection) {
          <p data-testid="coglatas-gantt-readonly">
            {{ contract.readOnly ? 'Schedule is read-only for the current actor.' : 'Authorized manual schedule changes are available.' }}
          </p>

          @if (vendorEligible) {
            <section class="coglatas-gantt__visual" aria-label="Optional visual timeline">
              <h3>Timeline chart</h3>
              <ng-container #syncfusionGanttHost />
            </section>
          }

          @if (contract.calendar; as calendar) {
            <section class="coglatas-gantt__summary" aria-labelledby="coglatas-gantt-calendar-heading">
              <h3 id="coglatas-gantt-calendar-heading">Calendar</h3>
              <p>Workspace timezone: <strong>{{ calendar.timeZone }}</strong></p>
              <p>Working days: {{ calendar.workingDays.length ? calendar.workingDays.join(', ') : 'No working-day summary available' }}.</p>
              <p>Holiday data: {{ calendar.holidaysAvailable ? 'Available' : 'Unavailable' }}.</p>
              @if (calendar.limitations.length) {
                <ul aria-label="Calendar limitations">
                  @for (limitation of calendar.limitations; track limitation) { <li>{{ limitation }}</li> }
                </ul>
              }
            </section>
          }

          <section aria-labelledby="coglatas-gantt-scheduled-heading">
            <h3 id="coglatas-gantt-scheduled-heading">Scheduled work</h3>
            @if (scheduledTasks.length) {
              <ol class="coglatas-gantt__items">
                @for (item of scheduledTasks; track item.taskId) {
                  <li><ng-container [ngTemplateOutlet]="canonicalItem" [ngTemplateOutletContext]="{ $implicit: item }"></ng-container></li>
                }
              </ol>
            } @else {
              <p>No scheduled Tasks are available.</p>
            }
          </section>

          <section aria-labelledby="coglatas-gantt-milestones-heading">
            <h3 id="coglatas-gantt-milestones-heading">Milestones</h3>
            @if (canonicalMilestones.length) {
              <ol class="coglatas-gantt__items">
                @for (item of canonicalMilestones; track item.taskId) {
                  <li><ng-container [ngTemplateOutlet]="canonicalItem" [ngTemplateOutletContext]="{ $implicit: item }"></ng-container></li>
                }
              </ol>
            } @else {
              <p>No Milestones are available.</p>
            }
          </section>

          <section aria-labelledby="coglatas-gantt-unscheduled-heading">
            <h3 id="coglatas-gantt-unscheduled-heading">Unscheduled work</h3>
            @if (unscheduledItems.length) {
              <ol class="coglatas-gantt__items">
                @for (item of unscheduledItems; track item.taskId) {
                  <li><ng-container [ngTemplateOutlet]="canonicalItem" [ngTemplateOutletContext]="{ $implicit: item }"></ng-container></li>
                }
              </ol>
            } @else {
              <p>No unscheduled Tasks are available.</p>
            }
          </section>

          <section aria-labelledby="coglatas-gantt-dependencies-heading">
            <h3 id="coglatas-gantt-dependencies-heading">Dependencies</h3>
            @if (dependencies.length) {
              <ol class="coglatas-gantt__dependencies">
                @for (dependency of dependencies; track dependency.dependencyId) {
                  <li
                    tabindex="-1"
                    [attr.data-gantt-dependency-id]="dependency.dependencyId">
                    <strong>{{ dependencyTitle(dependency) }}</strong>
                    <span>Type: {{ dependencyTypeLabel(dependency) }}. {{ dependency.editable ? 'Editable' : 'Read-only' }}.</span>
                    @if (dependency.warnings.length) {
                      <ul class="coglatas-gantt__warnings" aria-label="Dependency warnings">
                        @for (warning of dependency.warnings; track warning.code + ':' + warning.targetId) {
                          <li>{{ warning.severity }}: {{ warning.message }} ({{ warning.code }})</li>
                        }
                      </ul>
                    }
                    @if (canRemoveDependency(dependency)) {
                      <button type="button" [disabled]="isDependencyBusy(dependency)" (click)="openRemoveDependency(dependency, $event)">Remove FS dependency</button>
                    }
                  </li>
                }
              </ol>
            } @else {
              <p>No dependencies are available.</p>
            }
          </section>

          <section aria-labelledby="coglatas-gantt-warnings-heading">
            <h3 id="coglatas-gantt-warnings-heading">Schedule warnings</h3>
            @if (warnings.length) {
              <ul class="coglatas-gantt__warnings">
                @for (warning of warnings; track warning.code + ':' + warning.targetId + ':' + warning.field) {
                  <li>{{ warning.severity }}: {{ warning.message }} ({{ warning.code }})</li>
                }
              </ul>
            } @else {
              <p>No schedule warnings.</p>
            }
          </section>
        } @else {
          <p data-testid="coglatas-gantt-readonly">
            {{ contract.readOnly ? 'Schedule is read-only because the current API does not provide an authorized versioned schedule-write contract.' : 'Schedule changes are available.' }}
          </p>
          @if (contract.milestones.length) {
            <h3>Milestones</h3>
            <ul>
              @for (milestone of contract.milestones; track milestone.id) {
                <li><strong>{{ milestone.title }}</strong> {{ milestone.dueDate ?? 'No due date' }} - {{ milestone.status }}</li>
              }
            </ul>
          }
          <h3>Tasks</h3>
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
              <span class="coglatas-gantt__kind">{{ item.kind === 'milestone' ? 'Milestone' : isDerivedParent(item) ? 'Derived parent Task' : 'Task' }}</span>
              <h4>{{ item.title }}</h4>
            </div>
            @if (isBusy(item)) { <span role="status">Saving...</span> }
          </header>
          <dl class="coglatas-gantt__metadata">
            <div><dt>Stage</dt><dd>{{ item.workflowStageName ?? 'No Stage' }} ({{ stageLabel(item) }})</dd></div>
            <div><dt>Priority</dt><dd>{{ priorityLabel(item) }}</dd></div>
            <div><dt>Blocked</dt><dd>{{ item.isBlocked ? 'Blocked' : 'Not blocked' }}</dd></div>
            <div><dt>Dates</dt><dd>{{ dateLabel(item) }}</dd></div>
            <div><dt>Progress</dt><dd>{{ item.progressPercent }}%{{ item.progressIsDerived ? ' (derived)' : '' }}</dd></div>
            <div><dt>Assignee</dt><dd>{{ item.primaryAssignee?.displayName ?? 'Unassigned' }}</dd></div>
            <div><dt>Parent</dt><dd>{{ parentTitle(item) }}</dd></div>
          </dl>
          @if (item.warnings.length) {
            <ul class="coglatas-gantt__warnings" aria-label="Work item warnings">
              @for (warning of item.warnings; track warning.code + ':' + warning.field) {
                <li>{{ warning.severity }}: {{ warning.message }} ({{ warning.code }})</li>
              }
            </ul>
          }
          <div class="coglatas-gantt__actions">
            @if (canOpen(item)) {
              <button type="button" (click)="openDetails(item)">Open details</button>
            }
            @if (canEditSchedule(item)) {
              <button type="button" [disabled]="isBusy(item)" (click)="openSchedule(item, $event)">
                {{ item.kind === 'milestone' ? 'Edit Milestone date' : 'Edit dates' }}
              </button>
            }
            @if (canEditProgress(item)) {
              <button type="button" [disabled]="isBusy(item)" (click)="openProgress(item, $event)">Edit progress</button>
            }
            @if (canClearSchedule(item)) {
              <button type="button" [disabled]="isBusy(item)" (click)="openSchedule(item, $event, true)">Move to unscheduled</button>
            }
            @if (canAddDependency(item)) {
              <button type="button" [disabled]="isBusy(item)" (click)="openAddDependency(item, $event)">Add FS predecessor</button>
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
            @if (formError) { <p class="coglatas-gantt__form-error" role="alert">{{ formError }}</p> }

            @if (activeEditor === 'schedule' && activeItem; as item) {
              <form class="coglatas-gantt__form" (submit)="submitSchedule($event)">
                @if (item.kind === 'milestone') {
                  <label>Milestone date
                    <input
                      type="date"
                      name="milestoneDate"
                      required
                      [value]="milestoneDate"
                      (input)="milestoneDate = $any($event.target).value" />
                  </label>
                } @else {
                  <label>Planned start
                    <input
                      type="date"
                      name="plannedStartDate"
                      [value]="plannedStartDate"
                      (input)="plannedStartDate = $any($event.target).value" />
                  </label>
                  <label>Planned end
                    <input
                      type="date"
                      name="plannedEndDate"
                      [value]="plannedEndDate"
                      (input)="plannedEndDate = $any($event.target).value" />
                  </label>
                }
                <div class="coglatas-gantt__form-actions">
                  <button type="submit" [disabled]="editorBusy || !canEditSchedule(item)">Apply schedule</button>
                  @if (canClearSchedule(item)) {
                    <button type="button" [disabled]="editorBusy" (click)="submitClearSchedule()">Clear schedule</button>
                  }
                  <button type="button" (click)="cancelEditor()">Cancel</button>
                </div>
              </form>
            }

            @if (activeEditor === 'progress' && activeItem; as item) {
              <form class="coglatas-gantt__form" (submit)="submitProgress($event)">
                <label>Progress percent
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
                  <button type="submit" [disabled]="editorBusy">Apply progress</button>
                  <button type="button" (click)="cancelEditor()">Cancel</button>
                </div>
              </form>
            }

            @if (activeEditor === 'addDependency' && activeItem; as item) {
              <form class="coglatas-gantt__form" (submit)="submitAddDependency($event)">
                <p>Successor: <strong>{{ item.title }}</strong></p>
                <label>Finish-to-Start predecessor
                  <select
                    name="predecessorTaskId"
                    required
                    [value]="predecessorTaskId"
                    (change)="predecessorTaskId = $any($event.target).value">
                    <option value="">Select a predecessor</option>
                    @for (candidate of dependencyCandidates(item); track candidate.taskId) {
                      <option [value]="candidate.taskId">{{ candidate.title }}</option>
                    }
                  </select>
                </label>
                <p>Dates will not be moved automatically.</p>
                <div class="coglatas-gantt__form-actions">
                  <button type="submit" [disabled]="editorBusy || !predecessorTaskId">Add dependency</button>
                  <button type="button" (click)="cancelEditor()">Cancel</button>
                </div>
              </form>
            }

            @if (activeEditor === 'removeDependency' && activeDependency; as dependency) {
              <form class="coglatas-gantt__form" (submit)="submitRemoveDependency($event)">
                <p>Remove <strong>{{ dependencyTitle(dependency) }}</strong>?</p>
                <p>Dates will not be moved automatically.</p>
                <div class="coglatas-gantt__form-actions">
                  <button type="submit" [disabled]="editorBusy">Remove dependency</button>
                  <button type="button" (click)="cancelEditor()">Cancel</button>
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

  readonly vendorLoadError = signal<string | null>(null);
  activeEditor: 'schedule' | 'progress' | 'addDependency' | 'removeDependency' | null = null;
  activeItem: CoglatasGanttItem | null = null;
  activeDependency: CoglatasGanttDependency | null = null;
  plannedStartDate = '';
  plannedEndDate = '';
  milestoneDate = '';
  progressPercent = 0;
  predecessorTaskId = '';
  formError = '';

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
    if (this.activeEditor === 'progress') {return 'Edit progress';}
    if (this.activeEditor === 'addDependency') {return 'Add Finish-to-Start dependency';}
    if (this.activeEditor === 'removeDependency') {return 'Remove Finish-to-Start dependency';}
    return this.activeItem?.kind === 'milestone' ? 'Edit Milestone date' : 'Edit schedule';
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
        this.formError = 'A Milestone date is required.';
        return;
      }
    } else {
      if ((this.plannedStartDate && !this.validDateOnly(this.plannedStartDate))
        || (this.plannedEndDate && !this.validDateOnly(this.plannedEndDate))) {
        this.formError = 'Use valid calendar dates.';
        return;
      }
      if (this.plannedStartDate && this.plannedEndDate
        && this.plannedEndDate < this.plannedStartDate) {
        this.formError = 'Planned end cannot be before planned start.';
        return;
      }
      if (!this.plannedStartDate && !this.plannedEndDate && !this.canClearSchedule(item)) {
        this.formError = 'You do not have permission to clear this schedule.';
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
      this.formError = 'Progress must be a whole number from 0 to 100.';
      return;
    }
    if (item.kind === 'milestone' && value !== 0 && value !== 100) {
      this.formError = 'Milestone progress must be 0 or 100.';
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
      this.formError = 'Select an authorized predecessor.';
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
    if (!item.parentTaskId) {return 'None';}
    return this.allCanonicalItems.find((candidate) => candidate.taskId === item.parentTaskId)?.title
      ?? 'Authorized parent unavailable';
  }

  dateLabel(item: CoglatasGanttItem): string {
    if (item.kind === 'milestone') {return item.milestoneDate ?? 'Milestone date required';}
    if (!item.plannedStartDate && !item.plannedEndDate) {return 'Unscheduled';}
    return `${item.plannedStartDate ?? 'No start'} to ${item.plannedEndDate ?? 'No end'}`;
  }

  priorityLabel(item: CoglatasGanttItem): string {
    return item.priority[0].toUpperCase() + item.priority.slice(1);
  }

  stageLabel(item: CoglatasGanttItem): string {
    return item.stageCategory.replace(/([A-Z])/gu, ' $1').toLowerCase();
  }

  dependencyTitle(dependency: CoglatasGanttDependency): string {
    const predecessor = this.allCanonicalItems.find((item) => item.taskId === dependency.predecessorTaskId)?.title
      ?? 'Authorized predecessor unavailable';
    const successor = this.allCanonicalItems.find((item) => item.taskId === dependency.successorTaskId)?.title
      ?? 'Authorized successor unavailable';
    return `${predecessor} to ${successor}`;
  }

  dependencyTypeLabel(dependency: CoglatasGanttDependency): string {
    return dependency.type === 'finishToStart' ? 'Finish-to-Start' : `Legacy ${dependency.type}`;
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
        this.vendorLoadError.set('The visual timeline could not be loaded. The complete schedule list and forms remain available.');
        this.vendorFailed.emit();
        this.destroyVendor(false);
      });
      component.setInput('contract', this.contract);
      component.changeDetectorRef.detectChanges();
      this.vendorLoadError.set(null);
    } catch {
      if (!this.destroyed) {
        this.vendorLoadError.set('The visual timeline could not be loaded. The complete schedule list and forms remain available.');
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
