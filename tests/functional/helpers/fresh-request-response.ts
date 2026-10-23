import type { Page, Request, Response } from '@playwright/test';

/** Observe a newly emitted browser request, excluding responses already in flight. */
export async function responseToFreshRequest(
  page: Pick<Page, 'waitForRequest'>,
  predicate: (request: Request) => boolean,
  action: () => Promise<unknown>
): Promise<Response> {
  const requestPromise = page.waitForRequest(predicate);
  const [request] = await Promise.all([requestPromise, action()]);
  const response = await request.response();
  if (!response) {
    throw new Error('The fresh browser request completed without an HTTP response.');
  }
  return response;
}
