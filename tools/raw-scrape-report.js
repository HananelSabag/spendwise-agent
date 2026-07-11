/**
 * raw-scrape-report.js — DIAGNOSTIC ONLY (no mapping, no POST, no server).
 *
 * Scrapes each bank/card whose credentials exist in your local .env and writes
 * exactly what `israeli-bank-scrapers` returns — raw, as-is — to a single
 * self-contained HTML report + one JSON file per source. See fields, dates
 * (date vs processedDate), loans/installments/memo, description reliability,
 * per provider — before we transform anything.
 *
 * Run:
 *   1) Put credentials in spendwise-agent/.env (same var names as standalone):
 *        LEUMI_USERNAME / LEUMI_PASSWORD · MAX_USERNAME / MAX_PASSWORD
 *        VISA_CAL_USERNAME / VISA_CAL_PASSWORD  (etc.)
 *   2) node tools/raw-scrape-report.js       (opts: DIAG_DAYS=90, ONLY_BANK=leumi)
 *   3) Open scraped-data/raw-report.html and send it over.
 *
 * Nothing is sent anywhere — it only writes local files.
 */

import dotenv from 'dotenv';
dotenv.config({ override: true });

import fs from 'node:fs';
import path from 'node:path';

import { BANKS } from '../src/core/banks.js';
import { withBrowser, warmup } from '../src/core/browser.js';
import { scrapeBank } from '../src/core/scraper.js';
import { renderReportHtml } from '../src/core/rawReport.js';
import { DATA_DIR } from '../src/utils/paths.js';

// Credential shapes per provider (mirror of standalone's ENV_CREDS — kept local
// so this diagnostic never touches the production sync path).
const CREDS = {
  leumi:      { req: ['LEUMI_USERNAME', 'LEUMI_PASSWORD'], build: () => ({ username: process.env.LEUMI_USERNAME, password: process.env.LEUMI_PASSWORD }) },
  hapoalim:   { req: ['HAPOALIM_USER_CODE', 'HAPOALIM_PASSWORD'], build: () => ({ userCode: process.env.HAPOALIM_USER_CODE, password: process.env.HAPOALIM_PASSWORD }) },
  discount:   { req: ['DISCOUNT_ID', 'DISCOUNT_PASSWORD', 'DISCOUNT_NUM'], build: () => ({ id: process.env.DISCOUNT_ID, password: process.env.DISCOUNT_PASSWORD, num: process.env.DISCOUNT_NUM }) },
  yahav:      { req: ['YAHAV_USERNAME', 'YAHAV_PASSWORD', 'YAHAV_NATIONAL_ID'], build: () => ({ username: process.env.YAHAV_USERNAME, password: process.env.YAHAV_PASSWORD, nationalID: process.env.YAHAV_NATIONAL_ID }) },
  max:        { req: ['MAX_USERNAME', 'MAX_PASSWORD'], build: () => ({ username: process.env.MAX_USERNAME, password: process.env.MAX_PASSWORD }) },
  visa_cal:   { req: ['VISA_CAL_USERNAME', 'VISA_CAL_PASSWORD'], build: () => ({ username: process.env.VISA_CAL_USERNAME, password: process.env.VISA_CAL_PASSWORD }) },
  isracard:   { req: ['ISRACARD_ID', 'ISRACARD_CARD6', 'ISRACARD_PASSWORD'], build: () => ({ id: process.env.ISRACARD_ID, card6Digits: process.env.ISRACARD_CARD6, password: process.env.ISRACARD_PASSWORD }) },
  amex:       { req: ['AMEX_ID', 'AMEX_CARD6', 'AMEX_PASSWORD'], build: () => ({ id: process.env.AMEX_ID, card6Digits: process.env.AMEX_CARD6, password: process.env.AMEX_PASSWORD }) },
};

const has = (source) => CREDS[source] && CREDS[source].req.every((k) => (process.env[k] || '').trim() !== '');

function windowStart() {
  const days = parseInt(process.env.DIAG_DAYS || '90', 10);
  const d = new Date();
  d.setDate(d.getDate() - (Number.isFinite(days) && days > 0 ? days : 90));
  return d;
}

async function main() {
  const only = (process.env.ONLY_BANK || '').trim().toLowerCase();
  const active = Object.keys(CREDS).filter((s) => BANKS[s] && has(s) && (!only || s === only));

  if (!active.length) {
    console.log('No credentials found in .env for any known bank. Set e.g. LEUMI_USERNAME/LEUMI_PASSWORD and retry.');
    return;
  }
  console.log(`Diagnostic scrape (RAW, no POST) for: ${active.join(', ')}`);
  fs.mkdirSync(DATA_DIR, { recursive: true });

  const results = [];
  await withBrowser(async (browser) => {
    for (const source of active) {
      try {
        console.log(`→ scraping ${source} …`);
        if (BANKS[source].warmupUrl) await warmup(browser, BANKS[source].warmupUrl, source);
        const accounts = await scrapeBank(source, CREDS[source].build(), browser, windowStart());
        results.push({ source, accounts });
        fs.writeFileSync(path.join(DATA_DIR, `raw-${source}.json`), JSON.stringify(accounts, null, 2), 'utf8');
        console.log(`   ✓ ${source}: ${accounts.length} account(s)`);
      } catch (err) {
        results.push({ source, error: err.stack || err.message });
        console.log(`   ✗ ${source}: ${err.message}`);
      }
    }
  });

  const htmlPath = path.join(DATA_DIR, 'raw-report.html');
  fs.writeFileSync(htmlPath, renderReportHtml(results, { days: process.env.DIAG_DAYS || 90 }), 'utf8');
  console.log(`\nReport written: ${htmlPath}`);
  console.log('Open it and send it over — plus the raw-*.json files if you want.');
}

main().catch((err) => { console.error(err); process.exitCode = 1; });
