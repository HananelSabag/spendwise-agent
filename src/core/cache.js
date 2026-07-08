/**
 * Optional raw-data cache for explicit debugging only.
 * Production/default behavior does not write accounts or transactions to disk.
 * Set DEBUG_SAVE_SCRAPES=true only for a short local debugging session.
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
  if (process.env.DEBUG_SAVE_SCRAPES !== 'true') {
    log.debug('raw scrape cache disabled');
    return;
  }

  try {
    fs.mkdirSync(DATA_DIR, { recursive: true });
    const payload = { savedAt: new Date().toISOString(), source, accounts };
    fs.writeFileSync(fileFor(source), JSON.stringify(payload, null, 2), 'utf8');
    log.info(`raw data saved: scraped-data/${source}-latest.json`);
  } catch (err) {
    log.warn(`could not save cache - ${err.message}`);
  }
}

export function loadScrape(source) {
  const file = fileFor(source);
  if (!fs.existsSync(file)) throw new Error(`No cache file: ${file}`);
  const { savedAt, accounts } = JSON.parse(fs.readFileSync(file, 'utf8'));
  logger(source).info(`OFFLINE replay - ${accounts.length} account(s) from cache (saved ${savedAt})`);
  return accounts;
}
