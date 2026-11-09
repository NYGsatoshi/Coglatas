import { accessSync, constants, lstatSync, mkdirSync } from 'node:fs';

// This host runner starts Linux Docker containers which bind-mount the checkout.
// Create the fixed output parent before a container can create it under its UID.
export function assertHostArtifactRoot(stat, runnerUid) {
  if (!Number.isSafeInteger(runnerUid) || runnerUid < 0 ||
      stat.isSymbolicLink() || !stat.isDirectory() || stat.uid !== runnerUid ||
      (stat.mode & 0o300) !== 0o300) {
    throw new Error('Host artifacts directory must be writable and owned by this runner.');
  }
}

export function prepareHostArtifactRoot() {
  const runnerUid = typeof process.getuid === 'function' ? process.getuid() : null;
  if (runnerUid === null) {
    throw new Error('Host artifacts preparation requires a POSIX runner identity.');
  }
  try {
    try { mkdirSync('artifacts', { mode: 0o700 }); }
    catch (error) { if (error?.code !== 'EEXIST') { throw error; } }
    assertHostArtifactRoot(lstatSync('artifacts'), runnerUid);
    accessSync('artifacts', constants.W_OK | constants.X_OK);
  } catch {
    throw new Error('Host artifacts directory must be writable and owned by this runner.');
  }
}
