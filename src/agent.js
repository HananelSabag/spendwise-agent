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
 * .env needs ONLY: API_URL, BANK_AGENT_KEY (+ optional NTFY_*, CHROMIUM_PATH).
 */

import dotenv from 'dotenv';
dotenv.config({ override: true });

import fs from 'node:fs';
import { PRIVATE_KEY_FILE } from './utils/paths.js';
import { log, rotateIfNeeded, logger } from './utils/log.js';
import { inCooldown, markScraped, acquireLock, releaseLock, COOLDOWN_HOURS } from './utils/state.js';
import { open as openEnvelope } from './crypto/sealed.js';
import { BANKS, assertCredentialShape } from './core/banks.js';
import { withBrowser, warmup } from './core/browser.js';
import { scrapeBank, mapAccounts } from './core/scraper.js';
import { saveScrape } from './core/cache.js';
import { claimJobs, reportSuccess, reportFailure, notify } from './api/client.js';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function processJob(job, browser, privateKey) {
  const jlog = logger(`job:${job.id}`);
  const source = job.bank_source;
  if (!BANKS[source]) throw new Error(`Unknown bank source: ${source}`);

  // Decrypt — plaintext credentials exist only inside this function
  let credentials = openEnvelope(job.encrypted_credentials, privateKey);
  assertCredentialShape(source, credentials);

  if (BANKS[source].warmupUrl) await warmup(browser, BANKS[source].warmupUrl, source);

  jlog.info(`scraping ${source} (user ${job.user_id})`);
  const rawAccounts = await scrapeBank(source, credentials, browser);
  credentials = null; // drop the plaintext reference the moment it's not needed

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
  if (!process.env.API_URL || !process.env.BANK_AGENT_KEY) {
    log.error('FATAL: API_URL and BANK_AGENT_KEY must be set in .env');
    process.exit(1);
  }
  if (!fs.existsSync(PRIVATE_KEY_FILE)) {
    log.error('FATAL: agent-private.key not found. Run: npm run keys');
    process.exit(1);
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
    const runnable = [];
    for (const job of jobs) {
      if (inCooldown(job.connection_id)) {
        log.warn(`job ${job.id} (conn ${job.connection_id}) inside ${COOLDOWN_HOURS}h cooldown — declining`);
        await reportFailure(job.id, `Agent cooldown: scraped less than ${COOLDOWN_HOURS}h ago`, { transient: true })
          .catch((e) => log.error(`report failed — ${e.message}`));
      } else {
        runnable.push(job);
      }
    }
    if (runnable.length === 0) return;

    await withBrowser(async (browser) => {
      let first = true;
      for (const job of runnable) {
        try {
          if (!first) {
            // Human-like pause between banks
            const pause = 2 * 60_000 + Math.floor(Math.random() * 3 * 60_000);
            log.info(`pausing ${Math.round(pause / 1000)}s before next job`);
            await sleep(pause);
          }
          first = false;
          await processJob(job, browser, privateKey);
        } catch (err) {
          log.error(`job ${job.id} FAILED — ${err.message}`);
          await notify(`Job ${job.id} (${job.bank_source}) failed: ${err.message}`);
          await reportFailure(job.id, err.message)
            .catch((e) => log.error(`report failed — ${e.message}`));
        }
      }
    });
  } finally {
    releaseLock();
  }
  log.info('run finished');
}

main().catch(async (err) => {
  log.error(`FATAL: ${err.stack || err.message}`);
  await notify(`Agent fatal error: ${err.message}`);
  releaseLock();
  process.exit(1);
});
