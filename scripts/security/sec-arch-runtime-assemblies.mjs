import { createHash } from 'node:crypto';
import { open } from 'node:fs/promises';
import { resolve } from 'node:path';

// The explicit six-assembly scope matches the versioned Python producer binding.
export const runtimeAssemblyNames = Object.freeze(['Coglatas.Tests', 'Coglatas.Web', 'Coglatas.Application',
  'Coglatas.Infrastructure', 'Coglatas.Domain', 'Coglatas.SecurityArchitecture']);
export const runtimeAssemblyScope = 'SIX_ASSEMBLIES_WITH_LOADED_COPIES';
const maximumAssemblyBytes = 32 * 1024 * 1024;

export function unverifiedRuntimeAssemblyBinding() {
  return { schemaVersion: 2, verifierVersion: '2', assemblyBindingScope: runtimeAssemblyScope,
    assemblyDigests: {}, fullDependencyQualification: 'UNVERIFIED' };
}

async function assemblyDigest(path) {
  const source = await open(path, 'r');
  const hash = createHash('sha256');
  const buffer = Buffer.alloc(64 * 1024);
  let total = 0;
  try {
    for (;;) {
      // Read at most maximum+1, including a growing/replaced input after opening.
      const { bytesRead } = await source.read(buffer, 0, Math.min(buffer.length, maximumAssemblyBytes - total + 1), null);
      if (bytesRead === 0) break;
      total += bytesRead;
      if (total > maximumAssemblyBytes) throw new Error('Compiled assembly exceeds the bounded input size');
      hash.update(buffer.subarray(0, bytesRead));
    }
    if (total === 0) throw new Error('Empty compiled assembly identity');
    return hash.digest('hex');
  } finally {
    await source.close();
  }
}

export async function captureRuntimeAssemblyBinding(root) {
  const assemblyDigests = {};
  for (const name of runtimeAssemblyNames) {
    const directory = name === 'Coglatas.Tests' ? 'tests/Coglatas.Tests' :
      name === 'Coglatas.SecurityArchitecture' ? 'tools/Coglatas.SecurityArchitecture' : `src/${name}`;
    const canonical = await assemblyDigest(resolve(root, directory, `bin/Release/net10.0/${name}.dll`));
    const copied = await assemblyDigest(resolve(root, 'tests/Coglatas.Tests', `bin/Release/net10.0/${name}.dll`));
    if (canonical !== copied) throw new Error('Loaded dependency copy differs from the producer build');
    assemblyDigests[name] = canonical;
  }
  return { schemaVersion: 2, verifierVersion: '2', assemblyBindingScope: runtimeAssemblyScope,
    assemblyDigests, fullDependencyQualification: 'SIX_ASSEMBLY_LOCAL_BYTES_RECONCILED' };
}
