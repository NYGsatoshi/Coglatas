import { I18nService } from '../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, OnChanges, OnDestroy, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideDownload, LucideFile, LucideFolderKanban } from '@lucide/angular';

import { AppInlineLoadingComponent } from '../loading/app-inline-loading/app-inline-loading.component';
import { WorkStatusBadgeComponent } from '../ui/work-status/work-status-badge.component';
import { ContinueWorkingFacade } from './continue-working.facade';

@Component({
  selector: 'app-continue-working-panel',
  standalone: true,
  imports: [
    RouterLink,
    LucideDownload,
    LucideFile,
    LucideFolderKanban,
    AppInlineLoadingComponent,
    WorkStatusBadgeComponent,
  ],
  templateUrl: './continue-working-panel.component.html',
  styleUrl: './continue-working-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContinueWorkingPanelComponent implements OnChanges, OnDestroy {
  readonly i18n = inject(I18nService);
  private readonly facade = inject(ContinueWorkingFacade);

  @Input({ required: true }) workspaceId!: string;
  @Input() canCreateResearch = false;
  @Input() canBrowseFiles = false;

  readonly view = this.facade.view;

  ngOnChanges(): void {
    this.facade.activate(this.workspaceId);
  }

  ngOnDestroy(): void {
    this.facade.release();
  }

  retry(): void {
    this.facade.retry();
  }

  downloadFile(resourceId: string): void {
    this.facade.downloadFile(resourceId);
  }
}
