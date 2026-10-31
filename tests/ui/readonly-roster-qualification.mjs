import { mkdir, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';

/** Return a secondary qualification error; never replace an existing owner failure. */
export async function finishRosterQualification({ ownerContext, evidence, testInfo, primaryFailure }) {
  let cleanupFailed = false, attachmentFailed = false;
  if (ownerContext) {
    try { await ownerContext.close(); evidence.ownerContextClosed = true; }
    catch { cleanupFailed = true; evidence.ownerContextClosed = false; evidence.status = 'NOT_VERIFIED'; }
  }
  try {
    const receiptPath = testInfo.outputPath('receipt-source.json');
    await mkdir(dirname(receiptPath), { recursive: true });
    // Preserve the auth-free host collector's readability across container users.
    await writeFile(receiptPath, JSON.stringify(evidence), { flag: 'wx', mode: 0o644 });
    await testInfo.attach('readonly-roster-structural-receipt.json', {
      contentType: 'application/json', path: receiptPath,
    });
  } catch { attachmentFailed = true; }
  if (cleanupFailed || attachmentFailed) {
    testInfo.annotations.push({ type: 'qualification', description: 'Roster cleanup or structural receipt capture failed.' });
    if (!primaryFailure) return new Error('Roster cleanup or structural receipt capture failed.');
  }
  return null;
}
