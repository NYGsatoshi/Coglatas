import { I18nService } from '../../../core/i18n/i18n.service';
import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

export interface AppBreadcrumbItem {
  readonly label: string;
  readonly url?: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-breadcrumbs',
  standalone: true,
  imports: [RouterLink],
  template: `
    <nav class="breadcrumbs" [attr.aria-label]="i18n.translate('shared.breadcrumbs')">
      <ol>
        @for (item of items; track item.label; let last = $last) {
          <li>
            @if (item.url && !last) {
              <a [routerLink]="item.url">{{ item.label }}</a>
            } @else {
              <span [attr.aria-current]="last ? 'page' : null">{{ item.label }}</span>
            }
          </li>
        }
      </ol>
    </nav>
  `,
  styles: [
    `
      ol {
        display: flex;
        flex-wrap: wrap;
        gap: 0.35rem;
        align-items: center;
        margin: 0;
        padding: 0;
        list-style: none;
        color: #64748b;
        font-size: 0.875rem;
      }

      li:not(:last-child)::after {
        content: '/';
        margin-left: 0.35rem;
        color: #94a3b8;
      }

      a {
        color: #2563eb;
      }
    `
  ],
})
export class AppBreadcrumbsComponent {
  readonly i18n = inject(I18nService);
  @Input() items: readonly AppBreadcrumbItem[] = [];
}
