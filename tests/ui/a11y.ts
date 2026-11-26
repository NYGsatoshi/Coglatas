import AxeBuilder from "@axe-core/playwright";
import { expect, type Page } from "@playwright/test";

function formatViolations(violations: Awaited<ReturnType<AxeBuilder["analyze"]>>["violations"]) {
  return violations
    .map((violation) => {
      const nodes = violation.nodes
        .map((node) => `    - ${node.target.join(", ")}: ${node.failureSummary ?? "No failure summary."}`)
        .join("\n");

      return `${violation.id}: ${violation.help}\n  Impact: ${violation.impact ?? "unknown"}\n${nodes}`;
    })
    .join("\n\n");
}

export async function expectNoAccessibilityViolations(page: Page, selector?: string) {
  const builder = new AxeBuilder({ page });
  const results = await (selector ? builder.include(selector) : builder).analyze();
  expect(results.violations, formatViolations(results.violations)).toEqual([]);
  // Allow shadow diagnostics to compare the unchanged authoritative result.
  return results;
}
