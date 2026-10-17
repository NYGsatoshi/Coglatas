import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

import { injectFilesI18n } from '../../../core/i18n/providers/files-i18n';
import { FileScanStatus } from '../files.types';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-file-scan-status-badge',
  standalone: true,
  template: `
    <span [class]="'scan-badge scan-badge--' + status" data-testid="scan-status-badge">
      {{ i18n.fileScanStatusLabel(status) }}
    </span>
  `,
  styleUrl: './file-scan-status-badge.component.scss',
})
export class FileScanStatusBadgeComponent {
  readonly i18n = injectFilesI18n();

  @Input({ required: true }) status: FileScanStatus = 'pending';
}
