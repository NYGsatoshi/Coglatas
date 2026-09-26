import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

import {
  buildCompatCriticalGrep,
  loadCompatCriticalContract,
  selectedCompatCriticalFiles,
  verifyCompatCriticalSources
} from './compat-critical-contract.mjs';

const DEFAULT_CONTRACT = 'scripts/ci/compat-critical.contract.json';
const playwrightCli = fileURLToPath(new URL('../../node_modules/@playwright/test/cli.js', import.meta.url));
const { contractPath, profile: requestedProfile, passthrough } = parseArguments(process.argv.slice(2));

try {
  const contract = await loadCompatCriticalContract(contractPath);
  const profile = requestedProfile ?? contract.defaultProfile;
  await verifyCompatCriticalSources(contract);
  const grep = buildCompatCriticalGrep(contract, profile);
  const files = selectedCompatCriticalFiles(contract, profile);

  const exitCode = await runPlaywright(files, grep, passthrough);
  process.exitCode = exitCode;
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

function runPlaywright(files, grep, passthrough) {
  return new Promise((resolve, reject) => {
    const child = spawn(
      process.execPath,
      [
        playwrightCli,
        'test',
        ...files,
        '--config=playwright.config.ts',
        '--grep',
        grep,
        ...passthrough,
        '--retries=0'
      ],
      {
        cwd: process.cwd(),
        env: { ...process.env, TZ: 'UTC', COGLATAS_COMPAT_CRITICAL: '1' },
        stdio: 'inherit'
      }
    );
    child.on('error', reject);
    child.on('exit', (code) => resolve(code ?? 1));
  });
}

function parseArguments(args) {
  const separatorIndex = args.indexOf('--');
  const ownArgs = separatorIndex >= 0 ? args.slice(0, separatorIndex) : args;
  const passthrough = separatorIndex >= 0 ? args.slice(separatorIndex + 1) : [];
  let contractPath = DEFAULT_CONTRACT;
  let profile;

  for (let index = 0; index < ownArgs.length; index += 1) {
    const argument = ownArgs[index];
    if (argument === '--contract') {
      contractPath = requireValue(ownArgs, ++index, '--contract');
    } else if (argument === '--profile') {
      profile = requireValue(ownArgs, ++index, '--profile');
    } else {
      throw new Error(`Unknown argument: ${argument}. Pass Playwright arguments after --.`);
    }
  }

  return { contractPath, profile, passthrough };
}

function requireValue(args, index, option) {
  const value = args[index];
  if (!value || value.startsWith('--')) {
    throw new Error(`${option} requires a value.`);
  }
  return value;
}
