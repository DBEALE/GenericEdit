// Scenario: Open FX_RATES, select "Last 3 years" preset, click Search.
// Measures blob operations triggered by loading the header list.

const { test, expect } = require('@playwright/test');
const { readEntriesSince, summarise, waitUntilIdle } = require('../blob-log');

const USER_ID = 'Admin';
const ROLES   = 'FXReader,FXWriter,FXApprover,FXAdmin';

test('load FX_RATES headers - last 3 years', async ({ page }) => {
  // ── Load app and set user via localStorage ──────────────────────────────
  await page.goto('/');
  await page.evaluate(({ userId, roles }) => {
    localStorage.setItem('userId', userId);
    localStorage.setItem('roles', roles);
  }, { userId: USER_ID, roles: ROLES });

  await page.goto('/');
  // Wait for schema list to appear — click the FxRates entry in the sidebar list
  const fxRatesItem = page.locator('.dataset-list button, .schema-list button, li button').filter({ hasText: 'FxRates' }).first();
  await fxRatesItem.waitFor({ timeout: 15000 });

  // ── Select FX_RATES dataset ─────────────────────────────────────────────
  await fxRatesItem.click();
  // Wait for the saved-headers section to be visible
  await page.locator('.saved-headers-actions').waitFor({ timeout: 10000 });

  // ── Apply "Last 3 years" preset (without auto-search) ──────────────────
  await page.locator('select').filter({ hasText: 'Last' }).first().selectOption('Last 3 years');

  // ── Mark time then click Search ─────────────────────────────────────────
  const searchStart = new Date();

  // Register the response waiter BEFORE clicking so we don't miss it
  const headersResponsePromise = page.waitForResponse(
    (resp) => {
      if (!resp.url().includes('/datasets/FX_RATES/headers')) return false;
      if (resp.status() !== 200) return false;
      // Only accept a response that arrived after we clicked Search
      return new Date() >= searchStart;
    },
    { timeout: 90000 }
  );

  await page.getByRole('button', { name: 'Search' }).click();

  // Wait until the blob log stops being written to (backend finished processing)
  await waitUntilIdle(1500, 90000);

  // ── Read blob log ────────────────────────────────────────────────────────
  const entries = readEntriesSince(searchStart);
  const report  = summarise(entries);

  console.log('\n═══ BlobStore activity: load FX_RATES headers (last 3 years) ═══');
  console.log(`  searchStart (UTC): ${searchStart.toISOString()}`);
  console.log(report);
  console.log('═══════════════════════════════════════════════════════════════\n');

  const rows = page.locator('table.instance-header-grid tr, .instance-header-grid tr').filter({ hasNot: page.locator('th') });
  const rowCount = await rows.count();
  console.log(`  Rows visible in UI: ${rowCount}`);

  expect(entries.length).toBeGreaterThan(0);
}, { timeout: 120000 });
