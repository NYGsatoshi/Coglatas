/** Bounded structural diagnostics: never persist source text, URLs, IDs or payloads. */
export const OBSERVATION_ATTACHMENT = 'functional-observation-v1';
export const NETWORK_LIMIT = 80;
export const PROJECTION_KEYS = ['filesPage', 'uploaderEnabled', 'inventoryRows', 'selectedRows', 'detailPane', 'detailHeading', 'downloadEnabled', 'errorIndicator'];
export const OPERATIONS = ['files.inventory', 'files.detail', 'files.download', 'files.grant', 'files.versions', 'files.sharing', 'files.move', 'files.folders', 'search', 'auth', 'workspace', 'project', 'task', 'message', 'notification', 'announcement', 'api.other'];
const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'];
const EVENTS = ['REQUEST', 'RESPONSE', 'NETWORK_FAILURE'];
const LEVELS = ['error', 'warning', 'info', 'debug', 'other', 'pageError'];
const STEP_PATTERNS = {
  'FUNC-TASK-001': /^STEP-(?:0[1-9]|1[01])$/,
  'FUNC-FILE-002': /^F05-(?:FAST-(?:0[1-9]|1[0-2])|FULL-0[1-4])$/,
  'FUNC-MSG-001': /^MSG-(?:0[1-4]|FULL|NEG)$/,
  'FUNC-NOTIF-001': /^NOTIF-(?:0[1-4]|FULL)$/,
  'FUNC-ANN-001': /^ANN-0[1-4]$/,
  'FUNC-AUTHZ-001': /^AUTHZ-0[1-9]$/,
  'FUNC-AUTHZ-002': /^AUTHZ-0[1-9]$/,
};

export function stableStepId(journeyId, title) {
  if (typeof title !== 'string') return 'OWNER';
  const match = title.match(/^([A-Z0-9-]+) \/ ([A-Z0-9-]+)(?:\s|$)/);
  return match?.[1] === journeyId && STEP_PATTERNS[journeyId]?.test(match[2]) ? match[2] : 'OWNER';
}

export function failureKind(result, failedStep) {
  if (result.status === 'passed') return 'NONE';
  if (result.status === 'timedOut') return 'TIMEOUT';
  if (result.status === 'skipped') return 'SKIPPED';
  if (result.status === 'interrupted') return 'INTERRUPTED';
  // Inspect errors in memory only. Output is a fixed enum, never an assertion body.
  const errors = [failedStep?.error, ...(result.errors ?? [])].filter(Boolean);
  if (errors.some((error) => error.name === 'TimeoutError' || /\b(?:Timeout|timed out|timeout)\b/.test(error.message ?? ''))) return 'TIMEOUT';
  if (errors.some((error) => /(?:apiRequestContext\.|net::ERR_|ECONNREFUSED|ECONNRESET)/.test(error.message ?? ''))) return 'REQUEST_FAILURE';
  if (errors.some((error) => error.matcherResult || /(?:expect\(|AssertionError)/.test(error.message ?? ''))) return 'ASSERTION';
  return 'UNCLASSIFIED_FAILURE';
}

export function endpointOperation(url, baseURL) {
  try {
    const parsed = new URL(url);
    if (parsed.origin !== new URL(baseURL).origin || !parsed.pathname.startsWith('/api/')) return null;
    const path = parsed.pathname;
    if (/^\/api\/file-download-grants\/[^/]+\/download\/?$/.test(path)) return 'files.download';
    if (/^\/api\/files\/?$/.test(path)) return 'files.inventory';
    if (/^\/api\/files\/[^/]+\/download\/?$/.test(path)) return 'files.download';
    if (/^\/api\/files\/[^/]+\/(?:download-grant|download-grants)\/?$/.test(path)) return 'files.grant';
    if (/^\/api\/files\/[^/]+\/versions(?:\/[^/]+(?:\/content)?)?\/?$/.test(path)) return 'files.versions';
    if (/^\/api\/files\/[^/]+\/(?:sharing(?:\/recipients(?:\/[^/]+)?)?|sharing-history)\/?$/.test(path)) return 'files.sharing';
    if (/^\/api\/files\/[^/]+\/move\/?$/.test(path)) return 'files.move';
    if (/^\/api\/files\/[^/]+\/?$/.test(path)) return 'files.detail';
    if (/^\/api\/file-folders(?:\/[^/]+)?\/?$/.test(path)) return 'files.folders';
    if (path === '/api/search') return 'search';
    for (const [prefix, operation] of [['auth', 'auth'], ['security', 'auth'], ['workspaces', 'workspace'], ['projects', 'project'], ['tasks', 'task'], ['conversations', 'message'], ['messages', 'message'], ['notifications', 'notification'], ['announcements', 'announcement'], ['announcement-drafts', 'announcement']]) {
      if (path === `/api/${prefix}` || path.startsWith(`/api/${prefix}/`)) return operation;
    }
    return 'api.other';
  } catch {
    return null;
  }
}

export function createBrowserObservation(baseURL) {
  const data = { schemaVersion: 1, scope: 'primary-browser-context', network: [], networkTruncated: 0,
    consoleCounts: Object.fromEntries(LEVELS.map((level) => [level, 0])),
    projection: Object.fromEntries(PROJECTION_KEYS.map((key) => [key, null])), projectionCheckpoint: 'after-test-cleanup', structuralSnapshotState: 'NOT_REQUIRED', structuralSnapshot: null };
  const requests = new WeakMap();
  let requestSequence = 0;
  const append = (event, request, status = null) => {
    const operation = endpointOperation(request.url(), baseURL), method = request.method();
    if (!operation || !METHODS.includes(method)) return;
    if (!requests.has(request)) requests.set(request, ++requestSequence);
    data.network.push({ event, requestSequence: requests.get(request), method, operation, status });
    if (data.network.length > NETWORK_LIMIT) { data.network.shift(); data.networkTruncated = Math.min(data.networkTruncated + 1, 1000000); }
  };
  return { data, onRequest: (request) => append('REQUEST', request),
    onResponse: (response) => append('RESPONSE', response.request(), response.status()),
    onRequestFailed: (request) => append('NETWORK_FAILURE', request),
    onConsole: (message) => { const level = LEVELS.includes(message.type()) ? message.type() : 'other'; data.consoleCounts[level] = Math.min(data.consoleCounts[level] + 1, 1000000); },
    onPageError: () => { data.consoleCounts.pageError = Math.min(data.consoleCounts.pageError + 1, 1000000); } };
}

function exactKeys(value, expected) {
  return value && !Array.isArray(value) && typeof value === 'object' && Object.keys(value).sort().join(',') === [...expected].sort().join(',');
}
function integer(value, maximum) { return Number.isSafeInteger(value) && value >= 0 && value <= maximum; }

export function validateBrowserObservation(data) {
  if (!exactKeys(data, ['schemaVersion', 'scope', 'network', 'networkTruncated', 'consoleCounts', 'projection', 'projectionCheckpoint', 'structuralSnapshotState', 'structuralSnapshot']) || data.schemaVersion !== 1 || data.scope !== 'primary-browser-context' || !['failed-step', 'after-test-cleanup'].includes(data.projectionCheckpoint) || !['NOT_REQUIRED', 'CAPTURED', 'UNAVAILABLE'].includes(data.structuralSnapshotState)) throw new Error('Invalid bounded browser diagnostic schema.');
  if (data.structuralSnapshotState === 'CAPTURED') {
    if (!exactKeys(data.structuralSnapshot, ['name', 'sha256']) || !/^structural-(?:core|files|collaboration|authz-negative)-FUNC-[A-Z]+-\d{3}-[0-9a-f]{40}-[1-9]\d{0,19}-[1-9]\d{0,8}-\d{1,4}\.png$/.test(data.structuralSnapshot.name) || !/^[0-9a-f]{64}$/.test(data.structuralSnapshot.sha256)) throw new Error('Invalid structural snapshot provenance.');
  } else if (data.structuralSnapshot !== null) throw new Error('Unexpected structural snapshot provenance.');
  if (!Array.isArray(data.network) || data.network.length > NETWORK_LIMIT || !integer(data.networkTruncated, 1000000)) throw new Error('Invalid bounded browser network diagnostics.');
  for (const entry of data.network) {
    if (!exactKeys(entry, ['event', 'requestSequence', 'method', 'operation', 'status']) || !EVENTS.includes(entry.event) || !METHODS.includes(entry.method) || !OPERATIONS.includes(entry.operation) || !integer(entry.requestSequence, 1000000) || entry.requestSequence === 0 || (entry.event === 'RESPONSE' ? !integer(entry.status, 599) || entry.status < 100 : entry.status !== null)) throw new Error('Invalid bounded browser event.');
  }
  if (!exactKeys(data.consoleCounts, LEVELS) || Object.values(data.consoleCounts).some((count) => !integer(count, 1000000))) throw new Error('Invalid console counters.');
  if (!exactKeys(data.projection, PROJECTION_KEYS) || Object.values(data.projection).some((value) => value !== null && typeof value !== 'boolean')) throw new Error('Invalid structural projection.');
  return data;
}

/** A fresh unauthenticated page renders only this validated structural record. */
export function structuralSnapshotHtml(observation, identity) {
  const data = validateBrowserObservation(observation);
  let provenance = 'Execution identity was not supplied.';
  if (identity !== undefined) {
    if (!exactKeys(identity, ['commitSha', 'runId', 'runAttempt', 'domain', 'journeyId', 'gate', 'retry']) || !/^[0-9a-f]{40}$/.test(identity.commitSha) || !/^[1-9]\d{0,19}$/.test(identity.runId) || !/^[1-9]\d{0,8}$/.test(identity.runAttempt) || !['core', 'files', 'collaboration', 'authz-negative'].includes(identity.domain) || !Object.hasOwn(STEP_PATTERNS, identity.journeyId) || !['functional-fast', 'functional-full', 'functional-extended'].includes(identity.gate) || !integer(identity.retry, 1000)) throw new Error('Invalid structural snapshot execution identity.');
    provenance = `Candidate: ${identity.commitSha}<br>Run: ${identity.runId}; attempt: ${identity.runAttempt}; test retry: ${identity.retry}<br>Gate: ${identity.gate}; domain: ${identity.domain}; Journey: ${identity.journeyId}`;
  }
  const rows = Object.entries(data.projection).map(([key, value]) => `<tr><td>${key}</td><td>${value === null ? 'not observed' : value}</td></tr>`).join('');
  const events = data.network.slice(-16).map((event) => `<tr><td>${event.event}</td><td>${event.method}</td><td>${event.operation}</td><td>${event.status ?? '-'}</td></tr>`).join('');
  const consoleCounts = Object.entries(data.consoleCounts).map(([level, count]) => `${level}=${count}`).join('; ');
  return `<!doctype html><meta charset="utf-8"><title>Functional structural diagnostic</title><style>body{font:16px sans-serif;margin:32px;color:#17212b;background:#f6f8fa}table{border-collapse:collapse;background:white;margin:16px 0;width:100%}td,th{padding:7px 12px;border:1px solid #c9d1d9;text-align:left}h1{font-size:24px}p{line-height:1.4}</style><h1>Functional structural diagnostic</h1><p>Generated diagnostic panel. This is not a screenshot of the authenticated product.<br>Primary browser context; checkpoint: ${data.projectionCheckpoint}. No protected DOM text or pixels.</p><p>${provenance}</p><h2>Files projection</h2><table>${rows}</table><h2>Console severity counts</h2><p>${consoleCounts}</p><h2>Last bounded browser status events</h2><table><tr><th>Event</th><th>Method</th><th>Operation</th><th>Status</th></tr>${events}</table>`;
}

export function attemptDiagnostic(journeyId, result) {
  const flatten = (entries) => entries.flatMap((step) => [step, ...flatten(step.steps ?? [])]);
  const steps = flatten(result.steps ?? []).filter((step) => step.category === 'test.step');
  // Prefer the deepest failed stable child over its enclosing owner step.
  const failed = steps.filter((step) => step.error).findLast((step) => stableStepId(journeyId, step.title) !== 'OWNER')
    ?? steps.find((step) => step.error);
  const matches = (result.attachments ?? []).filter((attachment) => attachment.name === OBSERVATION_ATTACHMENT);
  let browser = null, browserEvidenceState = 'MISSING';
  if (matches.length === 1 && matches[0].body && matches[0].body.length <= 32768) {
    try { browser = validateBrowserObservation(JSON.parse(matches[0].body.toString('utf8'))); browserEvidenceState = 'COMPLETE'; } catch { browserEvidenceState = 'INCOMPLETE'; }
  } else if (matches.length) browserEvidenceState = 'INCOMPLETE';
  return { retry: Math.min(Math.max(result.retry ?? 0, 0), 1000), status: ['passed', 'failed', 'timedOut', 'skipped', 'interrupted'].includes(result.status) ? result.status : 'interrupted',
    failureKind: failureKind(result, failed), failedStepId: result.status === 'passed' ? null : stableStepId(journeyId, failed?.title),
    completedStepIds: [...new Set(steps.filter((step) => !step.error).map((step) => stableStepId(journeyId, step.title)).filter((id) => id !== 'OWNER'))],
    browserEvidenceState, browser };
}
