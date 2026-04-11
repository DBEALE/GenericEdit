const { defineConfig } = require('@playwright/test');

module.exports = defineConfig({
  testDir: './scenarios',
  timeout: 120000,
  use: {
    baseURL: 'http://localhost:4300',
    headless: true,
    // Capture screenshots on failure for diagnosis
    screenshot: 'only-on-failure',
  },
  reporter: 'list',
});
