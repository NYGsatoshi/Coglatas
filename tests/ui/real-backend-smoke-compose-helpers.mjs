const COMPOSE_PROJECT_NAME_MAX_LENGTH = 63,
  DEFAULT_FUNCTIONAL_PLAYWRIGHT_ARGS = Object.freeze([
    '--config',
    'playwright.functional.config.ts',
    'project-task/core-golden-journey.spec.ts',
    'files/files-fast-journey.spec.ts',
    '--project=functional-chromium',
    '--retries=0',
    '--workers=1'
  ]),
  DEFAULT_LEGACY_PLAYWRIGHT_ARGS = Object.freeze([
    'tests/ui/real-backend-smoke.spec.ts',
    '--project=chromium-desktop',
    '--retries=0',
    '--workers=1'
  ]);

export const composeV2Invocation = Object.freeze({
  command: 'docker',
  prefix: ['compose']
});

export const legacyComposeInvocation = Object.freeze({
  command: 'docker-compose',
  prefix: []
});

export function composeProjectName(parts, maxLength = COMPOSE_PROJECT_NAME_MAX_LENGTH) {
  const fallback = 'coglatas-real-backend-smoke',
    normalized = parts
      .filter((part) => part !== undefined && part !== null && String(part).trim().length > 0)
      .join('-')
      .toLowerCase()
      .replace(/[^a-z0-9_-]+/g, '-')
      .replace(/^[^a-z0-9]+/, '')
      .replace(/[-_]+$/, ''),
    withinLimit = (normalized || fallback).slice(0, maxLength).replace(/[-_]+$/, '');
  return withinLimit || 'coglatas';
}

export function getComposeProjectName(environment, processId) {
  const override = environment.REAL_BACKEND_SMOKE_COMPOSE_PROJECT_NAME;
  if (typeof override === 'string' && override.trim().length > 0) {
    return composeProjectName([override]);
  }

  return composeProjectName([
    'coglatas-real-backend-smoke',
    environment.GITHUB_RUN_ID,
    environment.GITHUB_RUN_ATTEMPT,
    environment.CI ? 'ci' : 'local',
    processId
  ]);
}

export async function selectComposeInvocation(runVersion) {
  if (await runVersion('docker', ['compose', 'version'])) {
    return composeV2Invocation;
  }

  if (await runVersion('docker-compose', ['version'])) {
    return legacyComposeInvocation;
  }

  throw new Error(
    'Docker Compose is required for the real-backend browser smoke. Install Docker Desktop or Docker Compose v2 so `docker compose version` succeeds; legacy `docker-compose version` is also supported.'
  );
}

export function isStaticAngularServerUrl(value) {
  try {
    const url = new URL(value);
    const host = url.hostname.toLowerCase();
    return url.port === '4173' && (host === '127.0.0.1' || host === 'localhost' || host === '::1');
  } catch {
    return false;
  }
}

export function isHstsPreloadedHttpUrl(value) {
  try {
    const url = new URL(value);
    const host = url.hostname.toLowerCase();
    return url.protocol === 'http:' && (host === 'app' || host.endsWith('.app'));
  } catch {
    return false;
  }
}

export function normalizeExitCode(code) {
  return Number.isInteger(code) && code >= 0 ? code : 1;
}

/** Closed wording observations only; none of these values establishes a root cause. */
export function composeFailureObservation(operation, result) {
  if (!['CONFIG', 'UP'].includes(operation) || !result || typeof result !== 'object') return null;
  const exitCode = normalizeExitCode(result.exitCode);
  if (exitCode === 0 || exitCode > 255) return null;
  const limit = 2 * 1024 * 1024;
  const available = typeof result.output === 'string';
  const length = available ? Buffer.byteLength(result.output, 'utf8') : 0;
  const overflow = result.outputOverflow === true || length > limit;
  let outputState;
  if (overflow) outputState = 'OVERFLOW';
  else if (!available) outputState = 'UNAVAILABLE';
  else if (length === 0) outputState = 'EMPTY';
  else outputState = 'AVAILABLE';
  let pattern = 'UNKNOWN';
  if (available && !overflow && result.outputOverflow === false && Number.isInteger(result.exitCode) && result.exitCode > 0) {
    const text = result.output.toLowerCase();
    // Docker daemon/rootless troubleshooting and Distribution HTTP API error wording.
    const matches = [
      ['DAEMON_CONNECTION_TEXT', text.includes('cannot connect to the docker daemon')],
      ['MANIFEST_UNKNOWN_TEXT', text.includes('manifest unknown') || text.includes('manifest_unknown')],
      ['RATE_LIMIT_TEXT', text.includes('toomanyrequests')],
      ['NO_SPACE_TEXT', text.includes('no space left on device')],
    ].filter(([, matched]) => matched);
    if (matches.length === 1) pattern = matches[0][0];
  }
  return { operation, exitCode, outputState, capturedBytes: Math.min(length, limit), pattern };
}

/**
 * Keep the migrated Functional owners on their own Playwright config while
 * retaining the legacy smoke suite on the root tests/ui config. Playwright
 * treats CLI paths as filters inside testDir, so mixing both directories in
 * one invocation silently discovers no migrated owner tests.
 */
// eslint-disable-next-line func-style -- Keep this public helper consistent with the module's existing exported function declarations while preventing new lint debt.
export function buildRealBackendPlaywrightPlan(userArgs = [], focusedGrep = '', filesGate = '') {
  if (filesGate && !['functional-fast', 'functional-full'].includes(filesGate)) {
    throw new Error('Files owner gate must be functional-fast or functional-full.');
  }
  const grepArgs = [];
  if (focusedGrep.trim()) {
    grepArgs.push('--grep', focusedGrep.trim());
  }
  if (userArgs.length) {
    return [{ name: 'custom', args: [...userArgs, ...grepArgs] }];
  }

  if (grepArgs.length) {
    const legacyRun = {
      name: 'focused legacy real-backend suite',
      args: [...DEFAULT_LEGACY_PLAYWRIGHT_ARGS, ...grepArgs]
    };
    return filesGate ? [{
      name: 'FCI-05 Files owner',
      functionalGate: filesGate,
      args: ['--config', 'playwright.functional.config.ts', 'files/files-fast-journey.spec.ts',
        '--project=functional-chromium', '--retries=0', '--workers=1'],
    }, legacyRun] : [legacyRun];
  }

  return [
    {
      name: 'Functional real-backend owners',
      functionalGate: 'functional-full',
      args: [...DEFAULT_FUNCTIONAL_PLAYWRIGHT_ARGS]
    },
    {
      name: 'legacy real-backend regression',
      args: [...DEFAULT_LEGACY_PLAYWRIGHT_ARGS]
    }
  ];
}

export function redactSecrets(output) {
  return output
    .replace(/(POSTGRES_PASSWORD\s*[:=]\s*)[^\r\n]+/gi, '$1[redacted]')
    .replace(/(COGLATAS_[A-Z0-9_]*PASSWORD\s*[:=]\s*)[^\r\n]+/gi, '$1[redacted]')
    .replace(/(Password=)[^;\s\r\n]+/gi, '$1[redacted]')
    .replace(/(Authorization\s*:\s*)[^\r\n]+/gi, '$1[redacted]')
    .replace(/((?:Cookie|Set-Cookie)\s*:\s*)[^\r\n]+/gi, '$1[redacted]')
    .replace(/((?:X-CSRF-Token|CSRF(?:Token)?)\s*[:=]\s*)[^\r\n]+/gi, '$1[redacted]')
    .replace(/((?:Invite|Invitation)[A-Za-z-]*Token\s*[:=]\s*)[^\r\n]+/gi, '$1[redacted]')
    .replace(/("(?:password|token|authorization|cookie|csrfToken|inviteToken|invitationToken)"\s*:\s*")[^"]*(")/gi, '$1[redacted]$2');
}
