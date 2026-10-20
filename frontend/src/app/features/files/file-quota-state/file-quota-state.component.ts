import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { LucideHardDrive } from '@lucide/angular';

import { injectFilesI18n } from '../../../core/i18n/providers/files-i18n';
import { FileMessagePresentation, renderFileMessage } from '../files-presentation';
import { FileQuotaViewModel } from '../files.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-file-quota-state',
  standalone: true,
  imports: [LucideHardDrive],
  template: `
    <section [class]="'quota quota--' + quota.state" [class.quota--unavailable]="quota.limitBytes <= 0" data-testid="file-quota-state">
      <svg lucideHardDrive [size]="quota.limitBytes > 0 ? 24 : 16" aria-hidden="true"></svg>
      <div class="quota__content">
        @if (quota.limitBytes > 0) {
          <p>{{ i18n.translate('files.quota.title') }}</p>
          <strong>{{ i18n.formatFileSize(quota.usedBytes) }} <span>/ {{ i18n.formatFileSize(quota.limitBytes) }}</span></strong>
          <progress [value]="quota.usedBytes" [max]="quota.limitBytes" [attr.aria-label]="i18n.translate('files.quota.title')"></progress>
          <p class="quota__status">{{ message(quota) }}</p>
        } @else {
          <p class="quota__unavailable">{{ message(quota) }}</p>
        }
      </div>
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
