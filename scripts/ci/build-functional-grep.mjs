import { pathToFileURL } from 'node:url';

import {
  FUNCTIONAL_BACKENDS,
  FUNCTIONAL_DOMAINS,
  FUNCTIONAL_GATES,
  FUNCTIONAL_POLARITIES,
  FUNCTIONAL_PRIORITIES,
  assertJourneyId,
  journeyTag,
  tagForDomain,
  tagForGate
} from './functional-tags.mjs';

/**
 * Build a Playwright --grep expression from explicit Functional metadata tags.
 * Multiple values inside one dimension are OR-ed; dimensions are AND-ed.
 *
 * @param {{
 *   gates?: string[] | string,
 *   domains?: string[] | string,
 *   priorities?: string[] | string,
 *   backends?: string[] | string,
 *   polarities?: string[] | string,
 *   journeys?: string[] | string,
 *   negativeAuthz?: boolean,
 *   releaseEvidence?: boolean
 * }} filters
 */
export function buildFunctionalGrep(filters = {}) {
  const tagGroups = [['@functional']];

  addChoiceGroup(tagGroups, filters.gates, FUNCTIONAL_GATES, tagForGate, 'gate');
  addChoiceGroup(tagGroups, filters.domains, FUNCTIONAL_DOMAINS, tagForDomain, 'domain');
  addChoiceGroup(tagGroups, filters.priorities, FUNCTIONAL_PRIORITIES, (value) => `@${value}`, 'priority');
  addChoiceGroup(
    tagGroups,
    filters.backends,
    FUNCTIONAL_BACKENDS,
    (value) => (value === 'real' ? '@real-backend' : '@mock-backend'),
    'backend'
  );
  addChoiceGroup(
    tagGroups,
    filters.polarities,
    FUNCTIONAL_POLARITIES,
    (value) => (value === 'positive' ? '@positive' : '@negative'),
    'polarity'
  );

  const journeys = normalizeValues(filters.journeys);
  if (journeys.length > 0) {
    tagGroups.push(journeys.map((journey) => journeyTag(assertJourneyId(journey))));
  }

  if (filters.negativeAuthz) {
    tagGroups.push(['@negative-authz']);
  }
  if (filters.releaseEvidence) {
    tagGroups.push(['@release-evidence']);
  }

  const assertions = tagGroups.map((tags) => exactTagLookahead(tags)).join('');
  return `^${assertions}[\\s\\S]*$`;
}

export function parseFunctionalGrepArguments(args) {
  const filters = {};
  const remaining = [...args];

  while (remaining.length > 0) {
    const argument = remaining.shift();
    switch (argument) {
      case '--gate':
        appendFilter(filters, 'gates', requireValue(argument, remaining));
        break;
      case '--domain':
        appendFilter(filters, 'domains', requireValue(argument, remaining));
        break;
      case '--priority':
        appendFilter(filters, 'priorities', requireValue(argument, remaining));
        break;
      case '--backend':
        appendFilter(filters, 'backends', requireValue(argument, remaining));
        break;
      case '--polarity':
        appendFilter(filters, 'polarities', requireValue(argument, remaining));
        break;
      case '--journey':
        appendFilter(filters, 'journeys', requireValue(argument, remaining));
        break;
      case '--negative-authz':
        filters.negativeAuthz = true;
        break;
      case '--release-evidence':
        filters.releaseEvidence = true;
        break;
      default:
        throw new Error(`Unknown Functional grep argument: ${argument}`);
    }
  }

  return filters;
}

if (isMainModule()) {
  try {
    const filters = parseFunctionalGrepArguments(process.argv.slice(2));
    process.stdout.write(buildFunctionalGrep(filters));
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  }
}

function addChoiceGroup(groups, rawValues, allowed, toTag, label) {
  const values = normalizeValues(rawValues);
  if (values.length === 0) {
    return;
  }

  for (const value of values) {
    if (!allowed.includes(value)) {
      throw new Error(`Invalid Functional ${label}: ${value}. Allowed values: ${allowed.join(', ')}.`);
    }
  }
  groups.push(values.map((value) => toTag(value)));
}

function normalizeValues(value) {
  if (value === undefined || value === null || value === '') {
    return [];
  }

  const values = Array.isArray(value) ? value : [value];
  return [
    ...new Set(
      values.flatMap((item) =>
        String(item)
          .split(',')
          .map((entry) => entry.trim())
          .filter(Boolean)
      )
    )
  ];
}

function exactTagLookahead(tags) {
  const alternatives = tags.map(escapeRegex).join('|');
  return `(?=[\\s\\S]*(?:^|\\s)(?:${alternatives})(?=\\s|$))`;
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');
}

function appendFilter(filters, key, value) {
  const current = filters[key] ?? [];
  filters[key] = [...current, value];
}

function requireValue(argument, remaining) {
  const value = remaining.shift();
  if (!value) {
    throw new Error(`${argument} requires a value.`);
  }
  return value;
}

function isMainModule() {
  const entryPoint = process.argv[1];
  return Boolean(entryPoint) && import.meta.url === pathToFileURL(entryPoint).href;
}
