import fs from 'node:fs';
import path from 'node:path';
import { chromium, type FullConfig } from '@playwright/test';
import { USERS, authFile, login } from './helpers';

/**
 * Logs each demo user in once through the real Keycloak form and saves cookies, so specs start
 * signed in without paying (or rate-limiting) a login per test.
 */
export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0]?.use.baseURL ?? 'http://localhost:8080';
  fs.mkdirSync(path.join(__dirname, '.auth'), { recursive: true });

  const browser = await chromium.launch();
  try {
    for (const user of USERS) {
      const context = await browser.newContext({ baseURL });
      const page = await context.newPage();
      try {
        await login(page, user);
        await context.storageState({ path: authFile(user) });
      } catch (error) {
        // The first real run of the compose/Keycloak wiring: leave evidence for the artifact.
        const dir = path.join(__dirname, '..', 'test-results');
        fs.mkdirSync(dir, { recursive: true });
        await page.screenshot({ path: path.join(dir, `setup-${user}.png`), fullPage: true });
        console.error(`Login setup failed for ${user} at ${page.url()}`);
        throw error;
      } finally {
        await context.close();
      }
    }
  } finally {
    await browser.close();
  }
}
