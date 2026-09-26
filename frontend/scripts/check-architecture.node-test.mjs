import assert from 'node:assert/strict';
import test from 'node:test';

import {
  findAgGridEnterpriseImports,
  findDisallowedSignalrImports,
  findDisallowedSyncfusionImports,
  findLegacyThemeTokens
} from './check-architecture.mjs';

test('rejects direct Syncfusion imports from a feature path', () => {
  const offenders = findDisallowedSyncfusionImports([
    {
      path: '/repo/frontend/src/app/features/files/files-page.component.ts',
      source: "import { UploaderComponent } from '@syncfusion/ej2-angular-inputs';"
    }
  ]);

  assert.deepEqual(offenders, ['/repo/frontend/src/app/features/files/files-page.component.ts']);
});

test('allows Syncfusion imports only in approved adapter locations', () => {
  const offenders = findDisallowedSyncfusionImports([
    {
      path: '/repo/frontend/src/app/shared/ui/adapters/syncfusion/syncfusion-uploader.adapter.ts',
      source: "import { UploaderComponent } from '@syncfusion/ej2-angular-inputs';"
    },
    {
      path: '/repo/frontend/src/app/shared/ui/adapters/syncfusion/syncfusion-uploader.adapter.ts',
      source: "import { UploaderComponent } from '@syncfusion/ej2-angular-inputs';"
    },
    {
      path: '/repo/frontend/src/app/shared/ui/adapters/syncfusion/syncfusion-gantt.component.ts',
      source: "import { GanttModule } from '@syncfusion/ej2-angular-gantt';"
    }
  ]);

  assert.deepEqual(offenders, []);
});

test('rejects direct SignalR imports outside the realtime transport adapter', () => {
  const offenders = findDisallowedSignalrImports([
    {
      path: '/repo/frontend/src/app/features/messaging/messaging.facade.ts',
      source: "import { HubConnectionBuilder } from '@microsoft/signalr';"
    },
    {
      path: '/repo/frontend/src/app/core/realtime/signalr-realtime.transport.ts',
      source: "import { HubConnectionBuilder } from '@microsoft/signalr';"
    }
  ]);

  assert.deepEqual(offenders, ['/repo/frontend/src/app/features/messaging/messaging.facade.ts']);
});

test('rejects AG Grid Enterprise from every frontend boundary', () => {
  assert.deepEqual(findAgGridEnterpriseImports([
    { path: '/repo/frontend/src/app/features/projects/project-board.ts', source: "import 'ag-grid-enterprise';" },
    { path: '/repo/frontend/src/app/features/projects/project-grid.ts', source: "const grid = import('ag-grid-enterprise/charts');" },
    { path: '/repo/frontend/src/app/shared/grid/community.ts', source: "import { GridApi } from 'ag-grid-community';" }
  ]), [
    '/repo/frontend/src/app/features/projects/project-board.ts',
    '/repo/frontend/src/app/features/projects/project-grid.ts'
  ]);
});

test('rejects legacy and undefined theme token namespaces from active components', () => {
  const offenders = findLegacyThemeTokens([
    { path: '/repo/frontend/src/app/shared/uploader.ts', source: 'background: var(--coglatas-surface-raised);' },
    { path: '/repo/frontend/src/app/shared/card.scss', source: 'background: var(--coglatas-color-bg-subtle);' },
    { path: '/repo/frontend/src/app/features/admin/audit.scss', source: 'color: var(--coglatas-color-text-on-action);' },
    { path: '/repo/frontend/src/app/shared/good.scss', source: 'background: var(--coglatas-color-bg-surface-subtle); color: var(--coglatas-color-text-inverse);' }
  ]);

  assert.deepEqual(offenders, [
    '/repo/frontend/src/app/shared/uploader.ts',
    '/repo/frontend/src/app/shared/card.scss',
    '/repo/frontend/src/app/features/admin/audit.scss'
  ]);
});
