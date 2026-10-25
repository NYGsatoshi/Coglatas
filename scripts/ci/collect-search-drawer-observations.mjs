import { execFileSync } from 'node:child_process';
import { closeSync, constants, existsSync, fstatSync, lstatSync, mkdirSync, openSync, readSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { searchDrawerByteLimit, searchDrawerFilename, searchDrawerInputDirectory,
  searchDrawerOutputDirectory, validateSearchDrawerObservation } from '../../tests/ui/helpers/search-drawer-observation.mjs';

/** Publish a newly validated projection, never copy raw input or general browser artifacts. */
export function collectSearchDrawerObservations(root, expectedSha) {
  const invalid = () => { throw new Error('Search drawer observation collection failed.'); };
  if (!/^[0-9a-f]{40}$/.test(expectedSha ?? '')) { invalid(); }
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
  const input = join(root, searchDrawerInputDirectory), output = join(root, searchDrawerOutputDirectory);
  if (existsSync(output)) { invalid(); }
  if (!existsSync(input)) { return 0; }
  directory(join(root, 'artifacts')); directory(input);
  const names = readdirSync(input).sort();
  if (names.length > 3) { invalid(); }
  directory(join(root, 'artifacts/ci'));
  const producerSha = boundedRead(join(root, 'artifacts/ci/frontend-build-sha'), 41).toString('utf8').trim();
  if (producerSha !== sourceSha) { invalid(); }
  const records = names.map((name) => {
    const bytes = boundedRead(join(input, name), searchDrawerByteLimit);
    const observation = validateSearchDrawerObservation(JSON.parse(bytes.toString('utf8')));
    if (name !== searchDrawerFilename(observation.profile, observation.attempt)) { invalid(); }
    return { name, value: { schemaVersion: 1, sourceSha, producerSha, observation } };
  });
  if (git('rev-parse', 'HEAD') !== sourceSha || git('status', '--porcelain', '--untracked-files=no')) { invalid(); }
  if (!records.length) { return 0; }
  mkdirSync(output);
  for (const { name, value } of records) { writeFileSync(join(output, name), JSON.stringify(value) + '\n', { flag: 'wx' }); }
  return records.length;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const count = collectSearchDrawerObservations(process.cwd(), process.env.GITHUB_SHA);
    console.log(`Search drawer observations collected: ${count}; causal qualification UNKNOWN/INCOMPLETE only.`);
  } catch {
    console.error('Search drawer observation collection failed; no unvalidated browser data may be published.');
    process.exitCode = 1;
  }
}
