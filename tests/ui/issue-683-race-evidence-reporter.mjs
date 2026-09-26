/* eslint-disable sort-vars -- Reporter helper declarations stay dependency-ordered so runtime evidence parsing remains explicit and no-use-before-define safe. */
import {
  ISSUE_683_PR03C_TITLE,
  ISSUE_683_SMOKE_EVIDENCE_ATTACHMENT,
  issue683RaceObservations
} from './issue-683-race-evidence.mjs';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

const EMPTY_TEXT = '',
  JSON_INDENT = 2,
  UTF8_ENCODING = 'utf8',
  /**
   * Read attachment body from either inline data or file path.
   * @param {object} attachment - Playwright attachment object with body or path
   * @returns {string} The attachment body as UTF-8 text
   */
  readAttachmentBody = (attachment) => {
    let body = EMPTY_TEXT;

    if (attachment.body) {
      body = Buffer.from(attachment.body).toString(UTF8_ENCODING);
    } else if (attachment.path) {
      body = readFileSync(attachment.path, UTF8_ENCODING);
    }

    return body;
  },
  /**
   * Parse race condition evidence from a test attachment.
   * @param {object} attachment - Playwright attachment containing smoke evidence JSON
   * @returns {object} Outcome with parseError (string or null) and raceObservations array
   */
  parseRaceEvidence = (attachment) => {
    const outcome = {
      parseError: null,
      raceObservations: []
    };

    if (!attachment) {
      outcome.parseError = `Missing ${ISSUE_683_SMOKE_EVIDENCE_ATTACHMENT} attachment.`;
      return outcome;
    }

    try {
      const body = readAttachmentBody(attachment);

      if (!body) {
        throw new Error('Attachment has neither an inline body nor a readable path.');
      }
      outcome.raceObservations = issue683RaceObservations(JSON.parse(body));
    } catch (error) {
      let parseError = String(error);

      if (error instanceof Error) {
        parseError = error.message;
      }
      outcome.parseError = parseError;
    }

    return outcome;
  },
  /**
   * Build a PR03C test record from test case and result data.
   * @param {object} testCase - Playwright test case object
   * @param {object} result - Playwright test result object
   * @returns {object} Record with attachment status, parse errors, race observations, retry count, and test status
   */
  buildPr03cRecord = (testCase, result) => {
    const attachment = result.attachments.find(
        (candidate) => candidate.name === ISSUE_683_SMOKE_EVIDENCE_ATTACHMENT
      ),
      parsed = parseRaceEvidence(attachment);

    return {
      attachmentFound: Boolean(attachment),
      parseError: parsed.parseError,
      raceObservations: parsed.raceObservations,
      retry: result.retry,
      status: result.status,
      title: testCase.title
    };
  },
  /**
   * Write collected evidence records and retries to a JSON file.
   * @param {string} outputPath - File path where evidence will be written
   * @param {Array<object>} records - Array of test records
   * @param {Array<number>} retries - Array of retry counts
   */
  writeEvidence = (outputPath, records, retries) => {
    const serialized = `${JSON.stringify({ records, retries }, null, JSON_INDENT)}\n`;

    mkdirSync(dirname(outputPath), { recursive: true });
    writeFileSync(outputPath, serialized, UTF8_ENCODING);
  };

/**
 * Playwright reporter that collects Issue 683 race condition evidence from PR03C test results.
 */
export default class Issue683RaceEvidenceReporter {
  records = [];

  retries = [];

  /**
   * Handle test end event by recording retry count and PR03C test details.
   * @param {object} testCase - The test case that ended
   * @param {object} result - The test result
   */
  onTestEnd(testCase, result) {
    this.retries.push(result.retry);
    if (testCase.title === ISSUE_683_PR03C_TITLE) {
      this.records.push(buildPr03cRecord(testCase, result));
    }
  }

  /**
   * Handle test run completion by writing evidence to the configured output file.
   */
  onEnd() {
    const outputPath = process.env.COGLATAS_ISSUE_683_EVIDENCE_FILE?.trim();

    if (!outputPath) {
      throw new Error('COGLATAS_ISSUE_683_EVIDENCE_FILE is required by the Issue #683 evidence reporter.');
    }
    writeEvidence(outputPath, this.records, this.retries);
  }
}
