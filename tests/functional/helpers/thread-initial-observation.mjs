export const INITIAL_THREAD_DOM_FLAGS = ['workspacePresent', 'workspaceVisible', 'loadingPresent', 'loadingVisible',
  'errorPresent', 'errorVisible', 'rootPresent', 'rootVisible'];
const PROJECTION_FLAGS = ['pagePresent', 'conversationPresent', 'threadOpenClass', 'groupClass'];
export const THREAD_SHAPE_FLAGS = ['objectShape', 'rootMatches', 'conversationMatches', 'summaryMatches',
  'replyIdentitiesMatch', 'countsConsistent'];
const exact = (value, fields) => value && !Array.isArray(value) && typeof value === 'object' &&
  Object.keys(value).length === fields.length && fields.every((field) => Object.hasOwn(value, field));
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

/** Diagnostics never join functional progress; sealing prevents late body completion from changing a receipt. */
export function observeInitialThreadShape(initial, readBody, expectedRoot, expectedConversation) {
  let open = true;
  void Promise.resolve().then(readBody).then((value) => {
    if (open) { initial.responseShape = threadResponseShape(value, expectedRoot, expectedConversation); }
  }).catch(() => { /* Unavailable diagnostics stay null; no native error is retained. */ });
  return () => { open = false; };
}

export function validateInitialThreadObservation(value) {
  const interleaving = Object.hasOwn(value ?? {}, 'preActionDeniedDom');
  if (!exact(value, interleaving ? ['responseShape', 'failureDom', 'preActionDeniedDom']
    : ['responseShape', 'failureDom']) ||
      (value.responseShape !== null && (!exact(value.responseShape, THREAD_SHAPE_FLAGS) ||
        THREAD_SHAPE_FLAGS.some((key) => typeof value.responseShape[key] !== 'boolean')))) {
    throw new Error('Initial Thread diagnostics are invalid.');
  }
  for (const [name, observation] of [['failureDom', value.failureDom],
    ...(interleaving ? [['preActionDeniedDom', value.preActionDeniedDom]] : [])]) {
    if (observation === null) { continue; }
    const fields = name === 'failureDom' && exact(observation, INITIAL_THREAD_DOM_FLAGS)
      ? INITIAL_THREAD_DOM_FLAGS : [...INITIAL_THREAD_DOM_FLAGS, ...PROJECTION_FLAGS];
    if (!exact(observation, fields) || fields.some((key) => typeof observation[key] !== 'boolean')) {
      throw new Error('Initial Thread DOM diagnostics are invalid.');
    }
    for (const [present, visible] of [['workspacePresent', 'workspaceVisible'],
      ['loadingPresent', 'loadingVisible'], ['errorPresent', 'errorVisible'], ['rootPresent', 'rootVisible']]) {
      if (observation[visible] && !observation[present]) {
        throw new Error('Initial Thread DOM diagnostics are inconsistent.');
      }
    }
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
    state.pagePresent = document.querySelectorAll('[data-testid="dm-page"]').length > 0;
    state.conversationPresent = document.querySelectorAll('[data-testid="dm-conversation-surface"]').length > 0;
    state.threadOpenClass = document.querySelectorAll('.dm-page__workspace.dm-page__workspace--thread-open').length > 0;
    state.groupClass = document.querySelectorAll('.dm-page__workspace.dm-page__workspace--group').length > 0;
    return state;
  });
}
