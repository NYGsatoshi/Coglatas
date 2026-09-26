import { moduleMetadata, type Meta, type StoryObj } from '@storybook/angular';

import { FrontendApiError } from '../core/api/api-error.model';
import { AppAuditReasonDialogComponent } from './dialog/app-audit-reason-dialog/app-audit-reason-dialog.component';
import { AppConfirmDialogComponent } from './dialog/app-confirm-dialog/app-confirm-dialog.component';
import { AppEmptyStateComponent } from './empty-state/app-empty-state/app-empty-state.component';
import { AppErrorBannerComponent } from './error/app-error-banner/app-error-banner.component';
import { AppErrorSummaryComponent } from './error/app-error-summary/app-error-summary.component';
import { AppFieldErrorComponent } from './form/app-field-error/app-field-error.component';
import { AppFormActionsComponent } from './form/app-form-actions/app-form-actions.component';
import { AppInlineLoadingComponent } from './loading/app-inline-loading/app-inline-loading.component';
import { AppSkeletonComponent } from './loading/app-skeleton/app-skeleton.component';
import { AppBreadcrumbsComponent } from './navigation/app-breadcrumbs/app-breadcrumbs.component';
import { AppPageLocalSearchComponent, PageLocalSearchRow } from './navigation/app-page-local-search/app-page-local-search.component';
import { AppPermissionDeniedComponent } from './permission/app-permission-denied/app-permission-denied.component';
import { AppPreviewDisabledComponent } from './permission/app-preview-disabled/app-preview-disabled.component';
import { AppSafeNotFoundComponent } from './permission/app-safe-not-found/app-safe-not-found.component';

const sampleError: FrontendApiError = {
  code: 'ValidationError',
  message: '入力内容を確認してください。',
  details: [{ code: 'Required', message: '必須項目を入力してください。', target: 'reason' }],
  requestId: 'req-story-0001',
  redactionApplied: true,
  httpStatus: 400,
  localErrorId: 'local-story-0001'
};

const localOnlyError: FrontendApiError = {
  code: 'NetworkError',
  message: '通信に失敗しました。',
  details: [],
  redactionApplied: true,
  httpStatus: 0,
  localErrorId: 'local-story-0002'
};

const rows: readonly PageLocalSearchRow[] = [
  { id: 'row-1', searchText: 'サンプル申請 下書き' },
  { id: 'row-2', searchText: 'サンプル通知 確認済み' }
];

const meta: Meta = {
  title: 'Shared/States',
  decorators: [
    moduleMetadata({
      imports: [
        AppAuditReasonDialogComponent,
        AppBreadcrumbsComponent,
        AppConfirmDialogComponent,
        AppEmptyStateComponent,
        AppErrorBannerComponent,
        AppErrorSummaryComponent,
        AppFieldErrorComponent,
        AppFormActionsComponent,
        AppInlineLoadingComponent,
        AppPageLocalSearchComponent,
        AppPermissionDeniedComponent,
        AppPreviewDisabledComponent,
        AppSafeNotFoundComponent,
        AppSkeletonComponent
      ]
    })
  ],
  args: {
    sampleError,
    localOnlyError,
    rows
  },
  render: (args) => ({
    props: args,
    template: `
      <main style="display:grid;gap:1rem;max-width:760px;padding:1rem;font-family:system-ui,sans-serif;">
        <app-breadcrumbs [items]="[{ label: 'ホーム', url: '/' }, { label: '共有UI' }]" />
        <app-page-local-search [rows]="rows" />
        <app-empty-state title="表示する項目がありません。" message="条件を変更するか、後でもう一度確認してください。" actionLabel="条件を変更" />
        <app-error-banner [error]="sampleError" />
        <app-permission-denied />
        <app-safe-not-found />
        <app-preview-disabled />
      </main>
    `
  })
};

export default meta;

type Story = StoryObj;

export const Default: Story = {};

export const LightTheme: Story = {
  decorators: [(story) => ({ template: `<div data-coglatas-theme="light">${  story().template  }</div>`, props: story().props })]
};

export const FocusVisible: Story = {
  render: () => ({ template: '<main style="padding:1rem"><app-empty-state actionLabel="Focused action" /></main>' })
};

export const Loading: Story = {
  render: () => ({
    template: `
      <main style="display:grid;gap:1rem;max-width:620px;padding:1rem;font-family:system-ui,sans-serif;">
        <app-inline-loading />
        <app-skeleton [lines]="4" />
      </main>
    `
  })
};

export const Empty: Story = {
  render: () => ({
    template: `
      <main style="max-width:620px;padding:1rem;font-family:system-ui,sans-serif;">
        <app-empty-state title="表示する項目がありません。" message="検索条件に一致する項目はありません。" actionLabel="検索条件をクリア" />
      </main>
    `
  })
};

export const Error: Story = {
  render: (args) => ({
    props: args,
    template: `
      <main style="display:grid;gap:1rem;max-width:680px;padding:1rem;font-family:system-ui,sans-serif;">
        <app-error-banner [error]="sampleError" />
        <app-error-banner [error]="localOnlyError" />
      </main>
    `
  })
};

export const PermissionDenied: Story = {
  render: () => ({
    template: `
      <main style="max-width:620px;padding:1rem;font-family:system-ui,sans-serif;">
        <app-permission-denied />
      </main>
    `
  })
};

export const SafeNotFound: Story = {
  render: () => ({
    template: `
      <main style="max-width:620px;padding:1rem;font-family:system-ui,sans-serif;">
        <app-safe-not-found />
      </main>
    `
  })
};

export const LongText: Story = {
  render: (args) => ({
    props: args,
    template: `
      <main style="display:grid;gap:1rem;max-width:520px;padding:1rem;font-family:system-ui,sans-serif;">
        <app-error-banner [error]="sampleError" title="非常に長い説明を含むエラー表示の確認" />
        <app-empty-state
          title="表示する項目がありません。"
          message="条件を変更しても項目が表示されない場合は、しばらく時間をおいてからもう一度確認してください。"
        />
      </main>
    `
  })
};

export const Mobile: Story = {
  parameters: {
    viewport: {
      defaultViewport: 'mobile1'
    }
  },
  render: (args) => ({
    props: args,
    template: `
      <main style="display:grid;gap:1rem;width:320px;padding:0.75rem;font-family:system-ui,sans-serif;">
        <app-page-local-search [rows]="rows" />
        <app-inline-loading />
        <app-permission-denied />
      </main>
    `
  })
};
