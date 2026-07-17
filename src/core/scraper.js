/**
 * Scraping engine — wraps israeli-bank-scrapers with retry, account
 * mapping, and strict parsing of what comes back.
 */

import path from 'node:path';
import { createScraper } from 'israeli-bank-scrapers';
import { ROOT_DIR } from '../utils/paths.js';
import { logger } from '../utils/log.js';
import { assertKnownBank } from './banks.js';
import { rawDebugEnabled, writeRawScrape } from './rawReport.js';

const RETRYABLE = new Set(['TIMEOUT', 'GENERIC', 'GENERAL_ERROR']);
const TERMINAL_FAILURES = {
  INVALID_PASSWORD: 'AUTH_INVALID',
  CHANGE_PASSWORD: 'PASSWORD_CHANGE_REQUIRED',
  ACCOUNT_BLOCKED: 'ACCOUNT_BLOCKED',
  TWO_FACTOR_RETRIEVER_MISSING: 'MFA_REQUIRED',
};

const CREDENTIAL_ERROR_TEXT = /invalid\s+(?:password|credentials?|user(?:name)?|login)|incorrect\s+(?:password|credentials?|user(?:name)?)|wrong\s+(?:password|credentials?|user(?:name)?)|login\s+failed|פרטי\s+ה(?:הזדהות|תחברות).*שגוי|שם\s+משתמש.*שגוי|סיסמ(?:ה|א).*שגוי/i;

export class ScrapeFailure extends Error {
  constructor(message, { code = 'SCRAPER_ERROR', terminal = false, scraperErrorType = null } = {}) {
    super(message);
    this.name = 'ScrapeFailure';
    this.code = code;
    this.terminal = terminal;
    this.scraperErrorType = scraperErrorType;
  }
}

export function classifyScrapeFailure(result = {}) {
  const errorType = String(result.errorType || '').toUpperCase();
  const errorMessage = String(result.errorMessage || 'unknown error');
  const terminalCode = TERMINAL_FAILURES[errorType]
    || (CREDENTIAL_ERROR_TEXT.test(errorMessage) ? 'AUTH_INVALID' : null);

  return {
    code: terminalCode || (errorType === 'TIMEOUT' ? 'SCRAPER_TIMEOUT' : 'SCRAPER_ERROR'),
    terminal: Boolean(terminalCode),
    retryable: !terminalCode && RETRYABLE.has(errorType),
    errorType: errorType || 'SCRAPE_ERROR',
    message: `${errorType || 'ScrapeError'}: ${errorMessage}`,
  };
}

export async function runScrapeAttempts(attempt, {
  retryDelayMs = 30_000,
  sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  onRetry = () => {},
} = {}) {
  let result = await attempt();
  let failure = result.success ? null : classifyScrapeFailure(result);
  if (failure?.retryable) {
    onRetry(failure);
    await sleep(retryDelayMs);
    result = await attempt();
    failure = result.success ? null : classifyScrapeFailure(result);
  }

  if (failure) {
    throw new ScrapeFailure(failure.message, {
      code: failure.code,
      terminal: failure.terminal,
      scraperErrorType: failure.errorType,
    });
  }
  return result;
}

// Hard wall-clock cap per scrape attempt. israeli-bank-scrapers takes a
// `timeout` option but does NOT guarantee scrape() settles within it — a stuck
// page or Cloudflare loop can hang indefinitely. Without this, one hung bank
// (e.g. the 2nd job in a run) would never report back, leaving the connection
// "syncing" forever on the client and blocking further syncs. Env-overridable.
const HARD_TIMEOUT_MS = parseInt(process.env.SCRAPE_HARD_TIMEOUT_MS, 10) || 150000;

function withTimeout(promise, ms, label) {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error(`${label} exceeded ${Math.round(ms / 1000)}s hard timeout`)), ms);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
}

/**
 * How far back to scrape. Three months keeps the previous calendar month
 * complete even when a user connects halfway through the current month.
 * BACKFILL_MONTHS can still override this for an explicit diagnostic run.
 */
export function scrapeWindowStart() {
  const d = new Date();
  const monthsBack = parseInt(process.env.BACKFILL_MONTHS || '3', 10);
  d.setMonth(d.getMonth() - (Number.isFinite(monthsBack) && monthsBack > 0 ? monthsBack : 3));
  return d;
}

/**
 * Scrape one bank. Retries ONCE on transient errors (timeout/generic) —
 * never on credential errors, which would look like a brute-force attempt
 * to the bank and risk locking the account.
 */
export async function scrapeBank(source, credentials, browser, fromDate = scrapeWindowStart(), rawScope = '') {
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
    // Guard the scrape with a hard wall-clock timeout. If the library hangs
    // past it, surface a retryable TIMEOUT result (instead of hanging forever)
    // so the retry-once path runs and, failing that, the job reports a failure.
    const scrapePromise = scraper.scrape(credentials);
    scrapePromise.catch(() => {}); // it may settle after we've timed out — don't crash
    try {
      return await withTimeout(scrapePromise, HARD_TIMEOUT_MS, `${source} scrape`);
    } catch (err) {
      return { success: false, errorType: 'TIMEOUT', errorMessage: err.message };
    }
  };

  const result = await runScrapeAttempts(attempt, {
    onRetry: (failure) => log.warn(`transient failure (${failure.errorType}) — retrying once in 30s`),
  });

  const accounts = result.accounts || [];
  // Debug: when raw export is toggled on (worker button / env / flag file),
  // drop exactly what the scraper returned before we map anything.
  if (rawDebugEnabled()) {
    try {
      const file = writeRawScrape(source, accounts, rawScope);
      log.info(`RAW debug report → ${file}`);
    } catch (e) {
      log.warn(`raw report write failed: ${e.message}`);
    }
  }
  return accounts;
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
      const chargedAmount = Number(txn.chargedAmount);
      const originalAmount = Number(txn.originalAmount);
      const originalCurrency = String(txn.originalCurrency ?? '').trim();
      const status = txn.status === 'pending' || txn.status === 'completed'
        ? txn.status
        : null;
      // MAX pending authorizations and some completed CAL rows arrive with
      // chargedAmount=0 while originalAmount carries the real ILS amount.
      // Preserve that provider fact; never apply this fallback to foreign FX.
      const providerIlsFallback = chargedAmount === 0
        && Number.isFinite(originalAmount)
        && originalAmount !== 0
        && ['ILS', 'NIS', '₪'].includes(originalCurrency.toUpperCase());
      const amount = providerIlsFallback ? originalAmount : chargedAmount;
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
      // The provider memo often carries the useful human detail that the
      // short description omits (salary marker, transfer recipient, charge
      // explanation). Preserve it as bank-provided notes; the server keeps
      // this separate from the transaction identity used for dedup.
      const memo = String(txn.memo ?? '').trim();
      mapped.notes = memo || '';
      if (Number.isFinite(originalAmount)) mapped.original_amount = originalAmount;
      if (originalCurrency) mapped.original_currency = originalCurrency;
      const chargedCurrency = String(txn.chargedCurrency ?? '').trim();
      if (chargedCurrency) mapped.charged_currency = chargedCurrency;
      const txnKind = String(txn.type ?? '').trim();
      if (txnKind) mapped.txn_kind = txnKind;

      // Some providers expose structured installment metadata; others put the
      // same fact only in the memo (e.g. "תשלום 5 מתוך 10"). Preserve either
      // representation so the server never has to infer it from an amount/date.
      const structured = txn.installments && typeof txn.installments === 'object'
        ? txn.installments
        : null;
      const memoInstallment = memo.match(/תשלום\s+(\d+)\s+מתוך\s+(\d+)/);
      const installmentNumber = Number(
        structured?.number ?? structured?.current ?? memoInstallment?.[1],
      );
      const installmentTotal = Number(
        structured?.total ?? structured?.count ?? memoInstallment?.[2],
      );
      if (Number.isInteger(installmentNumber) && installmentNumber > 0) {
        mapped.installment_number = installmentNumber;
      }
      if (Number.isInteger(installmentTotal) && installmentTotal > 0) {
        mapped.installment_total = installmentTotal;
      }
      // Credit companies expose two different dates: `date` is when the
      // purchase happened, while `processedDate` is when CAL/Max/Isracard
      // actually debit the card statement. SpendWise needs both: purchase
      // date drives spending analytics; processed date makes statement totals
      // line up on cycle-day boundaries. Older/partial scraper responses may
      // omit it, so only send a validated value.
      if (txn.processedDate) {
        const processedDate = new Date(txn.processedDate);
        if (!isNaN(processedDate.getTime())) {
          mapped.processed_date = processedDate.toISOString();
        }
      }
      if (status) mapped.status = status;
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
