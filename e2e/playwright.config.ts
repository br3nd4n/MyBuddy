import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  expect: { timeout: 5000 },
  retries: 0,
  workers: 2,
  use: {
    headless: true,
    actionTimeout: 0,
    navigationTimeout: 30_000,
  },
  reporter: [['list'], ['html', { open: 'never' }]],
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' } }
  ],
});