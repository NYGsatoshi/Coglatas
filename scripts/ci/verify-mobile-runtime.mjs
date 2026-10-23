import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { mobileRuntimeIdentity } from './mobile-runtime-contract.mjs';

try {
  const phase = process.argv[2], project = process.env.COMPOSE_PROJECT_NAME, sha = process.env.TARGET_SHA;
  if (!['before', 'after'].includes(phase) || !/^[a-z0-9][a-z0-9_-]{0,62}$/.test(project ?? '') ||
      !/^[0-9a-f]{40}$/.test(sha ?? '') || readFileSync('artifacts/mobile/source-sha', 'utf8').trim() !== sha ||
      execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim() !== sha ||
      execFileSync('git', ['status', '--porcelain', '--untracked-files=no'], { encoding: 'utf8' }).trim()) {
    throw new Error('Invalid mobile execution binding.');
  }
  const docker = (args) => execFileSync('docker', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  const container = docker(['compose', '-p', project, '-f', 'infra/compose/test/real-backend-smoke.yml',
    '-f', 'infra/compose/test/mobile-acceptance.yml', 'ps', '-q', 'app']);
  const running = JSON.parse(docker(['inspect', container]))[0];
  const image = JSON.parse(docker(['image', 'inspect', process.env.COGLATAS_APP_IMAGE]))[0];
  const reused = process.env.COGLATAS_REUSE_PREBUILT_APP_IMAGE === '1';
  const identity = mobileRuntimeIdentity({ sha, stamp: readFileSync('artifacts/mobile/source-sha', 'utf8').trim(),
    checkout: execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(),
    dirty: execFileSync('git', ['status', '--porcelain', '--untracked-files=no'], { encoding: 'utf8' }).trim(),
    image, running, reused, producerSource: process.env.COGLATAS_REUSE_PREBUILT_APP_SOURCE_SHA,
    producerId: process.env.COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID ?? process.env.COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID });
  if (phase === 'before') { writeFileSync('artifacts/mobile/image-before.json', JSON.stringify(identity) + '\n'); }
  else {
    const before = JSON.parse(readFileSync('artifacts/mobile/image-before.json', 'utf8'));
    if (JSON.stringify(before) !== JSON.stringify(identity)) { throw new Error('Mobile runtime changed during execution.'); }
    writeFileSync('artifacts/mobile/image-continuity.json', JSON.stringify({ ...identity,
      beforeProbeVerified: true, afterProbeVerified: true }) + '\n');
  }
  console.log(`Mobile runtime ${phase} identity verified.`);
} catch {
  console.error('Mobile runtime identity verification failed.');
  process.exitCode = 1;
}
