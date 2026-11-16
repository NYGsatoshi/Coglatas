import { execFileSync } from 'node:child_process';
import { closeSync, constants, existsSync, fstatSync, lstatSync, mkdirSync, openSync, readSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { imageKeyboardByteLimit, imageKeyboardFilename, imageKeyboardInputDirectory,
  imageKeyboardOutputDirectory, validateImageKeyboardObservation } from '../../tests/ui/helpers/image-keyboard-observation.mjs';

/** Publish a newly validated projection, never copy raw input or general browser artifacts. */
export function collectImageKeyboardObservations(root, expectedSha, run) {
  const invalid = () => { throw new Error('Image keyboard observation collection failed.'); };
  if (!/^[0-9a-f]{40}$/.test(expectedSha ?? '') || !run ||
      typeof run.id !== 'string' || typeof run.attempt !== 'string' ||
      run.id !== run.id.trim() || run.attempt !== run.attempt.trim() ||
      !/^[1-9][0-9]{0,19}$/.test(run.id) || !/^[1-9][0-9]{0,5}$/.test(run.attempt) ||
      run.job !== 'frontend-test' || Object.keys(run).sort().join() !== 'attempt,id,job') { invalid(); }
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  const directory = (path) => {
    const value = lstatSync(path);
    if (!value.isDirectory() || value.isSymbolicLink()) { invalid(); }
  };
  const boundedRead = (path, limit) => {
    const before = lstatSync(path);
    if (!before.isFile() || before.isSymbolicLink() || before.size > limit) { invalid(); }
    // A FIFO swapped in after lstat must not block descriptor acquisition.
    const descriptor = openSync(path, constants.O_RDONLY | constants.O_NONBLOCK | (constants.O_NOFOLLOW ?? 0));
    try {
      const current = fstatSync(descriptor);
      if (!current.isFile() || current.ino !== before.ino || current.dev !== before.dev || current.size > limit) { invalid(); }
      const bytes = Buffer.alloc(limit + 1);
      let length = 0, count;
      while (length < bytes.length && (count = readSync(descriptor, bytes, length, bytes.length - length, null)) !== 0) {
        length += count;
      }
      if (length > limit || fstatSync(descriptor).size > limit) { invalid(); }
      return bytes.subarray(0, length);
    } finally { closeSync(descriptor); }
  };
  const sourceSha = git('rev-parse', 'HEAD');
  if (sourceSha !== expectedSha || git('status', '--porcelain', '--untracked-files=no')) { invalid(); }
  const input = join(root, imageKeyboardInputDirectory), output = join(root, imageKeyboardOutputDirectory);
  if (existsSync(output)) { invalid(); }
  if (!existsSync(input)) { return 0; }
  directory(join(root, 'artifacts')); directory(input);
  const names = readdirSync(input).sort(compareCodeUnits);
  if (names.length > 180) { invalid(); }
  directory(join(root, 'artifacts/ci'));
  const producerSha = boundedRead(join(root, 'artifacts/ci/frontend-build-sha'), 41).toString('utf8').trim();
  if (producerSha !== sourceSha) { invalid(); }
  const records = names.map((name) => {
    const bytes = boundedRead(join(input, name), imageKeyboardByteLimit);
    const observation = validateImageKeyboardObservation(JSON.parse(bytes.toString('utf8')));
    if (name !== imageKeyboardFilename(observation.profile, observation.width, observation.image, observation.attempt)) { invalid(); }
    return { name, value: { schemaVersion: 1, sourceSha, producerSha, run: { ...run }, observation } };
  });
  if (git('rev-parse', 'HEAD') !== sourceSha || git('status', '--porcelain', '--untracked-files=no')) { invalid(); }
  if (!records.length) { return 0; }
  mkdirSync(output);
  for (const { name, value } of records) { writeFileSync(join(output, name), JSON.stringify(value) + '\n', { flag: 'wx' }); }
  return records.length;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const count = collectImageKeyboardObservations(process.cwd(), process.env.GITHUB_SHA, {
      id: process.env.GITHUB_RUN_ID, attempt: process.env.GITHUB_RUN_ATTEMPT, job: process.env.GITHUB_JOB
    });
    console.log(`Image keyboard observations collected: ${count}; causal qualification UNKNOWN/INCOMPLETE only.`);
  } catch {
    console.error('Image keyboard observation collection failed; no unvalidated browser data may be published.');
    process.exitCode = 1;
  }
}

// Match default UTF-16 code-unit ordering, including native string coercion.
function compareCodeUnits(left, right) {
  const leftString = `${left}`;
  const rightString = `${right}`;
  if (leftString === rightString) return 0;
  return leftString < rightString ? -1 : 1;
}
