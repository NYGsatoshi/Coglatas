import type { Locator } from '@playwright/test';

/** Measure native scrolling without accepting an in-flight animation frame. */
export async function readSettledScrollTop(locator: Locator): Promise<number> {
  return locator.evaluate(async element => {
    const positions = [element.scrollTop];
    for (let frame = 0; frame < 2; frame += 1) {
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
      positions.push(element.scrollTop);
    }
    // NaN fails both the exact Home and positive PageDown assertions. The next
    // existing expect.poll sample observes the real browser position again.
    return positions.every(position => position === positions[0]) ? positions[0] : Number.NaN;
  });
}
