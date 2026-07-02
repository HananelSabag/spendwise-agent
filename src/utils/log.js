/**
 * Structured logger with levels and file rotation.
 * Levels: debug < info < warn < error. LOG_LEVEL env controls verbosity.
 */

import fs from 'node:fs';
import path from 'node:path';
import { ROOT_DIR } from './paths.js';

const LOG_FILE = path.join(ROOT_DIR, 'agent.log');
const MAX_BYTES = 10 * 1024 * 1024;
const LEVELS = { debug: 10, info: 20, warn: 30, error: 40 };
const threshold = LEVELS[(process.env.LOG_LEVEL || 'info').toLowerCase()] ?? LEVELS.info;

export function rotateIfNeeded() {
  try {
    if (fs.existsSync(LOG_FILE) && fs.statSync(LOG_FILE).size > MAX_BYTES) {
      fs.renameSync(LOG_FILE, `${LOG_FILE}.${Date.now()}.bak`);
    }
  } catch { /* rotation is best-effort */ }
}

function write(level, scope, message) {
  if (LEVELS[level] < threshold) return;
  const line = `[${new Date().toISOString()}] ${level.toUpperCase().padEnd(5)} ${scope ? `[${scope}] ` : ''}${message}`;
  console.log(line);
  try {
    fs.appendFileSync(LOG_FILE, line + '\n');
  } catch { /* console output already happened */ }
}

/** Create a logger bound to a scope (e.g. a bank source or module name). */
export function logger(scope = '') {
  return {
    debug: (msg) => write('debug', scope, msg),
    info:  (msg) => write('info',  scope, msg),
    warn:  (msg) => write('warn',  scope, msg),
    error: (msg) => write('error', scope, msg),
    child: (sub) => logger(scope ? `${scope}:${sub}` : sub),
  };
}

export const log = logger();
