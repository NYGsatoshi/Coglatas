import type { Page, Request, Response } from '@playwright/test';

export type FreshRequestPhase = 'OBSERVER_REGISTERED' | 'ACTION_STARTED' | 'ACTION_COMPLETED' |
  'ACTION_FAILED' | 'REQUEST_OBSERVED' | 'REQUEST_FAILED' | 'WAITING_RESPONSE' | 'RESPONSE_OBSERVED' | 'RESPONSE_MISSING' | 'RESPONSE_FAILED';

/** Observe a newly emitted browser request, excluding responses already in flight. */
export async function responseToFreshRequest(
  page: Pick<Page, 'waitForRequest'>,
  predicate: (request: Request) => boolean,
  action: () => Promise<unknown>,
  observe: (phase: FreshRequestPhase) => void = () => {}
): Promise<Response> {
  const requestPromise = page.waitForRequest(predicate).then((request) => {
    observe('REQUEST_OBSERVED');
    return request;
  }, (error: unknown) => { observe('REQUEST_FAILED'); throw error; });
  observe('OBSERVER_REGISTERED');
  const actionPromise = (async () => {
    observe('ACTION_STARTED');
    try { const value = await action(); observe('ACTION_COMPLETED'); return value; }
    catch (error) { observe('ACTION_FAILED'); throw error; }
  })();
  const [request] = await Promise.all([requestPromise, actionPromise]);
  observe('WAITING_RESPONSE');
  let response: Response | null;
  try { response = await request.response(); }
  catch (error) { observe('RESPONSE_FAILED'); throw error; }
  if (!response) {
    observe('RESPONSE_MISSING');
    throw new Error('The fresh browser request completed without an HTTP response.');
  }
  observe('RESPONSE_OBSERVED');
  return response;
}
