import { Component, input } from '@angular/core';
import { AQ_WORKSPACE_COPY } from './aq-workspace-copy';

export interface QuestionSummary {
  fieldKey: string;
  label?: string;
  denominator: number;
  mean?: number;
  distribution?: { optionKey: string; responses: number }[];
}

@Component({
  selector: 'app-aq-question-summary-card',
  template: `
    <h3>{{ question().label ?? question().fieldKey }}</h3>
    <p>{{ copy.validAnswers }} {{ question().denominator }}
      @if (question().mean !== undefined) { · {{ copy.mean }} {{ question().mean }} }
    </p>
    @for (option of question().distribution ?? []; track option.optionKey) {
      <div class="aq-distribution">
        <p><span>{{ option.optionKey }}</span><strong>{{ option.responses }}{{ copy.countSuffix }}</strong></p>
        @if (canShowBar(option.responses, question().denominator)) {
          <meter aria-hidden="true" min="0" [attr.max]="question().denominator"
            [attr.value]="option.responses"></meter>
        }
      </div>
    }
  `,
  styles: `
    :host { display: block; min-inline-size: 0; }
    h3 { color: var(--coglatas-color-text-heading); margin: 0; font-size: 1rem; }
    p { margin: .35rem 0; }
    .aq-distribution { margin-block-start: .75rem; }
    .aq-distribution p { display: flex; flex-wrap: wrap; justify-content: space-between; gap: .5rem; }
    .aq-distribution meter { display: block; inline-size: 100%; block-size: 1rem; accent-color: var(--coglatas-color-action-primary); }
  `,
})
export class AqQuestionSummaryCard {
  readonly question = input.required<QuestionSummary>();
  protected readonly copy = AQ_WORKSPACE_COPY;

  protected canShowBar(count: number, denominator: number): boolean {
    return Number.isSafeInteger(count) && Number.isSafeInteger(denominator) &&
      denominator > 0 && count >= 0 && count <= denominator;
  }
}
