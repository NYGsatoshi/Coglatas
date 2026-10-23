export const INITIAL_THREAD_DOM_FLAGS = ['workspacePresent', 'workspaceVisible', 'loadingPresent', 'loadingVisible',
  'errorPresent', 'errorVisible', 'rootPresent', 'rootVisible'];
export const THREAD_SHAPE_FLAGS = ['objectShape', 'rootMatches', 'conversationMatches', 'summaryMatches',
  'replyIdentitiesMatch', 'countsConsistent'];
const exact = (value, fields) => value && !Array.isArray(value) && typeof value === 'object' &&
  Object.keys(value).sort().join() === [...fields].sort().join();
const record = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);

/** Compare actual DTO values in memory; export only fixed booleans, never a DTO or identity. */
export function threadResponseShape(value, expectedRoot, expectedConversation) {
  const objectShape = record(value) && record(value.rootMessage) && record(value.summary) && Array.isArray(value.replies);
  const root = objectShape ? value.rootMessage : {}, summary = objectShape ? value.summary : {};
  const replies = objectShape ? value.replies : [];
  const rootMatches = objectShape && typeof expectedRoot === 'string' && expectedRoot.length > 0 && root.id === expectedRoot;
  const conversationMatches = objectShape && typeof expectedConversation === 'string' && expectedConversation.length > 0 && root.conversationId === expectedConversation;
  return { objectShape, rootMatches, conversationMatches,
    summaryMatches: rootMatches && summary.threadRootMessageId === expectedRoot,
    replyIdentitiesMatch: rootMatches && conversationMatches && replies.every((reply) => record(reply) &&
      typeof reply.id === 'string' && reply.id.length > 0 && reply.threadRootMessageId === expectedRoot && reply.conversationId === expectedConversation) &&
      new Set(replies.map((reply) => reply.id)).size === replies.length,
    countsConsistent: objectShape && Number.isSafeInteger(value.maximumReplies) && value.maximumReplies > 0 &&
      Number.isSafeInteger(summary.replyCount) && summary.replyCount >= 0 && replies.length <= value.maximumReplies &&
      typeof value.hasMore === 'boolean' && (value.hasMore ? summary.replyCount > replies.length : summary.replyCount === replies.length) };
}

export function validateInitialThreadObservation(value) {
  if (!exact(value, ['responseShape', 'failureDom']) ||
      (value.responseShape !== null && (!exact(value.responseShape, THREAD_SHAPE_FLAGS) || THREAD_SHAPE_FLAGS.some((key) => typeof value.responseShape[key] !== 'boolean'))) ||
      (value.failureDom !== null && (!exact(value.failureDom, INITIAL_THREAD_DOM_FLAGS) || INITIAL_THREAD_DOM_FLAGS.some((key) => typeof value.failureDom[key] !== 'boolean')))) {
    throw new Error('Initial Thread diagnostics are invalid.');
  }
  for (const [present, visible] of [['workspacePresent', 'workspaceVisible'], ['loadingPresent', 'loadingVisible'], ['errorPresent', 'errorVisible'], ['rootPresent', 'rootVisible']]) {
    if (value.failureDom?.[visible] && !value.failureDom[present]) { throw new Error('Initial Thread diagnostics are inconsistent.'); }
  }
  return structuredClone(value);
}

/** One precleanup browser evaluation; no text, attributes, product globals or arbitrary selectors. */
export async function readInitialThreadDom(page) {
  return page.evaluate(() => {
    const state = {};
    for (const [name, selector] of [['workspace', '[data-testid="thread-workspace"]'],
      ['loading', '[data-testid="thread-preview"] > [role="status"]'], ['error', '[data-testid="thread-load-failure"]'],
      ['root', '[data-testid="thread-root-message"]']]) {
      const elements = document.querySelectorAll(selector);
      state[name + 'Present'] = elements.length > 0;
      state[name + 'Visible'] = [...elements].some((element) => {
        const bounds = element.getBoundingClientRect(), style = getComputedStyle(element);
        return bounds.width > 0 && bounds.height > 0 && style.visibility !== 'hidden' && style.visibility !== 'collapse';
      });
    }
    return state;
  });
}
