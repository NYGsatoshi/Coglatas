import type { Page } from '@playwright/test';
import { threadFixtureIds } from '../ui/fixtures/thread-api';

/** Mocked transport timing only. This gate proves no backend authorization. */
export async function installThreadDiagnosticGate(page: Page) {
  const state = { hold: false, deny: false, rejectNextReply: false,
    held: [] as { release: () => void }[], gets: 0, rejectedReplies: 0 };
  const path = `/api/messages/${threadFixtureIds.rootMessageId}/thread`;
  await page.route('**/api/messages/**', async (route) => {
    const request = route.request(), pathname = new URL(request.url()).pathname;
    if (pathname === `${path}/messages` && request.method() === 'POST' && state.rejectNextReply) {
      state.rejectNextReply = false; state.rejectedReplies += 1;
      await route.fulfill({ status: 400, json: { error: 'Synthetic validation rejection.' } });
      return;
    }
    if (pathname !== path || request.method() !== 'GET') { await route.fallback(); return; }
    state.gets += 1;
    if (state.hold) {
      await new Promise<void>((resolve) => { state.held.push({ release: resolve }); });
    }
    if (state.deny) { await route.fulfill({ status: 400, json: { error: 'Message thread not found.' } }); }
    else { await route.fallback(); }
  });
  return state;
}
