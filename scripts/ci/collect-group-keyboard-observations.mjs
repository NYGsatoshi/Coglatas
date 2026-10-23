import { execFileSync } from 'node:child_process';
import { closeSync, constants, existsSync, fstatSync, lstatSync, mkdirSync, openSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { groupKeyboardByteLimit, groupKeyboardFilename, groupKeyboardInputDirectory,
  groupKeyboardOutputDirectory, validateGroupKeyboardObservation } from '../../tests/ui/helpers/group-keyboard-observation.mjs';

/** Publish a newly validated projection, never copy raw input or general browser artifacts. */
export function collectGroupKeyboardObservations(root, expectedSha) {
  const invalid = () => { throw new Error('Group keyboard observation collection failed.'); };
  if (!/^[0-9a-f]{40}$/.test(expectedSha ?? '')) { invalid(); }
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  const sourceSha = git('rev-parse', 'HEAD');
  if (sourceSha !== expectedSha || git('status', '--porcelain', '--untracked-files=no')) { invalid(); }
  const input = join(root, groupKeyboardInputDirectory), output = join(root, groupKeyboardOutputDirectory);
  if (existsSync(output)) { invalid(); }
  if (!existsSync(input)) { return 0; }
  if (!lstatSync(input).isDirectory() || lstatSync(input).isSymbolicLink()) { invalid(); }
  const names = readdirSync(input).sort();
  if (names.length > 18) { invalid(); }
  const producerSha = readFileSync(join(root, 'artifacts/ci/frontend-build-sha'), 'utf8').trim();
  if (producerSha !== sourceSha) { invalid(); }
  const records = names.map((name) => {
    const path = join(input, name), before = lstatSync(path);
    if (!before.isFile() || before.isSymbolicLink() || before.size > groupKeyboardByteLimit) { invalid(); }
    const descriptor = openSync(path, constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0));
    try {
      const current = fstatSync(descriptor);
      if (!current.isFile() || current.ino !== before.ino || current.dev !== before.dev) { invalid(); }
      const bytes = readFileSync(descriptor);
      if (bytes.length > groupKeyboardByteLimit) { invalid(); }
      const observation = validateGroupKeyboardObservation(JSON.parse(bytes.toString('utf8')));
      if (name !== groupKeyboardFilename(observation.profile, observation.attempt)) { invalid(); }
      return { name, value: { schemaVersion: 1, sourceSha, producerSha, observation } };
    } finally { closeSync(descriptor); }
  });
  if (git('rev-parse', 'HEAD') !== sourceSha || git('status', '--porcelain', '--untracked-files=no')) { invalid(); }
  if (!records.length) { return 0; }
  mkdirSync(output);
  for (const { name, value } of records) { writeFileSync(join(output, name), JSON.stringify(value) + '\n', { flag: 'wx' }); }
  return records.length;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const count = collectGroupKeyboardObservations(process.cwd(), process.env.GITHUB_SHA);
    console.log(`Group keyboard observations collected: ${count}; causal qualification UNKNOWN/INCOMPLETE only.`);
  } catch {
    console.error('Group keyboard observation collection failed; no unvalidated browser data may be published.');
    process.exitCode = 1;
  }
}
