/**
 * Local raw-data cache. Every successful scrape is saved so development
 * and debugging can replay data (OFFLINE=1) without touching a bank —
 * banks lock accounts on frequent logins, so replay is a safety feature,
 * not just a convenience.
 */

import fs from 'node:fs';
import path from 'node:path';
import { DATA_DIR } from '../utils/paths.js';
import { logger } from '../utils/log.js';

function fileFor(source) {
  return path.join(DATA_DIR, `${source}-latest.json`);
}

export function saveScrape(source, accounts) {
  const log = logger(source);
  try {
    fs.mkdirSync(DATA_DIR, { recursive: true });
    const payload = { savedAt: new Date().toISOString(), source, accounts };
    fs.writeFileSync(fileFor(source), JSON.stringify(payload, null, 2), 'utf8');
    log.info(`raw data saved: scraped-data/${source}-latest.json`);
  } catch (err) {
    log.warn(`could not save cache — ${err.message}`);
  }
}

export function loadScrape(source) {
  const file = fileFor(source);
  if (!fs.existsSync(file)) throw new Error(`No cache file: ${file}`);
  const { savedAt, accounts } = JSON.parse(fs.readFileSync(file, 'utf8'));
  logger(source).info(`OFFLINE replay — ${accounts.length} account(s) from cache (saved ${savedAt})`);
  return accounts;
}
