import path from 'node:path';
import { defineConfig, devices } from '@playwright/test';

// Resolved from this file, not the cwd, so `npm run e2e` (cwd = web/) and CI artifact paths agree.
const webRoot = path.resolve(__dirname, '..');

export default defineConfig({
  testDir: __dirname,
  testMatch: '**/*.spec.ts',
  globalSetup: path.join(__dirname, 'global-setup.ts'),
  outputDir: path.join(webRoot, 'test-results'),
  // Journeys share one database and the lifecycle test is a long serial story; keep it simple.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [
    ['list'],
    ['html', { open: 'never', outputFolder: path.join(webRoot, 'playwright-report') }],
  ],
  use: {
    baseURL: 'http://localhost:8080',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
