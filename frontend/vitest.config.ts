import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

const isCi = process.env['CI'] === 'true' || process.env['GITHUB_ACTIONS'] === 'true';

// Only the credential-free Sonar sharding qualification sets this variable.
const proofShard = process.env['COGLATAS_SONAR_ANGULAR_PROOF_SHARD'];
if (proofShard !== undefined &&
    (!isCi || (proofShard !== '1/2' && proofShard !== '2/2'))) {
  throw new Error('Invalid Angular coverage qualification shard');
}

export default defineConfig({
  test: {
    ...(proofShard === undefined ? {} : { shard: proofShard }),
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
