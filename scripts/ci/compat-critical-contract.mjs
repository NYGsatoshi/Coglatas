import { readFile, realpath } from 'node:fs/promises';
import path from 'node:path';

const CONTRACT_NAME = 'compat-critical';
const ALLOWED_STATUS = new Set(['active', 'obsolete', 'superseded']);
const TEST_ID_PATTERN = /^COMPAT-[A-Z0-9]+-[0-9]{3}$/u;
const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/u;

export async function loadCompatCriticalContract(contractPath, options = {}) {
  let parsed;
  try {
    parsed = JSON.parse(await readFile(contractPath, 'utf8'));
  } catch (error) {
    throw new Error(`Unable to read compat-critical contract ${contractPath}: ${error instanceof Error ? error.message : error}`);
  }

  return validateCompatCriticalContract(parsed, options);
}

export function validateCompatCriticalContract(contract, options = {}) {
  const today = toIsoDate(options.now ?? new Date());
  assertPlainObject(contract, 'contract');

  if (contract.schemaVersion !== 1) {
    throw new Error(`compat-critical schemaVersion must be 1; received ${String(contract.schemaVersion)}.`);
  }
  if (contract.name !== CONTRACT_NAME) {
    throw new Error(`compat-critical contract name must be ${CONTRACT_NAME}.`);
  }

  const requiredCategories = readUniqueStrings(contract.requiredCategories, 'requiredCategories');
  const profiles = readProfiles(contract.profiles, requiredCategories);
  if (!profiles.has(contract.defaultProfile)) {
    throw new Error(`defaultProfile does not exist: ${String(contract.defaultProfile)}.`);
  }

  if (!Array.isArray(contract.tests) || contract.tests.length === 0) {
    throw new Error('compat-critical contract must contain at least one test.');
  }

  const ids = new Set();
  const titles = new Set();
  const tests = contract.tests.map((entry, index) => normalizeTest(entry, index, profiles, requiredCategories, ids, titles));
  const testsById = new Map(tests.map((entry) => [entry.id, entry]));
  const quarantines = readQuarantines(contract.quarantines, testsById, today);
  const quarantinedIds = new Set(quarantines.map((entry) => entry.testId));

  for (const [profileName, profile] of profiles) {
    const referenced = tests.filter((entry) => entry.profiles.includes(profileName));
    const nonActive = referenced.filter((entry) => entry.status !== 'active');
    if (nonActive.length > 0) {
      throw new Error(
        `Profile ${profileName} references obsolete/superseded tests: ${nonActive.map((entry) => entry.id).join(', ')}.`
      );
    }

    const selected = referenced.filter((entry) => !quarantinedIds.has(entry.id));
    if (selected.length === 0) {
      throw new Error(`Profile ${profileName} selects zero non-quarantined compat-critical tests.`);
    }

    const covered = new Set(selected.flatMap((entry) => entry.categories));
    const missing = profile.requiredCategories.filter((category) => !covered.has(category));
    if (missing.length > 0) {
      throw new Error(`Profile ${profileName} is missing required coverage categories: ${missing.join(', ')}.`);
    }
  }

  const activeCoverage = new Set(
    tests.filter((entry) => entry.status === 'active' && !quarantinedIds.has(entry.id)).flatMap((entry) => entry.categories)
  );
  const globallyMissing = requiredCategories.filter((category) => !activeCoverage.has(category));
  if (globallyMissing.length > 0) {
    throw new Error(`compat-critical contract is missing active coverage categories: ${globallyMissing.join(', ')}.`);
  }

  return {
    schemaVersion: 1,
    name: CONTRACT_NAME,
    defaultProfile: contract.defaultProfile,
    requiredCategories,
    profiles,
    tests,
    quarantines,
    quarantinedIds
  };
}

export function selectCompatCriticalTests(contract, profileName = contract.defaultProfile) {
  if (!contract.profiles.has(profileName)) {
    throw new Error(`Unknown compat-critical profile: ${profileName}.`);
  }

  const selected = contract.tests.filter(
    (entry) => entry.status === 'active' && entry.profiles.includes(profileName) && !contract.quarantinedIds.has(entry.id)
  );
  if (selected.length === 0) {
    throw new Error(`Profile ${profileName} selects zero compat-critical tests.`);
  }
  return selected;
}

export function buildCompatCriticalGrep(contract, profileName = contract.defaultProfile) {
  const tests = selectCompatCriticalTests(contract, profileName);
  const escaped = tests.map((entry) => escapeRegExp(entry.title));
  return `(?:^|\\s)(?:${escaped.join('|')})$`;
}

export function selectedCompatCriticalFiles(contract, profileName = contract.defaultProfile) {
  return [...new Set(selectCompatCriticalTests(contract, profileName).map((entry) => entry.file))].sort();
}

export async function verifyCompatCriticalSources(contract, repositoryRoot = process.cwd()) {
  const sources = new Map();
  for (const entry of contract.tests.filter((test) => test.status === 'active')) {
    let source = sources.get(entry.file);
    if (source === undefined) {
      const rootFull = path.resolve(repositoryRoot);
      const absolutePath = path.resolve(rootFull, entry.file);
      const rel = path.relative(rootFull, absolutePath);
      if (rel === '..' || rel.startsWith(`..${path.sep}`) || path.isAbsolute(rel)) {
        throw new Error('Invalid file path');
      }
      // A source path may be lexically inside the checkout but follow a symlink outside it.
      let canonicalRoot;
      let canonicalSource;
      try {
        canonicalRoot = await realpath(rootFull);
        canonicalSource = await realpath(absolutePath);
      } catch (error) {
        throw new Error(`compat-critical source file is missing: ${entry.file} (${error instanceof Error ? error.message : error}).`);
      }
      const actualRel = path.relative(canonicalRoot, canonicalSource);
      if (actualRel === '..' || actualRel.startsWith(`..${path.sep}`) || path.isAbsolute(actualRel)) {
        throw new Error('Invalid file path');
      }
      try {
        source = await readFile(canonicalSource, 'utf8');
      } catch (error) {
        throw new Error(`compat-critical source file is missing: ${entry.file} (${error instanceof Error ? error.message : error}).`);
      }
      sources.set(entry.file, source);
    }

    const locations = findTestDeclarationLocations(source, entry.title);
    if (locations.length !== 1) {
      throw new Error(
        `compat-critical test ${entry.id} must resolve to exactly one Playwright test declaration in ${entry.file}; found ${locations.length}.`
      );
    }

    const testStart = locations[0];
    const testEnd = findNextTestDeclaration(source, testStart + 1);
    const testSource = source.slice(testStart, testEnd < 0 ? source.length : testEnd);

    const unsafePatterns = [
      { pattern: /\bwaitForTimeout\s*\(/u, label: 'waitForTimeout/arbitrary sleep' },
      { pattern: /\bsetTimeout\s*\(/u, label: 'setTimeout/arbitrary sleep' },
      { pattern: /\bMath\.random\s*\(/u, label: 'Math.random/unseeded randomness' },
      { pattern: /\brandomUUID\s*\(/u, label: 'randomUUID/unseeded randomness' },
      { pattern: /\btoHaveScreenshot\s*\(/u, label: 'pixel screenshot assertion' },
      { pattern: /\btoMatchSnapshot\s*\(/u, label: 'snapshot-only assertion' },
      {
        pattern: /\b(?:test|testInfo)\s*(?:(?:\.|\?\.)\s*(?:skip|fixme|fail)|(?:\?\.\s*)?\[\s*(?:"(?:skip|fixme|fail)"|'(?:skip|fixme|fail)')\s*\])\s*(?:\?\.\s*)?\(/u,
        label: 'inline skip/fixme/expected-failure exception'
      }
    ];
    for (const { pattern, label } of unsafePatterns) {
      if (pattern.test(testSource)) {
        throw new Error(`compat-critical test ${entry.id} contains disallowed ${label}.`);
      }
    }

    const nearestDescribe = findNearestDescribeDeclaration(source, testStart);
    if (nearestDescribe && /test\.describe\.(?:skip|fixme)\s*\(/u.test(nearestDescribe)) {
      throw new Error(`compat-critical test ${entry.id} is nested under a skipped/fixme describe block.`);
    }
  }
}

export function verifyCompatCriticalDiscovery(contract, profileName, output) {
  const selected = selectCompatCriticalTests(contract, profileName);
  const discoveredLines = String(output)
    .split(/\r?\n/u)
    .map((line) => line.trim())
    .filter((line) => /^\[[^\]]+\]\s+›\s+/u.test(line));

  if (discoveredLines.length === 0) {
    throw new Error(`Playwright discovered zero compat-critical tests for profile ${profileName}.`);
  }

  const expectedTitles = new Set(selected.map((entry) => entry.title));
  const discoveredTitles = [];
  for (const line of discoveredLines) {
    const matched = selected.filter((entry) => line.endsWith(`› ${entry.title}`));
    if (matched.length !== 1) {
      throw new Error(`Unexpected or ambiguous compat-critical Playwright discovery line: ${line}`);
    }
    discoveredTitles.push(matched[0].title);
  }

  for (const title of expectedTitles) {
    const count = discoveredTitles.filter((candidate) => candidate === title).length;
    if (count !== 1) {
      throw new Error(`Expected exactly one discovered compat-critical test titled "${title}"; found ${count}.`);
    }
  }

  if (discoveredTitles.length !== selected.length) {
    throw new Error(
      `compat-critical discovery count mismatch for ${profileName}: expected ${selected.length}, found ${discoveredTitles.length}.`
    );
  }

  return { selectedCount: selected.length, discoveredCount: discoveredTitles.length };
}

function readProfiles(value, requiredCategories) {
  assertPlainObject(value, 'profiles');
  const profiles = new Map();
  for (const [name, profile] of Object.entries(value)) {
    assertPlainObject(profile, `profiles.${name}`);
    const profileCategories = readUniqueStrings(profile.requiredCategories, `profiles.${name}.requiredCategories`);
    const unknown = profileCategories.filter((category) => !requiredCategories.includes(category));
    if (unknown.length > 0) {
      throw new Error(`Profile ${name} references unknown categories: ${unknown.join(', ')}.`);
    }
    if (typeof profile.description !== 'string' || profile.description.trim().length === 0) {
      throw new Error(`Profile ${name} requires a non-empty description.`);
    }
    profiles.set(name, { name, description: profile.description.trim(), requiredCategories: profileCategories });
  }
  if (profiles.size === 0) {
    throw new Error('compat-critical contract must define at least one profile.');
  }
  return profiles;
}

function normalizeTest(entry, index, profiles, requiredCategories, ids, titles) {
  assertPlainObject(entry, `tests[${index}]`);
  const id = readNonEmptyString(entry.id, `tests[${index}].id`);
  if (!TEST_ID_PATTERN.test(id)) {
    throw new Error(`Invalid compat-critical test id: ${id}.`);
  }
  if (ids.has(id)) {
    throw new Error(`Duplicate compat-critical test id: ${id}.`);
  }
  ids.add(id);

  const title = readNonEmptyString(entry.title, `tests[${index}].title`);
  if (titles.has(title)) {
    throw new Error(`Duplicate compat-critical test title: ${title}.`);
  }
  titles.add(title);

  const file = readNonEmptyString(entry.file, `tests[${index}].file`);
  if (!file.startsWith('tests/ui/') || !file.endsWith('.spec.ts') ||
      file.split('/').some((segment) => segment === '.' || segment === '..') || file.includes('\\\\')) {
    throw new Error(`compat-critical source must be a tests/ui/*.spec.ts Playwright file: ${file}.`);
  }

  const categories = readUniqueStrings(entry.categories, `tests[${index}].categories`);
  const unknownCategories = categories.filter((category) => !requiredCategories.includes(category));
  if (unknownCategories.length > 0) {
    throw new Error(`Test ${id} references unknown categories: ${unknownCategories.join(', ')}.`);
  }

  const profileNames = readUniqueStrings(entry.profiles, `tests[${index}].profiles`);
  const unknownProfiles = profileNames.filter((profile) => !profiles.has(profile));
  if (unknownProfiles.length > 0) {
    throw new Error(`Test ${id} references unknown profiles: ${unknownProfiles.join(', ')}.`);
  }

  const status = readNonEmptyString(entry.status, `tests[${index}].status`);
  if (!ALLOWED_STATUS.has(status)) {
    throw new Error(`Test ${id} has unsupported status: ${status}.`);
  }
  if (status !== 'active' && profileNames.length > 0) {
    throw new Error(`Obsolete/superseded test ${id} must not remain attached to a selection profile.`);
  }

  return { id, title, file, categories, profiles: profileNames, status };
}

function readQuarantines(value, testsById, today) {
  if (!Array.isArray(value)) {
    throw new Error('quarantines must be an array.');
  }
  const seen = new Set();
  return value.map((entry, index) => {
    assertPlainObject(entry, `quarantines[${index}]`);
    const testId = readNonEmptyString(entry.testId, `quarantines[${index}].testId`);
    if (!testsById.has(testId)) {
      throw new Error(`Quarantine references unknown compat-critical test: ${testId}.`);
    }
    if (seen.has(testId)) {
      throw new Error(`Duplicate quarantine entry for ${testId}.`);
    }
    seen.add(testId);

    const reason = readNonEmptyString(entry.reason, `quarantines[${index}].reason`);
    const owner = readNonEmptyString(entry.owner, `quarantines[${index}].owner`);
    const issue = readNonEmptyString(entry.issue, `quarantines[${index}].issue`);
    const expiresOn = readNonEmptyString(entry.expiresOn, `quarantines[${index}].expiresOn`);
    if (!ISO_DATE_PATTERN.test(expiresOn) || Number.isNaN(Date.parse(`${expiresOn}T00:00:00Z`))) {
      throw new Error(`Quarantine ${testId} has invalid expiresOn date: ${expiresOn}.`);
    }
    if (expiresOn < today) {
      throw new Error(`Quarantine ${testId} expired on ${expiresOn}; remove or renew it explicitly.`);
    }
    return { testId, reason, owner, issue, expiresOn };
  });
}

function readUniqueStrings(value, label) {
  if (!Array.isArray(value) || value.length === 0) {
    throw new Error(`${label} must be a non-empty array.`);
  }
  const values = value.map((item, index) => readNonEmptyString(item, `${label}[${index}]`));
  const duplicates = values.filter((item, index) => values.indexOf(item) !== index);
  if (duplicates.length > 0) {
    throw new Error(`${label} contains duplicates: ${[...new Set(duplicates)].join(', ')}.`);
  }
  return values;
}

function readNonEmptyString(value, label) {
  if (typeof value !== 'string' || value.trim().length === 0 || value !== value.trim()) {
    throw new Error(`${label} must be a trimmed non-empty string.`);
  }
  return value;
}

function assertPlainObject(value, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${label} must be an object.`);
  }
}

function findTestDeclarationLocations(source, title) {
  const escaped = escapeRegExp(title);
  const pattern = new RegExp(`\\btest(?:\\.(?:only|skip|fixme|fail|slow))?\\(\\s*(['\"])${escaped}\\1\\s*,`, 'gu');
  return [...source.matchAll(pattern)].map((match) => match.index ?? -1).filter((index) => index >= 0);
}

function findNextTestDeclaration(source, fromIndex) {
  const pattern = /\n\s*test(?:\.(?:only|skip|fixme|fail|slow))?\(\s*['"]/gu;
  pattern.lastIndex = fromIndex;
  const match = pattern.exec(source);
  return match?.index ?? -1;
}

function findNearestDescribeDeclaration(source, testStart) {
  const prefix = source.slice(0, testStart);
  const matches = [...prefix.matchAll(/\btest\.describe(?:\.(?:skip|fixme|only))?\s*\(/gu)];
  if (matches.length === 0) {
    return '';
  }
  return matches.at(-1)?.[0] ?? '';
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');
}

function toIsoDate(value) {
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) {
    throw new Error(`Invalid validation date: ${String(value)}.`);
  }
  return date.toISOString().slice(0, 10);
}
