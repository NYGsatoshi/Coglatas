import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

import { readPublicHttpsSmokeConfiguration } from './public-https-golden-path-helpers.mjs';

const playwrightCli = fileURLToPath(new URL('../../node_modules/@playwright/test/cli.js', import.meta.url));
const userArgs = process.argv.slice(2);

process.exitCode = await main();

async function main() {
  try {
    const configuration = readPublicHttpsSmokeConfiguration(process.env);
    await waitForPublicReadiness(configuration.baseURL);
    return await runPlaywright(configuration.baseURL);
  } catch (error) {
    // Configuration failures are deliberately descriptive but contain no values.
    // The gate never writes its credentials, cookies, CSRF token, fixture IDs,
    // or response bodies to CI output.
    console.error(error instanceof Error ? error.message : 'Public HTTPS Golden Path setup failed.');
    return 1;
  }
}

function runPlaywright(baseURL) {
  const playwrightArgs = userArgs.length > 0
    ? userArgs
    : [
        'tests/ui/public-https-golden-path.spec.ts',
        '--project=chromium-desktop',
        '--retries=0',
        '--workers=1'
      ];

  return new Promise((resolve) => {
    let settled = false;
    const finish = (code) => {
      if (!settled) {
        settled = true;
        resolve(Number.isInteger(code) && code >= 0 ? code : 1);
      }
    };

    const child = spawn(process.execPath, [playwrightCli, 'test', ...playwrightArgs], {
      cwd: process.cwd(),
      env: {
        ...process.env,
        COGLATAS_PUBLIC_HTTPS_SMOKE: '1',
        PLAYWRIGHT_BASE_URL: baseURL
      },
      stdio: 'inherit'
    });

    child.once('error', () => {
      console.error('Unable to start the public HTTPS Playwright gate.');
      finish(1);
    });
    child.once('close', finish);
  });
}

async function waitForPublicReadiness(baseURL) {
  const readinessUrl = new URL('/health/ready', baseURL).toString();
  const deadline = Date.now() + 120_000;
  let lastStatus = 0;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(readinessUrl, { redirect: 'error' });
      lastStatus = response.status;
      if (response.ok) {
        return;
      }
    } catch {
      // A public deployment can be temporarily unavailable while rolling out.
      // Continue until the bounded gate deadline, then fail rather than pass.
    }

    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }

  throw new Error(
    `Public HTTPS endpoint is BLOCKED: /health/ready did not return 2xx before the gate deadline${lastStatus ? ` (last status ${lastStatus})` : ''}.`
  );
}
