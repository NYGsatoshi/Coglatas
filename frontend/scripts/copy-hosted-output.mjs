import { cp, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const frontendDirectory = path.resolve(scriptDirectory, "..");
const repositoryRoot = path.resolve(frontendDirectory, "..");
const buildOutput = path.join(frontendDirectory, "dist", "coglatas-web");
const webRoot = path.join(repositoryRoot, "src", "Coglatas.Web", "wwwroot");

await rm(webRoot, { force: true, recursive: true });
await cp(buildOutput, webRoot, { recursive: true });

console.log(`Copied Angular build artifacts to ${webRoot}`);
