/**
 * Browser lifecycle — launch, Cloudflare warmup, and GUARANTEED cleanup.
 *
 * withBrowser() is the only way to get a browser: it always closes it,
 * even on crashes or Ctrl+C (SIGINT/SIGTERM), so no orphaned Chrome
 * processes hold the bank session hostage.
 */

import fs from 'node:fs';
import puppeteer from 'puppeteer';
import { PROFILE_DIR } from '../utils/paths.js';
import { logger } from '../utils/log.js';

const log = logger('browser');

function detectChrome() {
  const candidates = [
    process.env.CHROMIUM_PATH && process.env.CHROMIUM_PATH.trim(),
    '/usr/bin/google-chrome',
    '/usr/bin/google-chrome-stable',
    '/snap/bin/chromium',
    '/usr/bin/chromium-browser',
    '/usr/bin/chromium',
  ].filter(Boolean);
  for (const c of candidates) {
    if (fs.existsSync(c)) return c;
  }
  return puppeteer.executablePath();
}

async function launch() {
  const executablePath = detectChrome();
  log.info(`launching Chrome: ${executablePath}`);
  if (!process.env.DISPLAY && process.platform !== 'win32') {
    log.warn('no DISPLAY set — headful Chrome needs Xvfb on Linux');
  }
  // Headful + real Chrome + persistent profile is what passes Cloudflare's
  // bot check and keeps the cf_clearance cookie between runs.
  return puppeteer.launch({
    headless: false,
    executablePath,
    userDataDir: PROFILE_DIR,
    defaultViewport: null,
    args: [
      '--no-sandbox',
      '--disable-setuid-sandbox',
      '--disable-dev-shm-usage',
      '--disable-blink-features=AutomationControlled',
      '--disable-extensions',
      '--window-size=1280,900',
      // Persistent profile otherwise restores the previous run's tabs and pops
      // a "Chrome didn't shut down correctly" bubble — both leave stray
      // about:blank tabs staring at the user. Suppress them.
      '--hide-crash-restore-bubble',
      '--disable-session-crashed-bubble',
    ],
  });
}

/**
 * Replace stale tabs with one clean about:blank tab. The scrapers run with
 * skipCloseBrowser:true, so their statement/login pages stay open in the
 * shared browser and pile up across jobs. Call this after each job (and once
 * at run start, to clear any tabs the persistent profile restored) so the user
 * never sees a stack of leftover about:blank tabs. Best-effort — never throws.
 */
export async function closeExtraPages(browser, reason = 'cleanup') {
  try {
    const pages = await browser.pages();
    if (pages.length === 1 && pages[0].url() === 'about:blank') return;

    const fresh = await browser.newPage().catch(() => null);
    if (fresh) {
      await fresh.goto('about:blank', { waitUntil: 'domcontentloaded', timeout: 5000 }).catch(() => {});
    }

    let blanked = 0;
    let closed = 0;
    for (const page of pages) {
      if (page.isClosed()) continue;
      await page.goto('about:blank', { waitUntil: 'domcontentloaded', timeout: 5000 })
        .then(() => { blanked += 1; })
        .catch(() => {});
      await page.close({ runBeforeUnload: false })
        .then(() => { closed += 1; })
        .catch(() => {});
    }

    if (!fresh) {
      const remaining = await browser.pages().catch(() => []);
      if (remaining.length === 0) await browser.newPage().catch(() => {});
      else await remaining[0].goto('about:blank', { waitUntil: 'domcontentloaded', timeout: 5000 }).catch(() => {});
    }

    if (closed > 0) log.info(`reset tabs (${reason}): blanked ${blanked}, closed ${closed}, clean tab ready`);
  } catch (err) {
    log.warn(`reset tabs (${reason}) skipped: ${err.message}`);
  }
}

/**
 * Run `fn(browser)` with guaranteed cleanup. Signal handlers ensure the
 * browser dies even on Ctrl+C mid-scrape.
 */
export async function withBrowser(fn) {
  const browser = await launch();
  const kill = async () => {
    log.warn('termination signal — closing browser');
    await browser.close().catch(() => {});
    process.exit(130);
  };
  process.once('SIGINT', kill);
  process.once('SIGTERM', kill);

  try {
    return await fn(browser);
  } finally {
    process.removeListener('SIGINT', kill);
    process.removeListener('SIGTERM', kill);
    await browser.close().catch(() => {});
    log.debug('browser closed');
  }
}

// ── Cloudflare warmup ─────────────────────────────────────────────
function looksLikeChallenge(text, title) {
  const t = (title || '').toLowerCase();
  const b = (text || '').toLowerCase();
  return (
    t.includes('just a moment') ||
    b.includes('security verification') ||
    b.includes('verifying you are') ||
    b.includes('checking your browser') ||
    b.includes('needs to review the security')
  );
}

/** Visit a bank's public page first so a Cloudflare challenge can clear. */
export async function warmup(browser, url, label) {
  const page = await browser.newPage();
  const wlog = logger(label);
  try {
    wlog.info(`warming up Cloudflare: ${url}`);
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 60000 }).catch(() => {});
    const deadline = Date.now() + 45000;
    let cleared = false;
    while (Date.now() < deadline) {
      const blocked = await page
        .evaluate(() => [document.body ? document.body.innerText : '', document.title])
        .then(([text, title]) => looksLikeChallenge(text, title))
        .catch(() => false);
      if (!blocked) { cleared = true; break; }
      await new Promise((r) => setTimeout(r, 2000));
    }
    wlog[cleared ? 'info' : 'warn'](cleared ? 'Cloudflare clear' : 'challenge did not clear within 45s');
  } finally {
    await page.close().catch(() => {});
  }
}
