import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { FileMessagePresentation, renderFileMessage } from '../files-presentation';
import { I18nService } from '../../../core/i18n/i18n.service';
import { FileQuotaViewModel } from '../files.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-file-quota-state',
  standalone: true,
  template: `
    <section [class]="'quota quota--' + quota.state" data-testid="file-quota-state">
      <div>
        <p>{{ i18n.translate('files.quota.title') }}</p>
        <strong>{{ message(quota) }}</strong>
      </div>
      <span>{{ formatBytes(quota.usedBytes) }} / {{ formatBytes(quota.limitBytes) }}</span>
    </section>
  `,
  styleUrl: './file-quota-state.component.scss',
})
export class FileQuotaStateComponent {
  readonly i18n = inject(I18nService);

  @Input({ required: true }) quota!: FileQuotaViewModel;

  message(value: FileMessagePresentation): string {
    return renderFileMessage(this.i18n, value);
  }

  formatBytes(bytes: number): string {
    return `${this.i18n.formatNumber(Math.max(0, Math.round(bytes / 1024 / 1024)))} MB`;
  }
}
