import fs from 'node:fs';
import path from 'node:path';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { ASSET_TAG, actionButton, authFile, confirmInDialog, type UserName } from './helpers';

/**
 * README media capture (screenshots + a short recorded journey). Never part of the normal run:
 * only executes when CAPTURE_MEDIA=1 (see the capture steps in .github/workflows/e2e.yml).
 */
test.skip(process.env['CAPTURE_MEDIA'] !== '1', 'README media capture: set CAPTURE_MEDIA=1');

const VIEWPORT = { width: 1280, height: 800 } as const;
const MEDIA_DIR = path.join(__dirname, 'media');
const VIDEO_DIR = path.join(MEDIA_DIR, 'video');

test.describe.configure({ mode: 'serial' });

async function open(browser: Browser, user: UserName, record: boolean): Promise<BrowserContext> {
  return browser.newContext({
    storageState: authFile(user),
    viewport: VIEWPORT,
    deviceScaleFactor: 1,
    ...(record ? { recordVideo: { dir: VIDEO_DIR, size: VIEWPORT } } : {}),
  });
}

/** Fonts loaded and no progress bar showing, then a full-viewport PNG with a stable name. */
async function shoot(page: Page, name: string): Promise<void> {
  await page.evaluate(() => document.fonts.ready);
  await expect(page.getByRole('progressbar')).toHaveCount(0);
  await page.screenshot({ path: path.join(MEDIA_DIR, name), fullPage: false });
}

/** Fills the raise form for SMT1-PNP-01 without submitting. */
async function fillRaiseForm(page: Page, title: string, description: string): Promise<void> {
  await page.goto('/work-orders/new');
  await expect(page.getByRole('heading', { name: 'Raise work order' })).toBeVisible();
  await page.getByRole('combobox', { name: 'Asset', exact: true }).fill(ASSET_TAG);
  await page.getByRole('option', { name: new RegExp(ASSET_TAG) }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);
  await page.getByLabel('Description', { exact: true }).fill(description);
}

test('screenshots', async ({ browser }) => {
  test.setTimeout(180_000);
  fs.mkdirSync(MEDIA_DIR, { recursive: true });

  // Supervisor views.
  const sam = await open(browser, 'sam', false);
  try {
    const page = await sam.newPage();

    await page.goto('/assets');
    await expect(page.getByRole('table', { name: 'Assets' })).toBeVisible();
    await shoot(page, '01-assets.png');

    // Every work order has at least its "Submitted" entry; open the first one in the list.
    await page.goto('/work-orders');
    const firstOrder = page.locator('a[href^="/work-orders/"]:not([href="/work-orders/new"])').first();
    await expect(firstOrder).toBeVisible();
    await firstOrder.click();
    await page.waitForURL(/\/work-orders\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { name: /History/ })).toBeVisible();
    await expect(page.locator('ol.timeline > li').first()).toBeVisible();
    await shoot(page, '02-work-order.png');

    await page.goto('/inventory');
    await expect(page.getByRole('searchbox', { name: /Search part number/ })).toBeVisible();
    await expect(page.getByRole('link').filter({ hasText: /^[A-Z]{2,}-/ }).first()).toBeVisible();
    await shoot(page, '04-inventory.png');

    await page.goto('/pm-schedules');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.getByRole('table').first()).toBeVisible();
    await shoot(page, '05-pm-schedules.png');

    await page.goto('/reports');
    await page.waitForLoadState('networkidle');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    // Three report cards; tolerate a different card element rather than failing the whole capture.
    await page
      .locator('mat-card')
      .nth(2)
      .waitFor({ state: 'visible', timeout: 10_000 })
      .catch(() => undefined);
    await shoot(page, '06-reports.png');
  } finally {
    await sam.close();
  }

  // Operator: the raise form, partly filled, not submitted.
  const olivia = await open(browser, 'olivia', false);
  try {
    const page = await olivia.newPage();
    await fillRaiseForm(
      page,
      'Nozzle 3 clogging on SMT1 pick-and-place',
      'Repeated pick errors on head 2 since the night shift; line slowed to half speed.',
    );
    await shoot(page, '03-raise-form.png');
  } finally {
    await olivia.close();
  }
});

test('recorded journey', async ({ browser }) => {
  test.setTimeout(180_000);
  fs.mkdirSync(VIDEO_DIR, { recursive: true });
  const pause = (page: Page) => page.waitForTimeout(600);
  const videos: string[] = [];

  // Journey 1: olivia raises a work order.
  let workOrderUrl = '';
  const oliviaCtx = await open(browser, 'olivia', true);
  try {
    const page = await oliviaCtx.newPage();
    const video = page.video();
    await fillRaiseForm(
      page,
      `Nozzle 3 clogging ${Date.now()}`,
      'Repeated pick errors on head 2; line slowed to half speed.',
    );
    await pause(page);
    await page.getByRole('button', { name: 'Raise work order' }).click();
    await page.waitForURL(/\/work-orders\/[0-9a-f-]{36}$/, { timeout: 30_000 });
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    workOrderUrl = new URL(page.url()).pathname;
    await pause(page);
    await pause(page);
    if (video) videos.push(await video.path());
  } finally {
    await oliviaCtx.close();
  }

  // Journey 2: sam approves and assigns tom.
  const samCtx = await open(browser, 'sam', true);
  try {
    const page = await samCtx.newPage();
    const video = page.video();
    await page.goto(workOrderUrl);
    await expect(actionButton(page, 'Approve')).toBeVisible();
    await pause(page);
    await actionButton(page, 'Approve').click();
    await confirmInDialog(page, 'Approve');
    await pause(page);

    await expect(actionButton(page, 'Assign')).toBeVisible();
    await actionButton(page, 'Assign').click();
    await confirmInDialog(page, 'Assign', async (dialog) => {
      await dialog.getByRole('combobox', { name: 'Technician' }).click();
      await pause(page);
      await page.getByRole('option', { name: /tom/i }).click();
      await pause(page);
    });
    await expect(page.getByText('Tom', { exact: false }).first()).toBeVisible();
    await pause(page);
    await pause(page);
    if (video) videos.push(await video.path());
  } finally {
    await samCtx.close();
  }

  // Videos are flushed on context close; give them stable names for the ffmpeg step.
  expect(videos).toHaveLength(2);
  videos.forEach((from, i) => {
    const to = path.join(VIDEO_DIR, `journey-${i + 1}.webm`);
    fs.rmSync(to, { force: true });
    fs.renameSync(from, to);
  });
});
