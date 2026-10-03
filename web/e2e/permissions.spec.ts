import { expect, test } from '@playwright/test';
import { asUser, raiseWorkOrder } from './helpers';

test('operator has no Reports link and the reports API refuses her with 403', async ({
  browser,
}) => {
  await asUser(browser, 'olivia', async (page) => {
    await page.goto('/assets');
    await expect(page.getByRole('link', { name: 'Assets', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Reports', exact: true })).toHaveCount(0);

    // Hiding the link is a convenience; the API is what enforces it.
    const response = await page.request.get('/api/reports/mttr');
    expect(response.status()).toBe(403);
  });
});

test('technician cannot approve a submitted work order', async ({ browser }) => {
  const title = `E2E permissions ${Date.now()}`;
  const url = await asUser(browser, 'olivia', (page) => raiseWorkOrder(page, title));

  await asUser(browser, 'tom', async (page) => {
    await page.goto(url);
    await expect(page.getByRole('heading', { level: 1 })).toContainText(title);
    await expect(page.getByRole('heading', { name: 'History' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Approve', exact: true })).toHaveCount(0);
  });
});
