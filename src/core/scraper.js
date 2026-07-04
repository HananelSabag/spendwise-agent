/**
 * Scraping engine — wraps israeli-bank-scrapers with retry, account
 * mapping, and strict parsing of what comes back.
 */

import path from 'node:path';
import { createScraper } from 'israeli-bank-scrapers';
import { ROOT_DIR } from '../utils/paths.js';
import { logger } from '../utils/log.js';
import { assertKnownBank } from './banks.js';

const RETRYABLE = ['TIMEOUT', 'GENERIC', 'GENERAL_ERROR'];

/** How far back to scrape. BACKFILL_MONTHS overrides for one-off backfills. */
export function scrapeWindowStart() {
  const d = new Date();
  const monthsBack = parseInt(process.env.BACKFILL_MONTHS || '1', 10);
  d.setMonth(d.getMonth() - (Number.isFinite(monthsBack) && monthsBack > 0 ? monthsBack : 1));
  return d;
}

/**
 * Scrape one bank. Retries ONCE on transient errors (timeout/generic) —
 * never on credential errors, which would look like a brute-force attempt
 * to the bank and risk locking the account.
 */
export async function scrapeBank(source, credentials, browser, fromDate = scrapeWindowStart()) {
  const meta = assertKnownBank(source);
  const log = logger(source);

  const attempt = async () => {
    const scraper = createScraper({
      companyId: meta.companyId,
      startDate: fromDate,
      combineInstallments: false,
      browser,
      skipCloseBrowser: true, // withBrowser() owns the lifecycle
      timeout: 120000,
      storeFailureScreenShotPath: path.join(ROOT_DIR, `fail-${source}.png`),
    });
    return scraper.scrape(credentials);
  };

  let result = await attempt();
  if (!result.success && RETRYABLE.includes(result.errorType)) {
    log.warn(`transient failure (${result.errorType}) — retrying once in 30s`);
    await new Promise((r) => setTimeout(r, 30_000));
    result = await attempt();
  }

  if (!result.success) {
    throw new Error(`${result.errorType || 'ScrapeError'}: ${result.errorMessage || 'unknown error'}`);
  }
  return result.accounts || [];
}

/**
 * Map raw scraper accounts to the SpendWise API payload shape.
 * Strict parsing: drops malformed transactions instead of sending garbage,
 * and reports how many were dropped so data problems are visible.
 */
export function mapAccounts(source, rawAccounts) {
  const log = logger(source);
  let dropped = 0;

  const accounts = rawAccounts.map((account) => {
    const txns = (account.txns || []).flatMap((txn) => {
      const amount = Number(txn.chargedAmount);
      const date = txn.date ? new Date(txn.date) : null;
      // A transaction without a finite amount or valid date is garbage —
      // dropping it here beats corrupting the ledger downstream.
      if (!Number.isFinite(amount) || !date || isNaN(date.getTime())) {
        dropped += 1;
        return [];
      }
      const mapped = {
        date: date.toISOString(),
        description: String(txn.description || '').trim(),
        charged_amount: amount,
      };
      if (txn.identifier !== undefined && txn.identifier !== null) {
        mapped.identifier = String(txn.identifier);
      }
      // Source-provided category text (e.g. Max supplies `category`). This is
      // the only category signal in the new model — the server stores it as
      // transactions.raw_category. Blank/undefined → null (never invent one).
      const rawCategory = String(txn.category ?? '').trim();
      mapped.raw_category = rawCategory || null;
      return [mapped];
    });

    return {
      account_number: account.accountNumber,
      type: account.type || source,
      // null = balance not provided by the bank/library; 0 is a real zero.
      balance: typeof account.balance === 'number' && Number.isFinite(account.balance)
        ? account.balance
        : null,
      txns,
    };
  });

  if (dropped > 0) log.warn(`dropped ${dropped} malformed transaction(s) during mapping`);
  return accounts;
}
