import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./visual",
  fullyParallel: true,
  workers: 2,
  reporter: "list",
  expect: { toHaveScreenshot: { maxDiffPixelRatio: 0.03, threshold: 0.3 } },
  use: {
    baseURL: "http://127.0.0.1:4173",
    browserName: "chromium",
    colorScheme: "light",
    deviceScaleFactor: 1,
    locale: "en-US",
    reducedMotion: "reduce",
    timezoneId: "UTC",
  },
  webServer: {
    command: "npm run dev -- --host 127.0.0.1 --port 4173 --strictPort",
    url: "http://127.0.0.1:4173",
    reuseExistingServer: true,
  },
});
