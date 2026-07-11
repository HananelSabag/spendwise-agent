/**
 * raw-scrape-report.js — DIAGNOSTIC ONLY (no mapping, no POST, no server).
 *
 * Scrapes each bank/card whose credentials exist in your local .env and writes
 * exactly what `israeli-bank-scrapers` returns — raw, as-is — to a single
 * self-contained HTML report + one JSON file per source. This is the "see the
 * truth before we transform anything" step: which fields exist, how dates look
 * (date vs processedDate), whether loans/installments/memo appear, how reliable
 * descriptions are, per provider.
 *
 * Run:
 *   1) Put credentials in spendwise-agent/.env (same var names as standalone):
 *        LEUMI_USERNAME / LEUMI_PASSWORD
 *        MAX_USERNAME / MAX_PASSWORD
 *        VISA_CAL_USERNAME / VISA_CAL_PASSWORD   (etc.)
 *   2) node tools/raw-scrape-report.js
 *      (optional: DIAG_DAYS=90 to widen the window; ONLY_BANK=leumi to scope)
 *   3) Open scraped-data/raw-report.html and send it over.
 *
 * Nothing is sent anywhere — it only writes local files.
 */

import dotenv from 'dotenv';
dotenv.config({ override: true });

import fs from 'node:fs';
import path from 'node:path';

import { BANKS } from '../src/core/banks.js';
import { withBrowser, warmup } from '../src/core/browser.js';
import { scrapeBank } from '../src/core/scraper.js';
import { DATA_DIR } from '../src/utils/paths.js';

// Credential shapes per provider (mirror of standalone's ENV_CREDS — kept local
// so this diagnostic never touches the production sync path).
const CREDS = {
  leumi:      { req: ['LEUMI_USERNAME', 'LEUMI_PASSWORD'], build: () => ({ username: process.env.LEUMI_USERNAME, password: process.env.LEUMI_PASSWORD }) },
  hapoalim:   { req: ['HAPOALIM_USER_CODE', 'HAPOALIM_PASSWORD'], build: () => ({ userCode: process.env.HAPOALIM_USER_CODE, password: process.env.HAPOALIM_PASSWORD }) },
  discount:   { req: ['DISCOUNT_ID', 'DISCOUNT_PASSWORD', 'DISCOUNT_NUM'], build: () => ({ id: process.env.DISCOUNT_ID, password: process.env.DISCOUNT_PASSWORD, num: process.env.DISCOUNT_NUM }) },
  yahav:      { req: ['YAHAV_USERNAME', 'YAHAV_PASSWORD', 'YAHAV_NATIONAL_ID'], build: () => ({ username: process.env.YAHAV_USERNAME, password: process.env.YAHAV_PASSWORD, nationalID: process.env.YAHAV_NATIONAL_ID }) },
  max:        { req: ['MAX_USERNAME', 'MAX_PASSWORD'], build: () => ({ username: process.env.MAX_USERNAME, password: process.env.MAX_PASSWORD }) },
  visa_cal:   { req: ['VISA_CAL_USERNAME', 'VISA_CAL_PASSWORD'], build: () => ({ username: process.env.VISA_CAL_USERNAME, password: process.env.VISA_CAL_PASSWORD }) },
  isracard:   { req: ['ISRACARD_ID', 'ISRACARD_CARD6', 'ISRACARD_PASSWORD'], build: () => ({ id: process.env.ISRACARD_ID, card6Digits: process.env.ISRACARD_CARD6, password: process.env.ISRACARD_PASSWORD }) },
  amex:       { req: ['AMEX_ID', 'AMEX_CARD6', 'AMEX_PASSWORD'], build: () => ({ id: process.env.AMEX_ID, card6Digits: process.env.AMEX_CARD6, password: process.env.AMEX_PASSWORD }) },
};

const has = (source) => CREDS[source] && CREDS[source].req.every((k) => (process.env[k] || '').trim() !== '');

function windowStart() {
  const days = parseInt(process.env.DIAG_DAYS || '90', 10);
  const d = new Date();
  d.setDate(d.getDate() - (Number.isFinite(days) && days > 0 ? days : 90));
  return d;
}

// ── HTML rendering ────────────────────────────────────────────────────────────
const esc = (v) => String(v ?? '')
  .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

const cell = (v) => {
  if (v === null || v === undefined) return '<span class="null">—</span>';
  if (typeof v === 'object') return `<code>${esc(JSON.stringify(v))}</code>`;
  return esc(v);
};

function renderTxnTable(txns) {
  if (!txns || !txns.length) return '<p class="muted">no transactions</p>';
  // Union of every key present on any txn — so fields we don't currently map
  // (memo, installments, type, status, category…) still show up.
  const keys = [...txns.reduce((set, t) => { Object.keys(t || {}).forEach((k) => set.add(k)); return set; }, new Set())];
  const head = keys.map((k) => `<th>${esc(k)}</th>`).join('');
  const rows = txns.map((t) => `<tr>${keys.map((k) => `<td>${cell(t[k])}</td>`).join('')}</tr>`).join('');
  return `<div class="scroll"><table><thead><tr>${head}</tr></thead><tbody>${rows}</tbody></table></div>`;
}

function renderAccount(acc) {
  const meta = Object.fromEntries(Object.entries(acc).filter(([k]) => k !== 'txns'));
  const n = (acc.txns || []).length;
  return `
    <div class="account">
      <h3>account: ${esc(acc.accountNumber || '?')} <span class="muted">· ${n} txns</span></h3>
      <p class="meta"><strong>account fields:</strong> <code>${esc(JSON.stringify(meta))}</code></p>
      ${renderTxnTable(acc.txns)}
    </div>`;
}

function renderBank(source, accounts, error) {
  if (error) {
    return `<section class="bank"><h2>${esc(source)} <span class="err">FAILED</span></h2><pre class="err">${esc(error)}</pre></section>`;
  }
  const allTxns = (accounts || []).flatMap((a) => a.txns || []);
  const keyUnion = [...allTxns.reduce((s, t) => { Object.keys(t || {}).forEach((k) => s.add(k)); return s; }, new Set())];
  return `
    <section class="bank">
      <h2>${esc(source)} <span class="muted">· ${accounts.length} account(s) · ${allTxns.length} txns</span></h2>
      <p class="meta"><strong>transaction fields present:</strong> <code>${esc(keyUnion.join(', '))}</code></p>
      ${(accounts || []).map(renderAccount).join('')}
    </section>`;
}

function buildHtml(results) {
  const body = results.map((r) => renderBank(r.source, r.accounts, r.error)).join('\n');
  return `<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8">
<title>SpendWise raw scrape report</title>
<style>
  body{font-family:system-ui,Segoe UI,Arial,sans-serif;margin:0;padding:24px;background:#0b1020;color:#e6e9f2}
  h1{margin:0 0 4px} .sub{color:#8b93a7;margin:0 0 24px;font-size:13px}
  section.bank{background:#141a2e;border:1px solid #24304d;border-radius:14px;padding:16px;margin:0 0 20px}
  h2{margin:0 0 8px;font-size:18px} h3{margin:14px 0 6px;font-size:14px;color:#c7d0e6}
  .muted{color:#8b93a7;font-weight:400} .null{color:#5b6478}
  .meta{font-size:12px;color:#9aa3b8;margin:4px 0} code{color:#8fd3ff;word-break:break-all}
  .scroll{overflow-x:auto;border:1px solid #24304d;border-radius:8px}
  table{border-collapse:collapse;width:100%;font-size:12px;direction:ltr}
  th,td{border-bottom:1px solid #24304d;padding:6px 8px;text-align:start;white-space:nowrap;vertical-align:top}
  th{background:#1b2440;position:sticky;top:0;color:#c7d0e6}
  .err{color:#ff8a8a} pre.err{white-space:pre-wrap}
</style></head><body>
<h1>SpendWise — raw scrape report</h1>
<p class="sub">Generated ${new Date().toISOString()} · window ${process.env.DIAG_DAYS || 90} days · RAW israeli-bank-scrapers output, no mapping/normalization applied.</p>
${body}
</body></html>`;
}

// ── Main ──────────────────────────────────────────────────────────────────────
async function main() {
  const only = (process.env.ONLY_BANK || '').trim().toLowerCase();
  const active = Object.keys(CREDS).filter((s) => BANKS[s] && has(s) && (!only || s === only));

  if (!active.length) {
    console.log('No credentials found in .env for any known bank. Set e.g. LEUMI_USERNAME/LEUMI_PASSWORD and retry.');
    return;
  }
  console.log(`Diagnostic scrape (RAW, no POST) for: ${active.join(', ')}`);
  fs.mkdirSync(DATA_DIR, { recursive: true });

  const results = [];
  await withBrowser(async (browser) => {
    for (const source of active) {
      try {
        console.log(`→ scraping ${source} …`);
        if (BANKS[source].warmupUrl) await warmup(browser, BANKS[source].warmupUrl, source);
        const accounts = await scrapeBank(source, CREDS[source].build(), browser, windowStart());
        results.push({ source, accounts });
        fs.writeFileSync(path.join(DATA_DIR, `raw-${source}.json`), JSON.stringify(accounts, null, 2), 'utf8');
        console.log(`   ✓ ${source}: ${accounts.length} account(s)`);
      } catch (err) {
        results.push({ source, error: err.stack || err.message });
        console.log(`   ✗ ${source}: ${err.message}`);
      }
    }
  });

  const htmlPath = path.join(DATA_DIR, 'raw-report.html');
  fs.writeFileSync(htmlPath, buildHtml(results), 'utf8');
  console.log(`\nReport written: ${htmlPath}`);
  console.log('Open it and send it over — plus the raw-*.json files if you want.');
}

main().catch((err) => { console.error(err); process.exitCode = 1; });
