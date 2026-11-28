import { TestBed } from '@angular/core/testing';
import { AqQuestionSummaryCard, QuestionSummary } from './aq-summary-card';

describe('AqQuestionSummaryCard', () => {
  function render(question: QuestionSummary) {
    const fixture = TestBed.createComponent(AqQuestionSummaryCard);
    fixture.componentRef.setInput('question', question);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('uses the released denominator and each released count without deriving missing categories', () => {
    const root = render({
      fieldKey: 'choice', label: 'Choices', denominator: 5,
      distribution: [{ optionKey: 'first', responses: 4 }, { optionKey: 'second', responses: 3 }],
    });
    const meters = Array.from(root.querySelectorAll<HTMLMeterElement>('meter'));
    expect(meters.map(meter => [meter.value, meter.max])).toEqual([[4, 5], [3, 5]]);
    expect(root.textContent).toContain('4件');
    expect(root.textContent).toContain('3件');
    expect(meters.every(meter => meter.getAttribute('aria-hidden') === 'true')).toBe(true);
  });

  it('keeps text counts but omits a misleading bar for an invalid or empty denominator', () => {
    const empty = render({ fieldKey: 'empty', denominator: 0, distribution: [{ optionKey: 'first', responses: 0 }] });
    expect(empty.querySelectorAll('meter')).toHaveLength(0);
    expect(empty.textContent).toContain('0件');
    const inconsistent = render({ fieldKey: 'count', denominator: 2, distribution: [{ optionKey: 'first', responses: 3 }] });
    expect(inconsistent.querySelectorAll('meter')).toHaveLength(0);
    expect(inconsistent.textContent).toContain('3件');
  });

  it('does not manufacture a distribution for a mean-only or omitted distribution', () => {
    const root = render({ fieldKey: 'number', denominator: 4, mean: 0 });
    expect(root.textContent).toContain('平均 0');
    expect(root.querySelectorAll('meter')).toHaveLength(0);
  });
});
