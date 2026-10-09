import { isAbsolute, relative, resolve, dirname, basename } from 'node:path';
import { mkdir, realpath } from 'node:fs/promises';
import { fixtureArguments } from './sec-arch-kafka-acls.mjs';

export function runtimeArguments(args) {
  const common = [];
  let privateDirectory;
  for (let index = 0; index < args.length; index++) {
    if (args[index] !== '--private-inventory-directory') common.push(args[index]);
    else {
      const value = args[++index];
      if (privateDirectory !== undefined || typeof value !== 'string' || !isAbsolute(value) || value.includes('\0'))
        throw new Error('Private inventory requires one absolute directory');
      privateDirectory = value;
    }
  }
  return { ...fixtureArguments(common), privateDirectory };
}

export function insideDirectory(root, candidate) {
  const value = relative(root, candidate);
  return value === '' || !(value === '..' || value.startsWith('../') || value.startsWith('..\\') || isAbsolute(value));
}

export async function preparePrivateDirectory(root, suppliedDirectory, inspectParent) {
  const parent = await realpath(dirname(resolve(suppliedDirectory)));
  const publicRoot = await realpath(root);
  if (insideDirectory(publicRoot, parent)) throw new Error('Private output cannot reside in the public checkout');
  const repository = await inspectParent(parent);
  if (repository.code !== 128 || repository.timedOut) throw new Error('Private output parent must be outside Git checkouts');
  const privateDirectory = resolve(parent, basename(resolve(suppliedDirectory)));
  await mkdir(privateDirectory);
  if (await realpath(privateDirectory) !== privateDirectory) throw new Error('Private output path changed');
  return privateDirectory;
}
