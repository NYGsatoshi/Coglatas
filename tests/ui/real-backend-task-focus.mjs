const stateKeys = [
  'activeControlKind', 'documentHasFocus', 'documentReadyState',
  'titlePresent', 'titleIsActiveElement', 'titleDisabled', 'titleConnected',
];
const activeControlKinds = new Set([
  'task-title', 'input', 'textarea', 'select', 'button', 'dialog', 'body', 'other', 'none',
]);
const documentReadyStates = new Set(['loading', 'interactive', 'complete']);
const phases = new Set(['before-assertion', 'assertion-failed']);
const captureTimeoutMs = 1_000;

// This self-contained function executes in the browser. Never return DOM data or values.
export function readTaskTitleFocusState() {
  const title = document.querySelector('[data-testid="task-create-title"]');
  const active = document.activeElement;
  let activeControlKind = 'other';
  if (!active) {
    activeControlKind = 'none';
  } else if (active === title) {
    activeControlKind = 'task-title';
  } else {
    switch (active.tagName) {
      case 'INPUT': activeControlKind = 'input'; break;
      case 'TEXTAREA': activeControlKind = 'textarea'; break;
      case 'SELECT': activeControlKind = 'select'; break;
      case 'BUTTON': activeControlKind = 'button'; break;
      case 'DIALOG': activeControlKind = 'dialog'; break;
      case 'BODY': activeControlKind = 'body'; break;
      default: break;
    }
  }
  return {
    activeControlKind,
    documentHasFocus: document.hasFocus(),
    documentReadyState: document.readyState,
    titlePresent: title !== null,
    titleIsActiveElement: title !== null && title === active,
    titleDisabled: title?.matches(':disabled') ?? false,
    titleConnected: title?.isConnected ?? false,
  };
}

export async function captureTaskTitleFocus(page, phase) {
  if (!phases.has(phase)) {
    throw new Error('Unsupported Task focus diagnostic phase.');
  }
  let timer;
  try {
    const raw = await Promise.race([
      page.evaluate(readTaskTitleFocusState),
      new Promise((resolve) => { timer = setTimeout(() => resolve(null), captureTimeoutMs); }),
    ]);
    const keys = Object.keys(raw);
    const state = {
      activeControlKind: raw.activeControlKind,
      documentHasFocus: raw.documentHasFocus,
      documentReadyState: raw.documentReadyState,
      titlePresent: raw.titlePresent,
      titleIsActiveElement: raw.titleIsActiveElement,
      titleDisabled: raw.titleDisabled,
      titleConnected: raw.titleConnected,
    };
    if (keys.length !== stateKeys.length || stateKeys.some((key) => !keys.includes(key)) ||
        !activeControlKinds.has(state.activeControlKind) ||
        !documentReadyStates.has(state.documentReadyState) ||
        stateKeys.slice(3).some((key) => typeof state[key] !== 'boolean') ||
        typeof state.documentHasFocus !== 'boolean') {
      return { phase, availability: 'unavailable' };
    }
    return { phase, availability: 'available', ...state };
  } catch {
    // Closed metadata only, including when the page or execution context has disappeared.
    return { phase, availability: 'unavailable' };
  } finally {
    clearTimeout(timer);
  }
}
