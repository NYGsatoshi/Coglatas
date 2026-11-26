import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createReadStream, constants, lstatSync, mkdirSync, openSync, fstatSync, readSync, closeSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { filesReportByteLimit, filesReportInput, validateFilesAttempts, summarizeHostedFilesAttempts } from '../../tests/ui/files-attempt-reporter.mjs';

const shaPattern = /^[a-f0-9]{40}$/;
const runNumberPattern = /^[1-9][0-9]{0,19}$/;
const outputDirectory = 'artifacts/files-attempt-validated';
const outputName = 'files-attempts.json';

function invalid() {
  throw new Error('Files attempt report source, producer, run or input is invalid.');
}

function checkDirectory(root, path) {
  if (!lstatSync(join(root, path)).isDirectory()) {
    invalid();
  }
}

function readBounded(file, limit) {
  const before = lstatSync(file);
  if (!before.isFile() || before.size > limit) {
    invalid();
  }

  const descriptor = openSync(file, constants.O_RDONLY | constants.O_NONBLOCK | (constants.O_NOFOLLOW ?? 0));
  try {
    const current = fstatSync(descriptor);
    if (!current.isFile() || current.ino !== before.ino || current.dev !== before.dev || current.size > limit) {
      invalid();
    }
    const bytes = Buffer.alloc(limit + 1);
    let length = 0;
    while (length < bytes.length) {
      const count = readSync(descriptor, bytes, length, bytes.length - length, null);
      if (count === 0) {
        break;
      }
      length += count;
    }
    if (length > limit || fstatSync(descriptor).size > limit) {
      invalid();
    }
    return bytes.subarray(0, length).toString('utf8');
  } finally {
    closeSync(descriptor);
  }
}

export async function collectFilesAttempts(root, { sourceSha, runId, runAttempt, job, frontendChecksOutcome, archive }) {
  if (!shaPattern.test(sourceSha ?? '') || !runNumberPattern.test(runId ?? '') ||
      !runNumberPattern.test(runAttempt ?? '') || job !== 'frontend-test') {
    invalid();
  }

  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  const checkSource = () => {
    if (git('rev-parse', 'HEAD') !== sourceSha || git('status', '--porcelain', '--untracked-files=no')) {
      invalid();
    }
  };
  checkSource();
  checkDirectory(root, 'artifacts');
  checkDirectory(root, 'artifacts/ci');
  checkDirectory(root, 'artifacts/files-attempt-report');

  const producerSha = readBounded(join(root, 'artifacts/ci/frontend-build-sha'), 41).trim();
  if (producerSha !== sourceSha || !archive || !lstatSync(archive).isFile()) {
    invalid();
  }
  const producerHash = createHash('sha256');
  for await (const chunk of createReadStream(archive)) {
    producerHash.update(chunk);
  }

  const attempts = validateFilesAttempts(JSON.parse(readBounded(join(root, filesReportInput), filesReportByteLimit)));
  const receipt = {
    schemaVersion: 1,
    sourceSha,
    producerSha,
    producerArchiveSha256: producerHash.digest('hex'),
    runId,
    runAttempt,
    job,
    frontendChecksOutcome,
    coverage: summarizeHostedFilesAttempts(attempts, frontendChecksOutcome),
    suite: 'tests/ui/file-image-viewport.spec.ts',
    attempts,
  };

  checkSource();
  const output = join(root, outputDirectory);
  const bytes = JSON.stringify(receipt) + '\n';
  const digest = createHash('sha256').update(bytes).digest('hex');
  mkdirSync(output);
  writeFileSync(join(output, outputName), bytes, { flag: 'wx' });
  writeFileSync(join(output, `${outputName}.sha256`), `${digest}  ${outputName}\n`, { flag: 'wx' });
  return attempts.length;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const count = await collectFilesAttempts(process.cwd(), {
      sourceSha: process.env.GITHUB_SHA,
      runId: process.env.GITHUB_RUN_ID,
      runAttempt: process.env.GITHUB_RUN_ATTEMPT,
      job: process.env.GITHUB_JOB,
      frontendChecksOutcome: process.env.COGLATAS_FRONTEND_CHECKS_OUTCOME,
      archive: join(process.env.RUNNER_TEMP, 'frontend-build-artifact/frontend-build-artifacts.tar'),
    });
    console.log(`Retained ${count} sanitized Files browser attempts.`);
  } catch {
    console.error('Files browser attempt evidence could not be validated.');
    process.exitCode = 1;
  }
}
