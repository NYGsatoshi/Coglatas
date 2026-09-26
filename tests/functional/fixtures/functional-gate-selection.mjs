const KNOWN_GATES = new Set([
  'functional-fast',
  'functional-full',
  'functional-extended',
  'functional-release'
]);
const FULL_EXPANSION_GATES = new Set([
  'functional-full',
  'functional-extended',
  'functional-release'
]);

export function selectedFunctionalGates(raw = process.env.COGLATAS_FUNCTIONAL_SELECTED_GATES) {
  return [
    ...new Set(
      String(raw ?? '')
        .split(',')
        .map((value) => value.trim())
        .filter(Boolean)
    )
  ];
}

/**
 * An unscoped/direct journey invocation exercises the complete owner path.
 * Only an explicit bounded gate selection is allowed to omit the full steps.
 */
export function functionalFullExpansionEnabled(raw = process.env.COGLATAS_FUNCTIONAL_SELECTED_GATES) {
  const gates = selectedFunctionalGates(raw);
  const unknown = gates.filter((gate) => !KNOWN_GATES.has(gate));
  if (unknown.length > 0) {
    throw new Error(`Unknown Functional gate selection: ${unknown.join(', ')}.`);
  }
  return gates.length === 0 || gates.some((gate) => FULL_EXPANSION_GATES.has(gate));
}
