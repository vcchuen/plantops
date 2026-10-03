import { expect, test } from '@playwright/test';
import {
  PART_NUMBER,
  actionButton,
  asUser,
  confirmInDialog,
  openPart,
  raiseWorkOrder,
} from './helpers';

test('work order lifecycle across operator, supervisor and technician; stock drops after completion', async ({
  browser,
}) => {
  test.setTimeout(180_000);
  const title = `E2E nozzle jam ${Date.now()}`;

  // On-hand before, read as the supervisor (inventory is visible to every signed-in role).
  const part = await asUser(browser, 'sam', (page) => openPart(page, PART_NUMBER));

  // 1. Olivia (operator) raises the work order.
  const workOrderUrl = await asUser(browser, 'olivia', (page) => raiseWorkOrder(page, title));

  // 2. Sam approves, then assigns Tom.
  await asUser(browser, 'sam', async (page) => {
    await page.goto(workOrderUrl);
    await actionButton(page, 'Approve').click();
    await confirmInDialog(page, 'Approve');

    await expect(actionButton(page, 'Assign')).toBeVisible();
    await actionButton(page, 'Assign').click();
    await confirmInDialog(page, 'Assign', async (dialog) => {
      await dialog.getByRole('combobox', { name: 'Technician' }).click();
      await page.getByRole('option', { name: /tom/i }).click();
    });
    await expect(page.getByText('Tom', { exact: false }).first()).toBeVisible();
  });

  // 3. Tom reserves 2 x NZL-CN040, starts, completes.
  await asUser(browser, 'tom', async (page) => {
    await page.goto(workOrderUrl);
    await expect(page.getByRole('button', { name: 'Reserve part' })).toBeVisible();
    await page.getByRole('button', { name: 'Reserve part' }).click();

    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Part', { exact: true }).fill(PART_NUMBER);
    await page.getByRole('option', { name: new RegExp(PART_NUMBER) }).click();
    await dialog.getByLabel('Quantity').fill('2');
    await dialog.getByRole('button', { name: 'Reserve', exact: true }).click();
    await expect(dialog).toBeHidden();

    const reserved = page.getByRole('table', { name: 'Reserved parts' });
    await expect(reserved).toContainText(PART_NUMBER);
    await expect(reserved).toContainText('2 pcs');

    await actionButton(page, 'Start work').click();
    await expect(actionButton(page, 'Complete')).toBeVisible();
    await actionButton(page, 'Complete').click();
    await confirmInDialog(page, 'Complete', async (d) => {
      await d
        .getByLabel('Resolution')
        .fill('Replaced the clogged nozzle and re-ran the line check.');
    });
    await expect(page.getByText('Stock is updated shortly after completion.')).toBeVisible();
  });

  // 4. Sam closes it, then the history shows each step by the right person, in order.
  const expected = [
    ['Submitted', /olivia/i],
    ['Approved', /sam/i],
    ['Assigned', /sam/i],
    ['Work started', /tom/i],
    ['Completed', /tom/i],
    ['Closed', /sam/i],
  ] as const;

  await asUser(browser, 'sam', async (page) => {
    await page.goto(workOrderUrl);
    await expect(actionButton(page, 'Close')).toBeVisible();
    await actionButton(page, 'Close').click();
    await expect(actionButton(page, 'Close')).toHaveCount(0);

    await page.reload();
    const entries = page.locator('ol.timeline > li .what');
    await expect(entries).toHaveCount(expected.length, { timeout: 15_000 });

    // The timeline may be oldest-first or newest-first; normalise before comparing.
    let texts = (await entries.allInnerTexts()).map((t) => t.replace(/\s+/g, ' ').trim());
    if (texts[0]?.startsWith('Closed')) texts = texts.reverse();
    expected.forEach(([label, actor], i) => {
      expect(texts[i], `history entry ${i + 1}`).toContain(label);
      expect(texts[i], `history entry ${i + 1} actor`).toMatch(actor);
    });
  });

  // 5. Stock is consumed asynchronously through the outbox: poll until on-hand dropped by 2.
  await asUser(browser, 'sam', async (page) => {
    await expect
      .poll(
        async () => {
          await page.goto(part.url);
          const dd = page
            .locator('dt')
            .filter({ hasText: /^On hand$/ })
            .locator('xpath=following-sibling::dd[1]');
          await dd.waitFor({ state: 'visible' });
          return Number((await dd.innerText()).trim());
        },
        { timeout: 30_000, intervals: [1_000, 2_000, 3_000], message: 'on-hand should drop by 2' },
      )
      .toBe(part.onHand - 2);
  });
});
