/**
 * rawReport.js — render/write the RAW scraper output for debugging.
 *
 * Shared by the standalone diagnostic tool and the live scrape hook: when raw
 * debug is enabled (env SPENDWISE_RAW_DEBUG=1, or a `RAW_DEBUG` flag file the
 * desktop worker toggles), each scrape also drops exactly what
 * israeli-bank-scrapers returned — every field, unmapped — to scraped-data/,
 * so we can inspect fields/dates/loans/descriptions per provider.
 */

import fs from 'node:fs';
import path from 'node:path';
import { DATA_DIR } from '../utils/paths.js';

const FLAG_FILE = path.join(DATA_DIR, 'RAW_DEBUG');

/** Raw debug on? Env var wins; otherwise the toggle flag file (worker button). */
export function rawDebugEnabled() {
  if (process.env.SPENDWISE_RAW_DEBUG === '1') return true;
  try { return fs.existsSync(FLAG_FILE); } catch { return false; }
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
      <h2>${esc(source)} <span class="muted">· ${(accounts || []).length} account(s) · ${allTxns.length} txns</span></h2>
      <p class="meta"><strong>transaction fields present:</strong> <code>${esc(keyUnion.join(', '))}</code></p>
      ${(accounts || []).map(renderAccount).join('')}
    </section>`;
}

/** results: [{ source, accounts, error }] → a self-contained HTML report. */
export function renderReportHtml(results, meta = {}) {
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
<p class="sub">Generated ${new Date().toISOString()}${meta.days ? ` · window ${meta.days} days` : ''} · RAW israeli-bank-scrapers output, no mapping/normalization applied.</p>
${body}
</body></html>`;
}

/** Write one source's raw scrape (JSON + single-bank HTML) to scraped-data/. */
export function writeRawScrape(source, accounts) {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  fs.writeFileSync(path.join(DATA_DIR, `raw-${source}.json`), JSON.stringify(accounts ?? [], null, 2), 'utf8');
  fs.writeFileSync(path.join(DATA_DIR, `raw-${source}.html`), renderReportHtml([{ source, accounts }]), 'utf8');
  return path.join(DATA_DIR, `raw-${source}.html`);
}
