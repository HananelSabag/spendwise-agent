/**
 * standalone.js — development / fallback mode
 *
 * Scrapes banks with credentials from the local .env and POSTs to the
 * legacy /bank-sync endpoint (X-API-Key). The production path is agent.js.
 *
 * Dev shortcut: OFFLINE=1 npm run standalone
 *   replays the last saved scrape from scraped-data/ — no browser, no bank.
 */

import dotenv from 'dotenv';
dotenv.config({ override: true });

import { log, rotateIfNeeded } from './utils/log.js';
import { BANKS } from './core/banks.js';
import { withBrowser, warmup } from './core/browser.js';
import { scrapeBank, mapAccounts } from './core/scraper.js';
import { saveScrape, loadScrape } from './core/cache.js';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Env-var credential mapping (standalone mode only)
const ENV_CREDS = {
  yahav: {
    required: ['YAHAV_USERNAME', 'YAHAV_PASSWORD', 'YAHAV_NATIONAL_ID'],
    build: () => ({
      username: process.env.YAHAV_USERNAME,
      password: process.env.YAHAV_PASSWORD,
      nationalID: process.env.YAHAV_NATIONAL_ID,
    }),
  },
  hapoalim: {
    required: ['HAPOALIM_USER_CODE', 'HAPOALIM_PASSWORD'],
    build: () => ({
      userCode: process.env.HAPOALIM_USER_CODE,
      password: process.env.HAPOALIM_PASSWORD,
    }),
  },
  leumi: {
    required: ['LEUMI_USERNAME', 'LEUMI_PASSWORD'],
    build: () => ({
      username: process.env.LEUMI_USERNAME,
      password: process.env.LEUMI_PASSWORD,
    }),
  },
  mizrahi: {
    required: ['MIZRAHI_USERNAME', 'MIZRAHI_PASSWORD'],
    build: () => ({
      username: process.env.MIZRAHI_USERNAME,
      password: process.env.MIZRAHI_PASSWORD,
    }),
  },
  discount: {
    required: ['DISCOUNT_ID', 'DISCOUNT_PASSWORD', 'DISCOUNT_NUM'],
    build: () => ({
      id: process.env.DISCOUNT_ID,
      password: process.env.DISCOUNT_PASSWORD,
      num: process.env.DISCOUNT_NUM,
    }),
  },
  mercantile: {
    required: ['MERCANTILE_ID', 'MERCANTILE_PASSWORD', 'MERCANTILE_NUM'],
    build: () => ({
      id: process.env.MERCANTILE_ID,
      password: process.env.MERCANTILE_PASSWORD,
      num: process.env.MERCANTILE_NUM,
    }),
  },
  otsar_hahayal: {
    required: ['OTSAR_USERNAME', 'OTSAR_PASSWORD'],
    build: () => ({
      username: process.env.OTSAR_USERNAME,
      password: process.env.OTSAR_PASSWORD,
    }),
  },
  beinleumi: {
    required: ['BEINLEUMI_USERNAME', 'BEINLEUMI_PASSWORD'],
    build: () => ({
      username: process.env.BEINLEUMI_USERNAME,
      password: process.env.BEINLEUMI_PASSWORD,
    }),
  },
  massad: {
    required: ['MASSAD_USERNAME', 'MASSAD_PASSWORD'],
    build: () => ({
      username: process.env.MASSAD_USERNAME,
      password: process.env.MASSAD_PASSWORD,
    }),
  },
  pagi: {
    required: ['PAGI_USERNAME', 'PAGI_PASSWORD'],
    build: () => ({
      username: process.env.PAGI_USERNAME,
      password: process.env.PAGI_PASSWORD,
    }),
  },
  isracard: {
    required: ['ISRACARD_ID', 'ISRACARD_CARD6', 'ISRACARD_PASSWORD'],
    build: () => ({
      id: process.env.ISRACARD_ID,
      card6Digits: process.env.ISRACARD_CARD6,
      password: process.env.ISRACARD_PASSWORD,
    }),
  },
  amex: {
    required: ['AMEX_ID', 'AMEX_CARD6', 'AMEX_PASSWORD'],
    build: () => ({
      id: process.env.AMEX_ID,
      card6Digits: process.env.AMEX_CARD6,
      password: process.env.AMEX_PASSWORD,
    }),
  },
  visa_cal: {
    required: ['VISA_CAL_USERNAME', 'VISA_CAL_PASSWORD'],
    build: () => ({
      username: process.env.VISA_CAL_USERNAME,
      password: process.env.VISA_CAL_PASSWORD,
    }),
  },
  max: {
    required: ['MAX_USERNAME', 'MAX_PASSWORD'],
    build: () => ({
      username: process.env.MAX_USERNAME,
      password: process.env.MAX_PASSWORD,
    }),
  },
};

function hasCredentials(source) {
  return ENV_CREDS[source].required.every((k) => (process.env[k] || '').trim() !== '');
}

async function postLegacy(source, accounts) {
  const apiUrl = (process.env.API_URL || '').replace(/\/+$/, '');
  const key = process.env.BANK_SYNC_API_KEY;
  const householdId = Number(process.env.HOUSEHOLD_ID);
  if (!apiUrl || !key || !householdId) {
    throw new Error('API_URL, BANK_SYNC_API_KEY and HOUSEHOLD_ID must be set for standalone mode');
  }

  let filtered = accounts;
  if (source === 'yahav' && process.env.YAHAV_ACCOUNT_NUMBER) {
    const target = process.env.YAHAV_ACCOUNT_NUMBER.trim();
    const matches = accounts.filter((a) => String(a.account_number || '').trim() === target);
    if (matches.length > 0) filtered = matches;
  }

  const res = await fetch(`${apiUrl}/bank-sync`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-API-Key': key },
    body: JSON.stringify({ household_id: householdId, source, accounts: filtered }),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`API ${res.status}: ${text.slice(0, 300)}`);
  try { return JSON.parse(text); } catch { return {}; }
}

async function main() {
  rotateIfNeeded();
  log.info('=== standalone sync started ===');

  const only = (process.env.ONLY_BANK || '').trim().toLowerCase();
  const active = Object.keys(BANKS)
    .filter(hasCredentials)
    .filter((s) => !only || s === only);
  const isOffline = process.env.OFFLINE === '1';

  if (active.length === 0) {
    log.info('no banks configured in .env; nothing to do');
    return;
  }
  if (isOffline) log.info('OFFLINE=1 — replaying cached data, no browser');

  const runBank = async (source, browser) => {
    let raw;
    if (isOffline) {
      raw = loadScrape(source);
    } else {
      if (BANKS[source].warmupUrl) await warmup(browser, BANKS[source].warmupUrl, source);
      raw = await scrapeBank(source, ENV_CREDS[source].build(), browser);
      saveScrape(source, raw);
    }
    const accounts = mapAccounts(source, raw);
    const result = await postLegacy(source, accounts);
    log.info(`[${source}] SUCCESS — ${result.inserted ?? '?'} new, ${result.skipped ?? '?'} skipped`);
  };

  let failures = 0;
  const runAll = async (browser) => {
    let first = true;
    for (const source of active) {
      try {
        if (!first && !isOffline) await sleep(2 * 60_000 + Math.random() * 3 * 60_000);
        first = false;
        await runBank(source, browser);
      } catch (err) {
        failures += 1;
        log.error(`[${source}] FAILED — ${err.message}`);
      }
    }
  };

  if (isOffline) await runAll(null);
  else await withBrowser(runAll);

  log.info('=== standalone sync finished ===');
  if (failures > 0) process.exitCode = 2;
}

main().catch((err) => {
  log.error(`FATAL: ${err.stack || err.message}`);
  process.exit(1);
});
