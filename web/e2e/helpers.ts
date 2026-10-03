import path from 'node:path';
import { expect, type Browser, type BrowserContext, type Page } from '@playwright/test';

export type UserName = 'olivia' | 'tom' | 'sam' | 'ada';

export const USERS: readonly UserName[] = ['olivia', 'tom', 'sam', 'ada'];
export const PASSWORD = 'Passw0rd!';
export const ASSET_TAG = 'SMT1-PNP-01';
export const PART_NUMBER = 'NZL-CN040';

export const authFile = (user: UserName): string => path.join(__dirname, '.auth', `${user}.json`);

/**
 * Drives the real Keycloak login form. Call it while the page sits on the Keycloak login page
 * (an anonymous visit to any guarded route redirects there).
 */
export async function submitKeycloakForm(page: Page, user: UserName): Promise<void> {
  await page.locator('#username').fill(user);
  await page.locator('#password').fill(PASSWORD);
  await page.locator('#kc-login').click();
}

/** Anonymous visit to /assets -> Keycloak -> back on /assets, signed in. */
export async function login(page: Page, user: UserName): Promise<void> {
  await page.goto('/assets');
  await page.locator('#username').waitFor({ state: 'visible', timeout: 30_000 });
  await submitKeycloakForm(page, user);
  await page.waitForURL(/localhost:8080\/assets/, { timeout: 30_000 });
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
}

export interface UserSession {
  context: BrowserContext;
  page: Page;
}

/** A separate browser context (own cookies) per person, reusing the session saved by global setup. */
export async function openAs(browser: Browser, user: UserName): Promise<UserSession> {
  const context = await browser.newContext({ storageState: authFile(user) });
  const page = await context.newPage();
  return { context, page };
}

/** Runs `fn` with a signed-in page for `user` and always closes the context. */
export async function asUser<T>(
  browser: Browser,
  user: UserName,
  fn: (page: Page) => Promise<T>,
): Promise<T> {
  const { context, page } = await openAs(browser, user);
  try {
    return await fn(page);
  } finally {
    await context.close();
  }
}

/** Raise form (Signal Forms + autocomplete). Returns the new work order's URL path. */
export async function raiseWorkOrder(page: Page, title: string): Promise<string> {
  await page.goto('/work-orders/new');
  await expect(page.getByRole('heading', { name: 'Raise work order' })).toBeVisible();

  // The asset is only accepted when picked from the autocomplete list (typing alone is invalid).
  await page.getByLabel('Asset', { exact: true }).fill(ASSET_TAG);
  await page.getByRole('option', { name: new RegExp(ASSET_TAG) }).click();

  await page.getByLabel('Title', { exact: true }).fill(title);
  await page.getByLabel('Description', { exact: true }).fill('Raised by the Playwright E2E suite.');
  await page.getByRole('button', { name: 'Raise work order' }).click();

  await page.waitForURL(/\/work-orders\/[0-9a-f-]{36}$/, { timeout: 30_000 });
  await expect(page.getByRole('heading', { level: 1 })).toContainText(title);
  return new URL(page.url()).pathname;
}

export function actionButton(page: Page, name: string) {
  return page
    .getByRole('group', { name: 'Work order actions' })
    .getByRole('button', { name, exact: true });
}

/** Opens the dialog action and confirms it. `fill` runs inside the dialog first. */
export async function confirmInDialog(
  page: Page,
  confirmName: string,
  fill?: (dialog: ReturnType<Page['getByRole']>) => Promise<void>,
): Promise<void> {
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  if (fill) await fill(dialog);
  await dialog.getByRole('button', { name: confirmName, exact: true }).click();
  await expect(dialog).toBeHidden();
}

/** Opens a part from the inventory list via search and returns its URL path and on-hand quantity. */
export async function openPart(
  page: Page,
  partNumber: string,
): Promise<{ url: string; onHand: number }> {
  await page.goto('/inventory');
  await page.getByRole('searchbox', { name: /Search part number/ }).fill(partNumber);
  await page.getByRole('link', { name: partNumber, exact: true }).click();
  await page.waitForURL(/\/inventory\/[0-9a-f-]{36}$/);
  return { url: new URL(page.url()).pathname, onHand: await readOnHand(page) };
}

export async function readOnHand(page: Page): Promise<number> {
  const dd = page
    .locator('dt')
    .filter({ hasText: /^On hand$/ })
    .locator('xpath=following-sibling::dd[1]');
  await expect(dd).toHaveText(/^\s*\d+\s*$/);
  return Number((await dd.innerText()).trim());
}
