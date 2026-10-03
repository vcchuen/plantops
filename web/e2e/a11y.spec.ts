import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { asUser, type UserName } from './helpers';

interface Target {
  name: string;
  /** Navigates to the page and resolves once its content (not the loading bar) is on screen. */
  open: (page: Page) => Promise<void>;
}

const targets: Target[] = [
  {
    name: 'asset list',
    open: async (page) => {
      await page.goto('/assets');
      await expect(page.getByRole('table', { name: 'Assets' })).toBeVisible();
    },
  },
  {
    name: 'work order detail',
    open: async (page) => {
      await page.goto('/work-orders');
      await page.getByRole('table', { name: 'Work orders' }).getByRole('link').first().click();
      await expect(page.getByRole('heading', { name: 'History' })).toBeVisible();
    },
  },
  {
    name: 'raise work order form',
    open: async (page) => {
      await page.goto('/work-orders/new');
      await expect(page.getByRole('heading', { name: 'Raise work order' })).toBeVisible();
    },
  },
  {
    name: 'inventory',
    open: async (page) => {
      await page.goto('/inventory');
      await expect(page.getByRole('table', { name: 'Parts' })).toBeVisible();
    },
  },
  {
    name: 'reports',
    open: async (page) => {
      await page.goto('/reports');
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await page.waitForLoadState('networkidle');
    },
  },
];

const user: UserName = 'sam';

for (const target of targets) {
  test(`a11y: ${target.name} has no serious or critical WCAG 2.1 A/AA violations`, async ({
    browser,
  }, testInfo) => {
    await asUser(browser, user, async (page) => {
      await target.open(page);

      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
        .analyze();

      await testInfo.attach(`axe-${target.name.replace(/\s+/g, '-')}.json`, {
        body: JSON.stringify(results, null, 2),
        contentType: 'application/json',
      });

      const blocking = results.violations.filter(
        (v) => v.impact === 'serious' || v.impact === 'critical',
      );
      expect(
        blocking.map((v) => ({
          rule: v.id,
          impact: v.impact,
          help: v.help,
          nodes: v.nodes.slice(0, 3).map((n) => n.target.join(' ')),
        })),
        'serious/critical axe violations',
      ).toEqual([]);
    });
  });
}
