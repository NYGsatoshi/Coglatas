import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';

@Component({
  selector: 'app-coglatas-date-time-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <input
      type="datetime-local"
      [attr.aria-label]="ariaLabel"
      [value]="localValue"
      [readOnly]="readOnly"
      (input)="handleInput($event)"
    />
  `,
  styles: [`
    :host { display: block; }
    input { width: 100%; min-height: 2.75rem; box-sizing: border-box; border: 1px solid var(--coglatas-color-border-strong); border-radius: 0.5rem; padding: 0.55rem 0.7rem; background: var(--coglatas-color-bg-control); color: var(--coglatas-color-text-primary); }
    input:focus-visible { outline: var(--coglatas-focus-outline); outline-offset: var(--coglatas-focus-offset); }
    input:read-only { opacity: 0.7; }
  `]
})
export class CoglatasDateTimePickerComponent implements OnChanges {
  @Input() value: string | null = null;
  @Input() ariaLabel = 'Date and time';
  @Input() timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  @Input() readOnly = false;

  @Output() readonly valueChanged = new EventEmitter<string | null>();

  localValue = '';

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['value']) {
      this.localValue = toDateTimeLocalValue(this.value);
    }
  }

  handleInput(event: Event): void {
    const target = event.target;
    if (!(target instanceof HTMLInputElement)) {
      return;
    }

    this.localValue = target.value;
    this.valueChanged.emit(target.value || null);
  }
}

function toDateTimeLocalValue(value: string | null): string {
  if (!value) {
    return '';
  }

  const localValuePattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/u;
  if (localValuePattern.test(value)) {
    return value.slice(0, 16);
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return '';
  }

  const timezoneOffset = parsed.getTimezoneOffset() * 60_000;
  return new Date(parsed.getTime() - timezoneOffset).toISOString().slice(0, 16);
}
