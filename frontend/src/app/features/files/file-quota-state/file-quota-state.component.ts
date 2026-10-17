import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { injectFilesI18n } from '../../../core/i18n/providers/files-i18n';
import { FileMessagePresentation, renderFileMessage } from '../files-presentation';
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
  readonly i18n = injectFilesI18n();

  @Input({ required: true }) quota!: FileQuotaViewModel;

  message(value: FileMessagePresentation): string {
    return renderFileMessage(this.i18n, value);
  }

  formatBytes(bytes: number): string {
    return `${this.i18n.formatNumber(Math.max(0, Math.round(bytes / 1024 / 1024)))} MB`;
  }
}
