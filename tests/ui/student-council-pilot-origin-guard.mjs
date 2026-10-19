// Chromium Fetch interception pauses each redirect hop before transmission.
// Playwright context.route alone does not observe redirected requests.
// The caller validates the production HTTPS origin and installs this guard
// on every credential-bearing page before its first navigation.
export async function installChromiumOriginGuard(page, expectedOrigin, onViolation) {
  let session;
  try {
    session = await page.context().newCDPSession(page);
    session.on('Fetch.requestPaused', async ({ requestId, request }) => {
      let allowed = false;
      try { allowed = new URL(request.url).origin === expectedOrigin; } catch { /* Deny malformed URLs. */ }
      try {
        if (!allowed) {
          onViolation();
          await session.send('Fetch.failRequest', { requestId, errorReason: 'BlockedByClient' });
        } else {
          await session.send('Fetch.continueRequest', { requestId });
        }
      } catch {
        if (!page.isClosed()) onViolation();
      }
    });
    await session.send('Fetch.enable', { patterns: [{ urlPattern: '*', requestStage: 'Request' }] });
  } catch {
    throw new Error('Pilot Chromium redirect-aware origin guard could not be installed.');
  }
}
