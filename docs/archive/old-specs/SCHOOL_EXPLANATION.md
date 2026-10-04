# School Explanation

Coglatas is a private collaboration and production-tracking system for a school or organization. It helps users organize school work into spaces, groups, messages, announcements, projects, tasks, files, and simple forms.

## What The System Does

The system lets approved users sign in, work in a school workspace, create groups and channels, send direct messages, publish announcements, manage production projects, assign tasks, upload files, and track notifications.

## What Data It Stores

It stores account details such as name, email, login status, tenant membership, roles, workspaces, groups, posts, messages, announcements, projects, tasks, comments, uploaded-file metadata, uploaded-file contents, audit logs, and security events.

It should not store production passwords in documentation or source code. API tokens, invite tokens, webhook secrets, and passwords are handled as sensitive data and must not be shown in normal admin screens.

## How Access Is Controlled

Users must sign in. Access is based on their school/tenant membership and their role. A school administrator can manage users and settings for their own school. A normal user can only see the workspaces, groups, channels, projects, files, and messages they are allowed to access.

Important checks happen on the server, not only in the browser. Hiding a button in the user interface is not treated as security by itself.

## How School Data Is Separated

Each school or organization is a tenant. Tenant data is tagged with a tenant ID and filtered by the server. A user in one tenant should not see another tenant's workspaces, projects, messages, files, notifications, or audit logs.

The system has automated tenant isolation tests, but a full browser/API tenant isolation test suite is still required before broad SaaS use.

## What Admins Can Do

Tenant admins can manage users, view tenant settings, view usage and quota information, manage tenant-level features, and view their own tenant audit logs.

Platform admins can manage tenants and platform-level settings. Platform admin access should be limited to trusted operators and disabled or tightly controlled in a single-school on-prem installation.

## What Is Not Implemented Yet

The system does not yet include end-to-end encrypted messaging, voice/video calls, live streaming, full billing, advanced SSO, full restore, password reset, a production object-storage adapter, or a complete external integration system.

## How Risks Are Managed

The pilot should use limited users, test data where possible, secure passwords, HTTPS in production-like environments, restricted admin accounts, routine backups, and a documented restore rehearsal. Administrators should record any failed workflow instead of working around it silently.

## How Pilot Use Should Be Limited

Use the system first for a controlled school demo or small internal pilot. Avoid broad SaaS rollout or high-stakes production data until object storage, HTTP tenant isolation tests, PostgreSQL search isolation tests, backup/restore drill evidence, and production security settings are complete.
