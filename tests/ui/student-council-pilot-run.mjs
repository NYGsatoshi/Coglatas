import { spawn } from 'node:child_process';
import { readFile, mkdir, unlink, access } from 'node:fs/promises';
import { isAbsolute, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readPilotConfiguration, validateCheckpoint, validateRestartRecord, validateExerciseReceipt, validatePhaseReceipt } from './student-council-pilot-helpers.mjs';

class PilotReadError extends Error {}

process.exitCode = await main();

async function main() {
  try {
    const args = process.argv.slice(2);
    if (args.length > 1 || (args.length === 1 && args[0] !== '--validate-only')) throw new Error('Only --validate-only is accepted; test selection and artifact settings cannot be overridden.');
    const fixturePath = process.env.COGLATAS_STUDENT_PILOT_FIXTURE;
    if (!fixturePath || !isAbsolute(fixturePath)) throw new Error('Pilot FIXTURE must be an absolute path to a private JSON inventory.');
    const fixture = await readJson(fixturePath);
    const config = readPilotConfiguration(process.env, fixture);
    const evidence = process.env.COGLATAS_STUDENT_PILOT_EVIDENCE_DIR;
    if (!evidence || !isAbsolute(evidence)) throw new Error('Pilot EVIDENCE_DIR must be an absolute private output directory.');
    if (config.phase === 'verify-restart') {
      const checkpoint = validateCheckpoint(await readJson(join(evidence, 'checkpoint.json')), config);
      validateExerciseReceipt(await readJson(join(evidence, 'exercise-receipt.json')), checkpoint);
      validateRestartRecord(await readJson(join(evidence, 'restart.json')), checkpoint);
    }
    if (args[0] === '--validate-only') {
      console.log('Pilot configuration: VALID. No browser, deployment or release qualification performed. Public pilot: NO_GO.');
      return 0;
    }
    await mkdir(evidence, { recursive: true, mode: 0o700 });
    const receiptPath = join(evidence, `${config.phase}-receipt.json`);
    // Preserve previous outcomes; each exercise requires a fresh evidence directory.
    for (const path of [receiptPath, ...(config.phase === 'exercise' ? [join(evidence, 'checkpoint.json')] : [])]) {
      try { await access(path); throw new PilotReadError('Pilot output already exists; use a fresh private phase output.'); }
      catch (error) { if (error.code !== 'ENOENT') throw error; }
    }
    const env = { ...process.env };
    for (const key of ['DEBUG', 'PWDEBUG', 'NODE_OPTIONS', 'PLAYWRIGHT_JSON_OUTPUT_NAME', 'PLAYWRIGHT_JUNIT_OUTPUT_NAME']) delete env[key];
    const cli = fileURLToPath(new URL('../../node_modules/@playwright/test/cli.js', import.meta.url));
    const code = await new Promise((resolve) => {
      const child = spawn(process.execPath, [cli, 'test', '--config', fileURLToPath(new URL('./student-council-pilot.config.ts', import.meta.url)), '--workers=1', '--retries=0'], {
        cwd: fileURLToPath(new URL('../..', import.meta.url)), env,
        // The child reporter and exception paths must not expose private values.
        stdio: 'ignore'
      });
      child.once('error', () => resolve(1));
      child.once('close', (result) => resolve(result === 0 ? 0 : 1));
    });
    if (code !== 0) await unlink(receiptPath).catch(() => {});
    if (code === 0) {
      const receipt = await readJson(receiptPath);
      validatePhaseReceipt(receipt, config);
    }
    console.log(code === 0
      ? 'Pilot execution: PASS; qualification: PILOT_PARTIAL; public pilot: NO_GO. Read the private scoped receipt and missing matrix controls.'
      : 'Pilot execution: FAIL; qualification: PILOT_NO_GO. No pass receipt was issued for this run.');
    return code;
  } catch (error) {
    const message = error instanceof Error ? error.message : '';
    console.error(error instanceof PilotReadError || /^(?:COGLATAS_STUDENT_PILOT_|Pilot |PILOT_NO_GO:|Checkpoint |Restart |Only )/u.test(message)
      ? message : 'Pilot setup failed; check the private fixture/evidence files.');
    return 1;
  }
}

async function readJson(path) {
  try { return JSON.parse(await readFile(path, 'utf8')); }
  catch { throw new PilotReadError('A required private pilot JSON file is missing, unreadable or invalid.'); }
}
