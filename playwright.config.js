import { defineConfig } from "@playwright/test";

const liveBaseURL = process.env.PLAYWRIGHT_BASE_URL;

export default defineConfig({
  testDir: "./tests/e2e",
  timeout: 90_000,
  outputDir: "./test-results",
  reporter: [["html", { outputFolder: "playwright-report", open: "never" }]],
  use: {
    baseURL: liveBaseURL ?? "http://127.0.0.1:8791",
  },
  webServer: liveBaseURL ? undefined : {
    command: "./node_modules/.bin/cf dev -- --port 8791",
    url: "http://127.0.0.1:8791",
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
