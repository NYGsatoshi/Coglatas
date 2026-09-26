export type FixtureAliasMap = Readonly<Record<string, string>>;

const ALIAS_PATTERN = /^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$/u;

export function loadFixtureAliases(raw = process.env.COGLATAS_FUNCTIONAL_FIXTURE_ALIASES): FixtureAliasMap {
  if (!raw) {
    return Object.freeze({});
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new Error('COGLATAS_FUNCTIONAL_FIXTURE_ALIASES must be valid JSON.');
  }

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('COGLATAS_FUNCTIONAL_FIXTURE_ALIASES must be a JSON object of alias -> stable ID.');
  }

  const aliases: Record<string, string> = {};
  for (const [alias, value] of Object.entries(parsed)) {
    validateAlias(alias);
    if (typeof value !== 'string' || value.length === 0) {
      throw new Error(`Fixture alias '${alias}' must map to a non-empty string ID.`);
    }
    aliases[alias] = value;
  }

  return Object.freeze(aliases);
}

export function resolveFixtureAlias(aliases: FixtureAliasMap, alias: string): string {
  validateAlias(alias);
  const value = aliases[alias];
  if (!value) {
    throw new Error(`Required Functional fixture alias '${alias}' was not provisioned.`);
  }
  return value;
}

function validateAlias(alias: string): void {
  if (!ALIAS_PATTERN.test(alias)) {
    throw new Error(`Invalid Functional fixture alias '${alias}'. Use lowercase dotted/dashed aliases.`);
  }
}
