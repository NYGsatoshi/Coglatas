import { defineConfig } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import base from '../../playwright.config.ts';

// Run the unchanged serializer case; it requests no page or Product server.
export default defineConfig({
  ...base,
  testDir: fileURLToPath(new URL('.', import.meta.url)),
  testMatch: /student-council-workspace-channels\.spec\.ts$/,
  grep: /channel-dialog diagnostic attachment accepts only fixed bounded structural data$/,
  webServer: undefined,
  reporter: [['list']],
  outputDir: fileURLToPath(new URL('../../artifacts/sonar-channel-dialog-serializer', import.meta.url)),
  projects: base.projects.filter(project => project.name === 'chromium-desktop' || project.name === 'chromium-mobile'),
});
