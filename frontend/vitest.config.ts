import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

const isCi = process.env['CI'] === 'true' || process.env['GITHUB_ACTIONS'] === 'true';

export default defineConfig({
  test: {
    server: {
      deps: {
        // Syncfusion's Angular wrappers publish CommonJS interop through
        // @syncfusion/ej2-angular-base. Keep the scope vendor-specific so Vitest
        // runs these packages through Vite instead of native Node ESM loading.
        inline: [/@syncfusion\/ej2-angular-/u],
      },
    },
    // GitHub Actions has a finite per-job log budget. The default Vitest reporter
    // emits one verbose line for every spec file, which is excessive for this
    // 1,000+ test suite. Keep local output unchanged while CI uses the compact
    // dot reporter; failures and the final summary are still printed.
    reporters: isCi ? ['dot'] : ['default'],
    // Opt in only when the hosted frontend job runs Vitest alongside the
    // bounded two-worker static Playwright suite. Avoid overcommitting CPUs:
    // both suites have deadline-sensitive Angular TestBed/hooks. Local and
    // independent test jobs retain Vitest's default pool and worker scheduling.
    pool: isCi && process.env['COGLATAS_FRONTEND_VITEST_MAX_WORKERS'] === '2' ? 'threads' : undefined,
    maxWorkers: isCi && process.env['COGLATAS_FRONTEND_VITEST_MAX_WORKERS'] === '2' ? 2 : undefined,
    coverage: {
      // The scanner's project root is the repository, one level above Angular.
      // Emit resolvable frontend/src paths without changing any coverage counts.
      reporter: [
        ['lcovonly', { projectRoot: fileURLToPath(new URL('../', import.meta.url)) }],
        'text-summary',
      ],
    },
    // Angular's test builder otherwise reuses a non-isolated fork across test
    // files. Each file owns TestBed and jsdom state, so isolate it to release
    // accumulated state before the next file is scheduled.
    isolate: true,
    setupFiles: ['./vitest.setup.ts'],
  },
});
