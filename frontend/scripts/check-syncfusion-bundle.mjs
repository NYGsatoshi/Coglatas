import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const outputRoot = fileURLToPath(new URL('../dist/coglatas-web/', import.meta.url));
const index = await readFile(join(outputRoot, 'index.html'), 'utf8');
// Same-origin runtime configuration is an HTTP endpoint, not a generated
// bundle file. Keep it out of filesystem inspection while retaining every
// Angular script emitted into index.html.
const initialScripts = [...index.matchAll(/src="([^"?]+\.js)"/gu)]
  .map((match) => match[1])
  .filter((script) => !script.startsWith('/api/'));

if (initialScripts.length === 0) {
  throw new Error('Bundle analysis could not identify initial JavaScript chunks.');
}

for (const script of initialScripts) {
  const contents = await readFile(join(outputRoot, script), 'utf8');
  if (contents.includes('@syncfusion/') || contents.includes('ej2-')) {
    throw new Error(`Syncfusion code entered the initial bundle: ${script}`);
  }
}

const bundleFiles = await readdir(outputRoot);
const lazyScripts = bundleFiles.filter((file) => file.endsWith('.js') && !initialScripts.includes(file));
const syncfusionChunk = await Promise.any(lazyScripts.map(async (file) => {
  const contents = await readFile(join(outputRoot, file), 'utf8');
  if (contents.includes('ejs-grid')) {
    return file;
  }
  throw new Error('not the Syncfusion grid chunk');
})).catch(() => null);

if (!syncfusionChunk) {
  throw new Error('Bundle analysis could not identify the lazy Syncfusion grid chunk.');
}

const ganttChunk = await Promise.any(lazyScripts.map(async (file) => {
  const contents = await readFile(join(outputRoot, file), 'utf8');
  if (contents.includes('ejs-gantt')) {
    return file;
  }
  throw new Error('not the Syncfusion Gantt chunk');
})).catch(() => null);

if (!ganttChunk) {
  throw new Error('Bundle analysis could not identify the lazy Syncfusion Gantt chunk.');
}

console.log(`Verified ${initialScripts.length} initial chunks, lazy Syncfusion grid chunk ${syncfusionChunk}, and lazy Syncfusion Gantt chunk ${ganttChunk}; ${bundleFiles.filter((file) => file.endsWith('.js')).length} JavaScript bundles inspected.`);
