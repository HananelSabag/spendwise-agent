/**
 * spendwise-agent — Bank Connect sync agent
 *
 * The production entry point. Runs on a trusted residential machine
 * (Task Scheduler every 30 min) and exits immediately when idle:
 *
 *   1. Claim pending sync jobs from SpendWise (X-Agent-Key, HTTPS out only)
 *   2. Decrypt each job's credential envelope with the local private key
 *      (in memory only — nothing sensitive ever touches disk)
 *   3. Local 3h cooldown per connection (bank lockout protection)
 *   4. Scrape via the shared engine (retry-once on transient errors)
 *   5. Report accounts (or the failure) back to the server
 *
 * .env needs API_URL always, plus either BANK_AGENT_KEY (Default Host) or a
 * completed pairing (see pairing.js — writes agent-private.key + the device
 * token this reads via api/client.js). Exit codes: 1 = real misconfiguration
 * (fix and retry), 2 = "not paired yet" (expected before first pairing, not
 * an error the caller should alarm on).
 */

import dotenv from 'dotenv';
dotenv.config({ override: true });

import fs from 'node:fs';
import { PRIVATE_KEY_FILE, DEVICE_TOKEN_FILE } from './utils/paths.js';
import { log, rotateIfNeeded, logger } from './utils/log.js';
import { inCooldown, markScraped, acquireLock, releaseLock, COOLDOWN_HOURS } from './utils/state.js';
import { open as openEnvelope } from './crypto/sealed.js';
import { BANKS, assertCredentialShape } from './core/banks.js';
import { withBrowser, warmup, closeExtraPages } from './core/browser.js';
import { scrapeBank, mapAccounts } from './core/scraper.js';
import { rawDebugEnabled } from './core/rawReport.js';
import { saveScrape } from './core/cache.js';
import { claimJobs, reportSuccess, reportFailure, notify } from './api/client.js';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const MAX_BROWSER_RESTARTS = 2;

async function processJob(job, browser, privateKey) {
  const jlog = logger(`job:${job.id}`);
  const source = job.bank_source;
  if (!BANKS[source]) throw new Error(`Unknown bank source: ${source}`);

  // Decrypt — plaintext credentials exist only inside this function
  let credentials = openEnvelope(job.encrypted_credentials, privateKey);
  let rawAccounts;
  try {
    assertCredentialShape(source, credentials);

    if (BANKS[source].warmupUrl) await warmup(browser, BANKS[source].warmupUrl, source);

    jlog.info(`scraping ${source} (user ${job.user_id})`);
    // Default Host serves more than one user. Scope diagnostic RAW files so a
    // later scrape of the same provider cannot overwrite another user's audit.
    rawAccounts = await scrapeBank(source, credentials, browser, undefined, `user-${job.user_id}`);
  } finally {
    credentials = null; // drop plaintext even when validation/login throws
  }

  saveScrape(source, rawAccounts);
  const accounts = mapAccounts(source, rawAccounts);
  const txnCount = accounts.reduce((s, a) => s + a.txns.length, 0);
  jlog.info(`scraped ${accounts.length} account(s), ${txnCount} txn(s)`);

  const result = await reportSuccess(job.id, accounts);
  jlog.info(`DONE — ${result.inserted} new, ${result.skipped} skipped (dedup)`);
  markScraped(job.connection_id);
}

async function main() {
  rotateIfNeeded();

  // ── Preconditions (fail loud and early) ──
  if (!process.env.API_URL) {
    log.error('FATAL: API_URL must be set in .env');
    process.exit(1);
  }
  const paired = fs.existsSync(DEVICE_TOKEN_FILE);
  if (!paired && !process.env.BANK_AGENT_KEY) {
    log.error('FATAL: BANK_AGENT_KEY must be set in .env, or pair this device with your SpendWise account');
    process.exit(1);
  }
  if (!fs.existsSync(PRIVATE_KEY_FILE)) {
    log.info('no agent-private.key yet — waiting to be paired (or run: npm run keys for a Default Host install)');
    process.exit(2);
  }
  if (!acquireLock()) {
    log.info('another instance is running — exiting');
    return;
  }

  try {
    const jobs = await claimJobs(5);
    if (jobs.length === 0) {
      log.info('no pending jobs');
      return;
    }

    const privateKey = fs.readFileSync(PRIVATE_KEY_FILE, 'utf8').trim();

    // Cooldown gate: decline jobs we refuse to run so the server sees them.
    // transient:true → the server records the decline without touching the
    // connection's consecutive_failures (a cooldown is not a real failure).
    // RAW debug is a deliberate diagnostic run — bypass the cooldown so a
    // just-synced connection can be re-scraped to capture its raw output.
    const rawDebug = rawDebugEnabled();
    if (rawDebug) log.info('🐞 RAW DEBUG ON — bypassing cooldown, dumping raw scrape to scraped-data/');

    const runnable = [];
    let cooldownDeclined = 0;
    for (const job of jobs) {
      if (inCooldown(job.connection_id) && !rawDebug) {
        log.warn(`sync request ${job.id} (conn ${job.connection_id}) too soon since last successful sync — skipping`);
        await reportFailure(job.id, `Agent cooldown: scraped less than ${COOLDOWN_HOURS}h ago`, { transient: true })
          .catch((e) => log.error(`report failed — ${e.message}`));
        cooldownDeclined++;
      } else {
        runnable.push(job);
      }
    }
    if (cooldownDeclined > 0) {
      log.info(`skipped ${cooldownDeclined} sync request(s): synced recently; ${runnable.length} can run now`);
    }
    if (runnable.length === 0) {
      log.info(`no sync requests can run now (${cooldownDeclined} skipped as too soon)`);
      return;
    }

    let pending = runnable;
    let restarts = 0;
    // Chrome occasionally dies mid-run (seen as every subsequent job failing
    // instantly with "Connection closed" even though the underlying scraper
    // is configured with skipCloseBrowser). Rather than let one crash take
    // down every remaining job in the batch, relaunch and keep going — capped
    // so a fundamentally broken Chrome install can't loop forever.
    while (pending.length > 0 && restarts <= MAX_BROWSER_RESTARTS) {
      const before = pending.length;
      pending = await runJobBatch(pending, privateKey, restarts > 0);
      if (pending.length === before) break; // no progress made — don't spin
      if (pending.length > 0) {
        restarts++;
        log.warn(`browser died mid-run — relaunching (attempt ${restarts}/${MAX_BROWSER_RESTARTS}) for ${pending.length} remaining job(s)`);
      }
    }
  } finally {
    releaseLock();
  }
  log.info('run finished');
}

/**
 * Run as many `jobs` as possible in one browser session. Returns the jobs
 * that were never attempted because the browser died partway through — the
 * caller decides whether to relaunch and retry them or give up.
 */
async function runJobBatch(jobs, privateKey, isRestart) {
  const notAttempted = [];

  await withBrowser(async (browser) => {
    await closeExtraPages(browser, isRestart ? 'browser restart' : 'run start');

    let first = true;
    for (let i = 0; i < jobs.length; i++) {
      const job = jobs[i];
      try {
        if (!first) {
          // Short human-like gap between DIFFERENT banks. This is not the
          // lockout guard (that's the per-connection 3h cooldown), just a
          // pause so we don't fire back-to-back logins. Kept short so a
          // manual multi-account "Sync Now" doesn't look frozen. Overridable.
          const base = parseInt(process.env.INTER_JOB_PAUSE_MS, 10) || 8000;
          const pause = base + Math.floor(Math.random() * 10_000);
          log.info(`pausing ${Math.round(pause / 1000)}s before next job`);
          await sleep(pause);
        }
        first = false;
        await processJob(job, browser, privateKey);
      } catch (err) {
        log.error(`job ${job.id} FAILED — ${err.message}`);
        await notify(`Job ${job.id} (${job.bank_source}) failed: ${err.message}`);
        await reportFailure(job.id, err.message, {
          errorCode: err.code,
          terminal: err.terminal === true,
        })
          .catch((e) => log.error(`report failed — ${e.message}`));
      } finally {
        // Close the scraper's leftover pages so tabs don't pile up job-to-job
        // — skip if the browser itself is already gone, that call would just
        // throw and get logged as noise on top of the real problem.
        if (browser.isConnected()) {
          await closeExtraPages(browser, `after job ${job.id}`);
        }
      }

      if (!browser.isConnected()) {
        notAttempted.push(...jobs.slice(i + 1));
        break;
      }
    }
  });

  return notAttempted;
}

main().catch(async (err) => {
  log.error(`FATAL: ${err.stack || err.message}`);
  await notify(`Agent fatal error: ${err.message}`);
  releaseLock();
  process.exit(1);
});
