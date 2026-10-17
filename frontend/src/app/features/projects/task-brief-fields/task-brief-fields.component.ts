import { injectProjectsI18n } from '../../../core/i18n/providers/projects-i18n';
import { projectUiText, ProjectUiCopy } from '../projects.i18n';
import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

import { TASK_BRIEF_FIELD_MAX_LENGTH } from '../projects.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-task-brief-fields',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <fieldset class="task-brief" data-testid="task-brief-fields">
      <legend>{{ t('Task brief') }}</legend>
      <p class="task-brief__intro" [id]="helpId">
        {{ t('Optional, Task-specific guidance. Authorized Project context is shown separately and is not copied into these fields.') }}
      </p>

      <div class="task-brief__fields">
        <label [attr.for]="fieldId('goal')">
          <span class="task-brief__label">{{ t('Goal') }} <small>{{ t('(optional)') }}</small></span>
          <span class="task-brief__source" data-testid="task-brief-goal-source">{{ sourceLabel(goalControl) }}</span>
          <textarea
            [id]="fieldId('goal')"
            [formControl]="goalControl"
            rows="3"
            [maxLength]="maxLength"
            [readonly]="readonly"
            [attr.aria-invalid]="isInvalid(goalControl, goalErrors) ? 'true' : null"
            [attr.aria-describedby]="describedBy(goalControl, goalErrors, t('Goal'), 'goal')"
            data-testid="task-brief-goal-input"
          ></textarea>
          <small [id]="limitId('goal')">{{ t('What should be true when this Task is complete. Up to {{count}} characters.', {count: i18n.formatNumber(maxLength)}) }}</small>
          @if (errorMessages(goalControl, goalErrors, 'Goal').length > 0) {
            <strong [id]="errorId('goal')" class="task-brief__error" role="alert">
              @for (message of errorMessages(goalControl, goalErrors, 'Goal'); track message) { {{ message }} }
            </strong>
          }
        </label>

        <label [attr.for]="fieldId('deliverable')">
          <span class="task-brief__label">{{ t('Deliverable') }} <small>{{ t('(optional)') }}</small></span>
          <span class="task-brief__source" data-testid="task-brief-deliverable-source">{{ sourceLabel(deliverableControl) }}</span>
          <textarea
            [id]="fieldId('deliverable')"
            [formControl]="deliverableControl"
            rows="3"
            [maxLength]="maxLength"
            [readonly]="readonly"
            [attr.aria-invalid]="isInvalid(deliverableControl, deliverableErrors) ? 'true' : null"
            [attr.aria-describedby]="describedBy(deliverableControl, deliverableErrors, t('Deliverable'), 'deliverable')"
            data-testid="task-brief-deliverable-input"
          ></textarea>
          <small [id]="limitId('deliverable')">{{ t('The concrete output to hand off or publish. Up to {{count}} characters.', {count: i18n.formatNumber(maxLength)}) }}</small>
          @if (errorMessages(deliverableControl, deliverableErrors, 'Deliverable').length > 0) {
            <strong [id]="errorId('deliverable')" class="task-brief__error" role="alert">
              @for (message of errorMessages(deliverableControl, deliverableErrors, 'Deliverable'); track message) { {{ message }} }
            </strong>
          }
        </label>

        <label [attr.for]="fieldId('constraints')">
          <span class="task-brief__label">{{ t('Constraints') }} <small>{{ t('(optional)') }}</small></span>
          <span class="task-brief__source" data-testid="task-brief-constraints-source">{{ sourceLabel(constraintsControl) }}</span>
          <textarea
            [id]="fieldId('constraints')"
            [formControl]="constraintsControl"
            rows="3"
            [maxLength]="maxLength"
            [readonly]="readonly"
            [attr.aria-invalid]="isInvalid(constraintsControl, constraintsErrors) ? 'true' : null"
            [attr.aria-describedby]="describedBy(constraintsControl, constraintsErrors, t('Constraints'), 'constraints')"
            data-testid="task-brief-constraints-input"
          ></textarea>
          <small [id]="limitId('constraints')">{{ t('Boundaries, requirements, or conditions to preserve. Up to {{count}} characters.', {count: i18n.formatNumber(maxLength)}) }}</small>
          @if (errorMessages(constraintsControl, constraintsErrors, 'Constraints').length > 0) {
            <strong [id]="errorId('constraints')" class="task-brief__error" role="alert">
              @for (message of errorMessages(constraintsControl, constraintsErrors, 'Constraints'); track message) { {{ message }} }
            </strong>
          }
        </label>
      </div>

      <section class="task-brief__review" aria-labelledby="task-brief-review-heading" data-testid="task-brief-review">
        <h3 id="task-brief-review-heading">{{ t('Review before starting') }}</h3>
        <dl>
          <div data-testid="task-brief-review-goal"><dt>{{ t('Goal') }}</dt><dd>{{ reviewValue(goalControl) }}</dd></div>
          <div data-testid="task-brief-review-deliverable"><dt>{{ t('Deliverable') }}</dt><dd>{{ reviewValue(deliverableControl) }}</dd></div>
          <div data-testid="task-brief-review-constraints"><dt>{{ t('Constraints') }}</dt><dd>{{ reviewValue(constraintsControl) }}</dd></div>
        </dl>
      </section>
    </fieldset>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .task-brief { min-width: 0; margin: 0; padding: 1rem; border: 1px solid var(--coglatas-color-border-default); border-radius: .75rem; }
    .task-brief legend { padding: 0 .35rem; font-size: 1.05rem; font-weight: 750; }
    .task-brief__intro { margin: 0 0 1rem; color: var(--coglatas-color-text-secondary); }
    .task-brief__fields { display: grid; gap: 1rem; }
    .task-brief label { display: grid; min-width: 0; gap: .375rem; }
    .task-brief__label { display: flex; flex-wrap: wrap; align-items: baseline; gap: .3rem; font-weight: 700; }
    .task-brief__label small { color: var(--coglatas-color-text-secondary); font-weight: 500; }
    .task-brief__source { justify-self: start; padding: .2rem .5rem; border-radius: 999px; background: var(--coglatas-color-bg-selected); color: var(--coglatas-color-action-primary); font-size: .78rem; font-weight: 700; }
    .task-brief textarea { width: 100%; min-width: 0; box-sizing: border-box; resize: vertical; padding: .625rem .75rem; border: 1px solid var(--coglatas-color-border-strong); border-radius: .5rem; background: var(--coglatas-color-bg-control); color: var(--coglatas-color-text-primary); font: inherit; }
    .task-brief textarea[readonly] { background: var(--coglatas-color-bg-surface-subtle); color: var(--coglatas-color-text-secondary); }
    .task-brief label > small { color: var(--coglatas-color-text-secondary); }
    .task-brief__error { color: var(--coglatas-color-danger); font-size: .8rem; }
    .task-brief__review { min-width: 0; margin-top: 1.25rem; padding-top: 1rem; border-top: 1px solid var(--coglatas-color-border-default); }
    .task-brief__review h3 { margin: 0 0 .75rem; font-size: 1rem; }
    .task-brief__review dl { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: .75rem; margin: 0; }
    .task-brief__review dl > div { min-width: 0; padding: .75rem; border-radius: .5rem; background: var(--coglatas-color-bg-surface-subtle); }
    .task-brief__review dt { font-weight: 700; }
    .task-brief__review dd { margin: .35rem 0 0; color: var(--coglatas-color-text-secondary); overflow-wrap: anywhere; white-space: pre-wrap; }
    @media (max-width: 720px) { .task-brief__review dl { grid-template-columns: 1fr; } }
  `],
})
export class TaskBriefFieldsComponent {
  readonly i18n = injectProjectsI18n();
  readonly t = (text: ProjectUiCopy, parameters: Readonly<Record<string, string | number>> = {}): string => projectUiText(this.i18n, text, parameters);
  @Input({ required: true }) goalControl!: FormControl<string>;
  @Input({ required: true }) deliverableControl!: FormControl<string>;
  @Input({ required: true }) constraintsControl!: FormControl<string>;
  @Input() readonly = false;
  /** Allows each consuming form to keep labels and error targets unique. */
  @Input() inputIdPrefix = 'task-brief';
  @Input() goalErrors: readonly string[] = [];
  @Input() deliverableErrors: readonly string[] = [];
  @Input() constraintsErrors: readonly string[] = [];

  readonly maxLength = TASK_BRIEF_FIELD_MAX_LENGTH;

  sourceLabel(control: FormControl<string>): string {
    return control.value.trim().length > 0 ? this.t('Task-specific') : this.t('Not set');
  }

  reviewValue(control: FormControl<string>): string {
    return control.value.trim() || this.t('Not set');
  }

  get helpId(): string { return `${this.inputIdPrefix}-help`; }

  fieldId(field: 'goal' | 'deliverable' | 'constraints'): string {
    return `${this.inputIdPrefix}-${field}`;
  }

  limitId(field: 'goal' | 'deliverable' | 'constraints'): string {
    return `${this.inputIdPrefix}-${field}-limit`;
  }

  errorId(field: 'goal' | 'deliverable' | 'constraints'): string {
    return `${this.inputIdPrefix}-${field}-error`;
  }

  describedBy(
    control: FormControl<string>,
    externalErrors: readonly string[],
    label: string,
    field: 'goal' | 'deliverable' | 'constraints',
  ): string {
    const base = `${this.helpId} ${this.limitId(field)}`;
    return this.errorMessages(control, externalErrors, label).length > 0
      ? `${base} ${this.errorId(field)}`
      : base;
  }

  errorMessages(
    control: FormControl<string>,
    externalErrors: readonly string[],
    label: string,
  ): readonly string[] {
    const messages = [...externalErrors];
    if (control.touched && control.hasError('maxlength')) {
      const local = this.t('{{field}} must be {{count}} characters or fewer.', {field: label, count: this.i18n.formatNumber(this.maxLength)});
      if (!messages.includes(local)) {
        messages.push(local);
      }
    }
    return messages;
  }

  isInvalid(control: FormControl<string>, externalErrors: readonly string[]): boolean {
    return externalErrors.length > 0 || (control.touched && control.hasError('maxlength'));
  }
}
