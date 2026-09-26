import { spawn } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

import {
  buildCompatCriticalGrep,
  loadCompatCriticalContract,
  selectedCompatCriticalFiles,
  verifyCompatCriticalDiscovery,
  verifyCompatCriticalSources
} from './compat-critical-contract.mjs';

const DEFAULT_CONTRACT = 'scripts/ci/compat-critical.contract.json';
const playwrightCli = fileURLToPath(new URL('../../node_modules/@playwright/test/cli.js', import.meta.url));

export async function verifyCompatCritical(options = {}) {
  const contractPath = options.contractPath ?? DEFAULT_CONTRACT;
  const contract = await loadCompatCriticalContract(contractPath);
  const profile = options.profile ?? contract.defaultProfile;
  const project = options.project ?? 'chromium-desktop';

  await verifyCompatCriticalSources(contract, options.repositoryRoot ?? process.cwd());
  const grep = buildCompatCriticalGrep(contract, profile);
  const files = selectedCompatCriticalFiles(contract, profile);
  const discovery = await runPlaywrightList({ files, grep, project, cwd: options.repositoryRoot ?? process.cwd() });
  const result = verifyCompatCriticalDiscovery(contract, profile, discovery.stdout);

  return { ...result, profile, project, files, stdout: discovery.stdout };
}

if (isMainModule()) {
  try {
    const options = parseArguments(process.argv.slice(2));
    const result = await verifyCompatCritical(options);
    process.stdout.write(result.stdout);
    if (!result.stdout.endsWith('\n')) {
      process.stdout.write('\n');
    }
    console.log(
      `compat-critical verification passed: profile=${result.profile}; project=${result.project}; tests=${result.selectedCount}; files=${result.files.length}.`
    );
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  }
}

function runPlaywrightList({ files, grep, project, cwd }) {
  return new Promise((resolve, reject) => {
    const child = spawn(
      process.execPath,
      [
        playwrightCli,
        'test',
        ...files,
        '--config=playwright.config.ts',
        `--project=${project}`,
        '--grep',
        grep,
        '--retries=0',
        '--list'
      ],
      {
        cwd,
        env: { ...process.env, TZ: 'UTC', COGLATAS_COMPAT_CRITICAL: '1' },
        stdio: ['ignore', 'pipe', 'pipe']
      }
    );

    let stdout = '';
    let stderr = '';
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', (chunk) => { stdout += chunk; });
    child.stderr.on('data', (chunk) => { stderr += chunk; });
    child.on('error', reject);
    child.on('exit', (code) => {
      if (code === 0) {
        resolve({ stdout, stderr });
        return;
      }
      reject(new Error(`Playwright compat-critical discovery failed with exit code ${code ?? 1}.\n${stderr || stdout}`));
    });
  });
}

function parseArguments(args) {
  const options = { contractPath: DEFAULT_CONTRACT, profile: undefined, project: 'chromium-desktop' };
  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    switch (argument) {
      case '--contract':
        options.contractPath = requireValue(args, ++index, '--contract');
        break;
      case '--profile':
        options.profile = requireValue(args, ++index, '--profile');
        break;
      case '--project':
        options.project = requireValue(args, ++index, '--project');
        break;
      default:
        throw new Error(`Unknown argument: ${argument}`);
    }
  }
  return options;
}

function requireValue(args, index, option) {
  const value = args[index];
  if (!value || value.startsWith('--')) {
    throw new Error(`${option} requires a value.`);
  }
  return value;
}

function isMainModule() {
  const entryPoint = process.argv[1];
  return Boolean(entryPoint) && import.meta.url === pathToFileURL(entryPoint).href;
}
