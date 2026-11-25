import { mkdirSync, writeFileSync } from 'node:fs';
import { basename } from 'node:path';

export const filesReportInput = 'artifacts/files-attempt-report/raw.json';
export const filesReportByteLimit = 32 * 1024;
const specName = 'file-image-viewport.spec.ts';
const suiteName = 'Authorized Files image viewport';
const projects = new Set(['chromium-desktop', 'chromium-mobile', 'webkit-desktop', 'webkit-mobile', 'narrow-320']);
const statuses = new Set(['passed', 'failed', 'timedOut', 'skipped', 'interrupted']);
const casesByTitle = new Map([
  ['downloads from the expanded surface and clears protected modal state on later session expiry', 'download-session-expiry'],
  ['discards dimensions, transforms and protected URLs on replacement, workspace, denial and session expiry', 'protected-state-reset'],
]);
for (const width of [320, 390, 768, 1366, 1536]) {
  casesByTitle.set(`expands one authorized image renderer and returns keyboard focus at ${width}px`, `expanded-focus-${width}`);
  casesByTitle.set(`fits, rotates and scrolls real wide and portrait PNGs at ${width}px`, `viewport-scroll-${width}`);
}
const caseIds = new Set(casesByTitle.values());

function invalid() {
  throw new Error('Files attempt report did not match the approved evidence schema.');
}

export function validateFilesAttempts(input) {
  if (!Array.isArray(input) || !input.length || input.length > projects.size * caseIds.size * 3) {
    invalid();
  }

  const lastAttemptByCase = new Map();
  return input.map((record) => {
    if (!record || Object.keys(record).length !== 5) {
      invalid();
    }

    const { project, caseId, attempt, status, durationMs } = record;
    if (!projects.has(project) || !caseIds.has(caseId) || !statuses.has(status) ||
        !Number.isInteger(attempt) || attempt < 0 || attempt > 2 ||
        !Number.isInteger(durationMs) || durationMs < 0 || durationMs > 600_000) {
      invalid();
    }

    const identity = `${project}/${caseId}`;
    const previous = lastAttemptByCase.get(identity);
    if (attempt !== (previous ? previous.attempt + 1 : 0) ||
        (previous && !['failed', 'timedOut'].includes(previous.status))) {
      invalid();
    }
    lastAttemptByCase.set(identity, { attempt, status });
    return { project, caseId, attempt, status, durationMs };
  });
}

export default class FilesAttemptReporter {
  attempts = [];
  unidentifiedCase = false;

  onTestEnd(test, result) {
    if (test.location.file.replaceAll('\\', '/').split('/').slice(-3).join('/') !== `tests/ui/${specName}`) {
      return;
    }

    const [, project, file, suite, title, ...rest] = test.titlePath();
    const caseId = casesByTitle.get(title);
    if (basename(file) !== specName || suite !== suiteName || rest.length || !caseId) {
      this.unidentifiedCase = true;
      return;
    }
    this.attempts.push({ project, caseId, attempt: result.retry, status: result.status, durationMs: result.duration });
  }

  onEnd() {
    if (this.unidentifiedCase) {
      invalid();
    }
    // The Angular runner also supports focused invocations with no Files cases.
    if (this.attempts.length === 0) {
      return;
    }

    const attempts = validateFilesAttempts(this.attempts);
    mkdirSync('artifacts', { recursive: true });
    mkdirSync('artifacts/files-attempt-report');
    writeFileSync(filesReportInput, JSON.stringify(attempts) + '\n', { flag: 'wx' });
  }
}
