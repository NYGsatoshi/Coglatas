import { Routes } from '@angular/router';

import { authSessionGuard } from './core/auth/auth-session.guard';
import { PagePlaceholderComponent } from './core/routing/page-placeholder.component';
import { SessionExpiredPageComponent } from './core/session/session-expired-page.component';
import { workspaceContextGuard } from './core/workspace/workspace-context.guard';
import { AppShellComponent } from './layout/app-shell/app-shell.component';
import { AppPermissionDeniedComponent } from './shared/permission/app-permission-denied/app-permission-denied.component';

export const routes: Routes = [
  {
    path: 'signin',
    pathMatch: 'full',
    redirectTo: 'login'
  },
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login-page/login-page.component').then((m) => m.LoginPageComponent)
  },
  {
    path: 'session-expired',
    component: SessionExpiredPageComponent
  },
  {
    path: 'register/invite',
    loadComponent: () =>
      import('./features/auth/invite-registration-page/invite-registration-page.component').then(
        (m) => m.InviteRegistrationPageComponent
      )
  },
  {
    path: 'permission-denied',
    component: AppPermissionDeniedComponent
  },
  {
    path: '',
    component: AppShellComponent,
    canActivate: [authSessionGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        redirectTo: 'workspaces'
      },
      {
        path: 'messages/saved',
        loadComponent: () =>
          import('./features/messaging/message-follow-ups-page/message-follow-ups-page.component').then(
            (m) => m.MessageFollowUpsPageComponent
          )
      },
      {
        path: 'messages/settings',
        loadComponent: () =>
          import('./features/messaging/message-settings-page/message-settings-page.component').then(
            (m) => m.MessageSettingsPageComponent
          )
      },
      {
        path: 'dm',
        data: { conversationDirectory: 'dm' },
        loadComponent: () =>
          import('./features/messaging/messages-page/messages-page.component').then(
            (m) => m.MessagesPageComponent
          )
      },
      {
        path: 'teams',
        data: { conversationDirectory: 'teams' },
        loadComponent: () =>
          import('./features/messaging/messages-page/messages-page.component').then(
            (m) => m.MessagesPageComponent
          )
      },
      {
        path: 'messages',
        loadComponent: () =>
          import('./features/messaging/messages-page/messages-page.component').then(
            (m) => m.MessagesPageComponent
          )
      },
      {
        path: 'conversations/:conversationId',
        loadComponent: () =>
          import('./features/messaging/channel-messaging-page/channel-messaging-page.component').then(
            (m) => m.ChannelMessagingPageComponent
          )
      },
      {
        path: 'workspaces/:workspaceId/channels/:conversationId',
        canActivate: [workspaceContextGuard],
        loadComponent: () =>
          import('./features/messaging/channel-messaging-page/channel-messaging-page.component').then(
            (m) => m.ChannelMessagingPageComponent
          )
      },
      {
        path: 'dm/:conversationId',
        loadComponent: () =>
          import('./features/messaging/dm-page/dm-page.component').then((m) => m.DmPageComponent)
      },
      {
        path: 'workspaces/:workspaceId/research/new',
        canActivate: [workspaceContextGuard],
        loadComponent: () =>
          import(
            './features/workspaces/workspace-research-quick-create-page/workspace-research-quick-create-page.component'
          ).then((m) => m.WorkspaceResearchQuickCreatePageComponent)
      },
      {
        path: 'workspaces/:workspaceId/projects',
        canActivate: [workspaceContextGuard],
        loadComponent: () =>
          import('./features/projects/projects-overview-page/projects-overview-page.component').then(
            (m) => m.ProjectsOverviewPageComponent
          )
      },
      {
        path: 'workspaces/:workspaceId/files',
        canActivate: [workspaceContextGuard],
        loadComponent: () =>
          import('./features/files/files-page/files-page.component').then((m) => m.FilesPageComponent)
      },
      {
        path: 'workspaces/:workspaceId/members',
        canActivate: [workspaceContextGuard],
        loadComponent: () =>
          import('./features/workspaces/workspace-members-page/workspace-members-page.component').then(
            (m) => m.WorkspaceMembersPageComponent
          )
      },
      {
        path: 'workspaces',
        loadComponent: () =>
          import('./features/workspaces/workspace-dashboard-page/workspace-dashboard-page.component').then(
            (m) => m.WorkspaceDashboardPageComponent
          )
      },
      {
        path: 'announcements',
        loadComponent: () =>
          import('./features/announcements/announcements-page/announcements-page.component').then(
            (m) => m.AnnouncementsPageComponent
          )
      },
      {
        path: 'announcements/:announcementId',
        loadComponent: () =>
          import('./features/announcements/announcements-page/announcements-page.component').then(
            (m) => m.AnnouncementsPageComponent
          )
      },
      {
        path: 'app/projects/:projectId/tasks/:taskId/reports/:artifactVersionId',
        loadComponent: () => import('./features/artifacts/report-reader-page/report-reader-page.component').then((m) => m.ReportReaderPageComponent)
      },
      {
        path: 'app/projects/:projectId/reports/:artifactVersionId',
        loadComponent: () => import('./features/artifacts/report-reader-page/report-reader-page.component').then((m) => m.ReportReaderPageComponent)
      },
      {
        path: 'projects/:projectId/tasks/:taskId/reports/:artifactVersionId',
        loadComponent: () => import('./features/artifacts/report-reader-page/report-reader-page.component').then((m) => m.ReportReaderPageComponent)
      },
      {
        path: 'projects/:projectId/reports/:artifactVersionId',
        loadComponent: () => import('./features/artifacts/report-reader-page/report-reader-page.component').then((m) => m.ReportReaderPageComponent)
      },
      {
        path: 'projects/:projectId/tasks/new',
        loadComponent: () =>
          import('./features/projects/task-create-page/task-create-page.component').then(
            (m) => m.TaskCreatePageComponent
          )
      },
      {
        path: 'projects/:projectId/tasks/:taskId',
        loadComponent: () =>
          import('./features/projects/task-detail-page/task-detail-page.component').then(
            (m) => m.TaskDetailPageComponent
          )
      },
      {
        path: 'projects/:projectId',
        loadComponent: () =>
          import('./features/projects/project-detail-page/project-detail-page.component').then(
            (m) => m.ProjectDetailPageComponent
          )
      },
      {
        path: 'tasks',
        loadComponent: () =>
          import('./features/projects/my-tasks-page/my-tasks-page.component').then((m) => m.MyTasksPageComponent)
      },
      {
        path: 'projects',
        loadComponent: () =>
          import('./features/projects/projects-overview-page/projects-overview-page.component').then(
            (m) => m.ProjectsOverviewPageComponent
          )
      },
      {
        path: 'artifacts/:artifactId',
        loadComponent: () =>
          import('./features/artifacts/artifact-detail-page/artifact-detail-page.component').then(
            (m) => m.ArtifactDetailPageComponent
          )
      },
      {
        path: 'files',
        loadComponent: () =>
          import('./features/files/files-page/files-page.component').then((m) => m.FilesPageComponent)
      },
      {
        path: 'account',
        loadComponent: () =>
          import('./features/account/account-page/account-page.component').then((m) => m.AccountPageComponent)
      },
      {
        path: 'admin/audit/findings',
        loadComponent: () =>
          import('./features/admin/audit-findings-page/audit-findings-page.component').then(
            (m) => m.AuditFindingsPageComponent
          )
      },
      {
        path: 'admin/audit/claims-evidence',
        loadComponent: () =>
          import('./features/admin/audit-claims-evidence-page/audit-claims-evidence-page.component').then(
            (m) => m.AuditClaimsEvidencePageComponent
          )
      },
      {
        loadComponent: async () =>
          (await import('./features/admin/audit-package-export-page/audit-package-export-page.component'))
            .AuditPackageExportPageComponent,
        path: 'admin/audit/package-export'
      },
      {
        path: 'admin/audit',
        loadComponent: () =>
          import('./features/admin/audit-log-page/audit-log-page.component').then((m) => m.AuditLogPageComponent)
      },
      {
        path: 'admin/invites',
        loadComponent: () =>
          import('./features/admin/invite-admin-page/invite-admin-page.component').then(
            (m) => m.InviteAdminPageComponent
          )
      },
      {
        path: 'admin/export-diagnostics',
        loadComponent: () =>
          import('./features/admin/export-diagnostics-page/export-diagnostics-page.component').then(
            (m) => m.ExportDiagnosticsPageComponent
          )
      }
    ]
  },
  {
    path: '**',
    component: PagePlaceholderComponent,
    data: {
      title: '未実装',
      summary: '準備中'
    }
  }
];
