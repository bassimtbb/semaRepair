import { defineConfig, devices } from '@playwright/test'
import path from 'path'

const FRONTEND_DIR = path.resolve(__dirname, '../frontend')

export default defineConfig({
  testDir: './tests',
  timeout: 120_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: 0,

  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
    ['json', { outputFile: 'test-results/results.json' }],
  ],

  use: {
    baseURL: 'http://localhost:5173',
    headless: true,
    viewport: { width: 1280, height: 800 },
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'retain-on-failure',
  },

  // Auto-start the Vite dev server before running tests.
  // If it's already running (e.g. you started it manually), this is skipped.
  webServer: {
    command: 'npm run dev',
    cwd: FRONTEND_DIR,
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 60_000,
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
