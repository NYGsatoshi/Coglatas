// Match default UTF-16 code-unit ordering, including native string coercion.
export function compareCodeUnits(left, right) {
  const leftString = `${left}`;
  const rightString = `${right}`;
  if (leftString === rightString) return 0;
  return leftString < rightString ? -1 : 1;
}
