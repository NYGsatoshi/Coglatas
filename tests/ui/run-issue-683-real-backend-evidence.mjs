/* eslint-disable func-style, no-await-in-loop, no-console, no-magic-numbers, no-shadow, no-ternary, no-use-before-define, one-var, sort-imports, sort-keys -- Issue #683 evidence is intentionally sequential and imperative so every fresh Compose run is fully captured before the next run starts. */
import { spawnSync } from 'node:child_process';
import { copyFile, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { join, relative } from 'node:path';
import {
  ISSUE_683_ITERATION_COUNT,
  ISSUE_683_MIN_RACE_OBSERVATION_ITERATIONS,
  ISSUE_683_REQUIRED_EXIT_CODE,
  ISSUE_683_REQUIRED_PLAYWRIGHT_RETRY_COUNT,
  isPassingIssue683Iteration,
  summarizeIssue683Iterations
} from './issue-683-race-evidence.mjs';
import { validateDetachedFixedSha } from './fixed-sha-evidence.mjs';

const ISSUE_ID = '#683';
const EVIDENCE_ROOT = 'issue-683-evidence';
const JUNIT_SOURCE = join('test-results', 'playwright-results.xml');
const ITERATION_PAD_WIDTH = 2;
const ITERATION_PAD_CHARACTER = '0';
const FALLBACK_FAILURE_EXIT_CODE = 1;
const FIRST_ITERATION = 1;
const ITERATION_INCREMENT = 1;
const ZERO_COUNT = 0;
const NO_ITERATION_RETRIES = 0;
const ENABLED_ENVIRONMENT_VALUE = '1';

const expectedSha = process.env.GITHUB_SHA?.trim() ?? '';
const candidateSha = validateDetachedFixedSha(
  await readFile('.git/HEAD', 'utf8'),
  expectedSha,
  `Issue ${ISSUE_ID} evidence`
);

await rm(EVIDENCE_ROOT, { recursive: true, force: true });
await mkdir(EVIDENCE_ROOT, { recursive: true });

const orderingContract = spawnSync(
  process.execPath,
  [
    '--test',
    'tests/ui/real-backend-pr03c-failure-correlation.node-test.mjs',
    'tests/ui/issue-683-race-evidence.node-test.mjs'
  ],
  { cwd: process.cwd(), stdio: 'inherit' }
);
const orderingContractExitCode = Number.isInteger(orderingContract.status)
  ? orderingContract.status
  : FALLBACK_FAILURE_EXIT_CODE;
await writeJsonFile(join(EVIDENCE_ROOT, 'ordering-contract.json'), {
  candidateSha,
  exitCode: orderingContractExitCode,
  verifies: [
    'boundary-before-revocation',
    'revocation-success-before-failure-selection',
    'pre-revocation-execution-scope-404-remains-unexpected',
    'successful-revocation-required-by-issue-683-observation-policy'
  ]
});
if (orderingContractExitCode !== ISSUE_683_REQUIRED_EXIT_CODE) {
  throw new Error(`Issue ${ISSUE_ID} post-revocation ordering contract failed on fixed SHA ${candidateSha}.`);
}

const iterations = [];
let terminalError = null;

for (
  let iteration = FIRST_ITERATION;
  iteration <= ISSUE_683_ITERATION_COUNT;
  iteration += ITERATION_INCREMENT
) {
  const iterationName = `iteration-${String(iteration).padStart(ITERATION_PAD_WIDTH, ITERATION_PAD_CHARACTER)}`;
  const iterationDirectory = join(EVIDENCE_ROOT, iterationName);
  const raceEvidencePath = join(iterationDirectory, 'race-evidence.json');
  await mkdir(iterationDirectory, { recursive: true });

  console.log(
    `Issue ${ISSUE_ID} evidence ${iterationName}/${ISSUE_683_ITERATION_COUNT}: fixed SHA ${candidateSha}, Playwright retries=${ISSUE_683_REQUIRED_PLAYWRIGHT_RETRY_COUNT}.`
  );

  const result = spawnSync(process.execPath, ['tests/ui/run-real-backend-p0.mjs'], {
    cwd: process.cwd(),
    env: {
      ...process.env,
      COGLATAS_ISSUE_683_EVIDENCE: ENABLED_ENVIRONMENT_VALUE,
      COGLATAS_ISSUE_683_EVIDENCE_FILE: relative(process.cwd(), raceEvidencePath)
    },
    stdio: 'inherit'
  });
  const exitCode = Number.isInteger(result.status) ? result.status : FALLBACK_FAILURE_EXIT_CODE;

  if (exitCode === ISSUE_683_REQUIRED_EXIT_CODE && await fileExists(JUNIT_SOURCE)) {
    await copyFile(JUNIT_SOURCE, join(iterationDirectory, 'playwright-results.xml'));
  }

  const reporterOutput = await readJsonFile(raceEvidencePath);
  const records = Array.isArray(reporterOutput?.records) ? reporterOutput.records : [];
  const playwrightRetries = Array.isArray(reporterOutput?.retries) ? reporterOutput.retries : [];
  const parseErrors = records
    .map((record) => record?.parseError)
    .filter((error) => typeof error === 'string' && error.length > ZERO_COUNT);
  if (!reporterOutput) {
    parseErrors.push(`Issue ${ISSUE_ID} race evidence reporter output is missing or unreadable.`);
  } else if (!Array.isArray(reporterOutput.retries)) {
    parseErrors.push(`Issue ${ISSUE_ID} reporter did not record Playwright retry values.`);
  }

  const raceObservationCount = records.reduce(
    (count, record) => count + (Array.isArray(record?.raceObservations) ? record.raceObservations.length : ZERO_COUNT),
    ZERO_COUNT
  );
  const iterationSummary = {
    iteration,
    exitCode,
    playwrightRetries,
    pr03cResultCount: records.length,
    pr03cRetries: records.map((record) => record?.retry),
    pr03cStatuses: records.map((record) => record?.status),
    parseErrors,
    raceObservationCount
  };
  iterations.push(iterationSummary);
  await writeJsonFile(join(iterationDirectory, 'summary.json'), iterationSummary);

  if (!isPassingIssue683Iteration(iterationSummary)) {
    terminalError = new Error(
      `Issue ${ISSUE_ID} evidence failed in ${iterationName}; no iteration retry is permitted. See ${iterationDirectory}/summary.json.`
    );
    break;
  }
}

const summary = {
  issue: ISSUE_ID,
  candidateSha,
  fixedShaMatchesGithubSha: candidateSha === expectedSha,
  orderingContractExitCode,
  iterationPolicy: {
    planned: ISSUE_683_ITERATION_COUNT,
    minimumRaceObservationIterations: ISSUE_683_MIN_RACE_OBSERVATION_ITERATIONS,
    playwrightRetries: ISSUE_683_REQUIRED_PLAYWRIGHT_RETRY_COUNT,
    iterationRetries: NO_ITERATION_RETRIES
  },
  ...summarizeIssue683Iterations(iterations),
  iterations
};
await writeJsonFile(join(EVIDENCE_ROOT, 'summary.json'), summary);
await writeFile(join(EVIDENCE_ROOT, 'summary.md'), renderMarkdownSummary(summary), 'utf8');

if (terminalError) {
  throw terminalError;
}
if (!summary.accepted) {
  throw new Error(
    `Issue ${ISSUE_ID} evidence conditions were not met: completed=${summary.completedIterations}/${summary.plannedIterations}, race-observed=${summary.raceObservedIterations}/${summary.minimumRaceObservationIterations}. No retries were used.`
  );
}

console.log(
  `Issue ${ISSUE_ID} evidence accepted for ${candidateSha}: ${summary.completedIterations} clean iterations, retries=0, race observed in ${summary.raceObservedIterations} iteration(s).`
);

/**
 * Read and parse a JSON file, returning null if the file doesn't exist or parsing fails.
 * @param {string} path - Path to the JSON file
 * @returns {Promise<object|null>} Parsed JSON object or null on error
 */
async function readJsonFile(path) {
  try {
    return JSON.parse(await readFile(path, 'utf8'));
  } catch {
    return null;
  }
}

/**
 * Write a value to a file as formatted JSON with a trailing newline.
 * @param {string} path - Path where JSON file will be written
 * @param {any} value - Value to serialize as JSON
 * @returns {Promise<void>}
 */
async function writeJsonFile(path, value) {
  await writeFile(path, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
}

/**
 * Check if a file exists by attempting to read it.
 * @param {string} path - Path to the file
 * @returns {Promise<boolean>} True if file exists and is readable, false otherwise
 */
async function fileExists(path) {
  try {
    await readFile(path);
    return true;
  } catch {
    return false;
  }
}

/**
 * Render a markdown summary of Issue 683 evidence results.
 * @param {object} summary - The evidence summary object
 * @returns {string} Formatted markdown text
 */
function renderMarkdownSummary(summary) {
  const result = summary.accepted ? 'PASS' : 'FAIL';
  return [
    `# Issue ${ISSUE_ID} real-browser/backend evidence`,
    '',
    `- Candidate SHA: \`${summary.candidateSha}\``,
    `- Fixed SHA matches \`GITHUB_SHA\`: ${summary.fixedShaMatchesGithubSha}`,
    `- Post-revocation ordering contract exit code: ${summary.orderingContractExitCode}`,
    `- Planned clean iterations: ${summary.plannedIterations}`,
    `- Completed clean iterations: ${summary.completedIterations}`,
    `- Playwright retries: ${summary.requiredPlaywrightRetries}`,
    `- Iteration retries after failure: ${NO_ITERATION_RETRIES}`,
    `- Race observation condition: passing PR03C with successful Workspace revocation plus exact \`GET /api/tasks/{taskId}/execution-scope -> 404\` in at least ${summary.minimumRaceObservationIterations} iteration(s)`,
    `- Race-observed iterations: ${summary.raceObservedIterations}`,
    `- Result: **${result}**`,
    ''
  ].join('\n');
}
