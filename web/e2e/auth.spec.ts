import { expect, test } from '@playwright/test';
import { login } from './helpers';

// Fresh, anonymous context: this spec must not reuse the saved sessions.
test.use({ storageState: { cookies: [], origins: [] } });

test('anonymous visit is sent to Keycloak, sign-in lands back on /assets, sign-out is anonymous', async ({
  page,
}) => {
  await page.goto('/assets');
  await expect(page.locator('#username')).toBeVisible({ timeout: 30_000 });
  expect(new URL(page.url()).port).toBe('8081');

  await login(page, 'sam');
  await expect(page).toHaveURL(/\/assets/);
  // Toolbar shows "<display name> · <roles>"; the demo user is a supervisor.
  await expect(page.getByText(/sam/i).first()).toBeVisible();
  await expect(page.getByText(/supervisor/)).toBeVisible();
  expect((await page.request.get('/api/identity/me')).status()).toBe(200);

  await page.getByRole('button', { name: 'Sign out' }).click();

  // Keycloak may ask to confirm the logout when no id_token_hint is sent.
  const confirm = page.locator('#kc-logout');
  await Promise.race([
    confirm.waitFor({ state: 'visible', timeout: 15_000 }).then(() => confirm.click()),
    page.getByRole('button', { name: 'Sign in' }).waitFor({ state: 'visible', timeout: 15_000 }),
  ]).catch(() => undefined);

  await page.goto('/status');
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole('button', { name: 'Sign out' })).toHaveCount(0);

  const me = await page.request.get('/api/identity/me');
  expect(me.status()).toBe(401);
});
