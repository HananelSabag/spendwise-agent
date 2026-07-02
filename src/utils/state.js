/**
 * Local agent state: per-connection cooldowns + single-instance lock.
 * The cooldown is the agent's OWN safety floor — even if the server queue
 * misbehaves, this machine will never log into the same bank connection
 * more often than COOLDOWN_HOURS.
 */

import fs from 'node:fs';
import { STATE_FILE, LOCK_FILE } from './paths.js';

export const COOLDOWN_HOURS = 3;
const LOCK_STALE_MS = 60 * 60_000; // a scrape should never take an hour

function load() {
  try { return JSON.parse(fs.readFileSync(STATE_FILE, 'utf8')); } catch { return {}; }
}
function save(state) {
  try { fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2), 'utf8'); } catch { /* non-fatal */ }
}

/** True if this connection was scraped less than COOLDOWN_HOURS ago. */
export function inCooldown(connectionId) {
  const last = load()[`conn-${connectionId}`];
  return Boolean(last && Date.now() - last < COOLDOWN_HOURS * 3600_000);
}

export function markScraped(connectionId) {
  const state = load();
  state[`conn-${connectionId}`] = Date.now();
  save(state);
}

/**
 * Acquire the single-instance lock. Returns false when another live
 * instance holds it; steals locks older than LOCK_STALE_MS.
 */
export function acquireLock() {
  try {
    if (fs.existsSync(LOCK_FILE)) {
      const age = Date.now() - fs.statSync(LOCK_FILE).mtimeMs;
      if (age < LOCK_STALE_MS) return false;
    }
    fs.writeFileSync(LOCK_FILE, String(process.pid));
    return true;
  } catch {
    return false;
  }
}

export function releaseLock() {
  try { fs.unlinkSync(LOCK_FILE); } catch { /* already gone */ }
}
