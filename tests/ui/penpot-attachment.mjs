import { mkdir } from 'node:fs/promises';
import path from 'node:path';

/**
 * Persist one Penpot screenshot and its layout evidence.
 * The default in-memory path avoids a temporary PNG read/write roundtrip.
 * Explicit capture directories retain the preexisting stable PNG path.
 */
export async function attachPenpotEvidence(
  page, testInfo, fileName, layout, directory = process.env.PENPOT_CAPTURE_DIRECTORY,
) {
  if (directory) {
    await mkdir(directory, { recursive: true });
    const imagePath = path.join(directory, `${fileName}.png`);
    await page.screenshot({ path: imagePath, fullPage: false, animations: 'disabled' });
    await testInfo.attach(fileName, { path: imagePath, contentType: 'image/png' });
  } else {
    const png = await page.screenshot({ fullPage: false, animations: 'disabled' });
    await testInfo.attach(fileName, { body: png, contentType: 'image/png' });
  }
  await testInfo.attach(`${fileName}-layout`, {
    body: JSON.stringify(layout),
    contentType: 'application/json',
  });
}
