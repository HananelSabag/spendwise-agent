/**
 * analyze-balance-html.js
 *
 * Offline tool — parses scraped-data/yahav-home-snapshot.html and finds
 * every element that might contain the account balance.
 *
 * Run after a scrape (or after manually saving the page):
 *   node analyze-balance-html.js
 *
 * Output: ranked list of CSS selectors + their text values, so you can
 * pick the right one and update BALANCE_SELECTORS in sync.js.
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { JSDOM } from 'jsdom';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const HTML_FILE = path.join(__dirname, 'scraped-data', 'yahav-home-snapshot.html');

if (!fs.existsSync(HTML_FILE)) {
  console.error('No snapshot found at scraped-data/yahav-home-snapshot.html');
  console.error('Run the scraper once to generate it, or save the Yahav home page manually.');
  process.exit(1);
}

const html  = fs.readFileSync(HTML_FILE, 'utf8');
const dom   = new JSDOM(html);
const doc   = dom.window.document;

// ── Helper: best CSS path for an element ─────────────────────────────────────
function cssPath(el) {
  const parts = [];
  while (el && el.nodeType === 1) {
    let sel = el.tagName.toLowerCase();
    if (el.id)        sel += `#${el.id}`;
    else if (el.className) {
      const cls = [...el.classList].slice(0, 3).join('.');
      if (cls) sel += `.${cls}`;
    }
    parts.unshift(sel);
    el = el.parentElement;
    if (parts.length >= 5) break;
  }
  return parts.join(' > ');
}

// ── 1. Find every element whose text looks like a money amount ────────────────
console.log('\n══════════════════════════════════════════════════════');
console.log(' ELEMENTS CONTAINING A NUMBER (potential balance)');
console.log('══════════════════════════════════════════════════════');

const moneyRe = /^[\s₪,\d.+-]+$/;
const allEls  = [...doc.querySelectorAll('*')];
const hits    = [];

for (const el of allEls) {
  // Leaf or near-leaf only (few children)
  if (el.children.length > 2) continue;
  const text = (el.textContent || '').trim();
  if (!text || text.length > 30) continue;
  const n = parseFloat(text.replace(/[^\d.-]/g, ''));
  if (isNaN(n) || n === 0) continue;
  if (!moneyRe.test(text)) continue;
  // Must look like a plausible ILS balance (positive, reasonable range)
  if (n < 1 || n > 5_000_000) continue;
  hits.push({ el, text, n });
}

if (hits.length === 0) {
  console.log('  (none found — the page may be the login screen, not the account home)');
} else {
  hits.sort((a, b) => {
    // Prefer elements with "balance"/"יתרה" in class names
    const aScore = (a.el.className || '').includes('balance') ? 1 : 0;
    const bScore = (b.el.className || '').includes('balance') ? 1 : 0;
    return bScore - aScore;
  });
  for (const { el, text, n } of hits.slice(0, 20)) {
    const cls  = el.className ? `.${[...el.classList].join('.')}` : '';
    const tag  = el.tagName.toLowerCase();
    console.log(`  ${tag}${cls}`);
    console.log(`    text: "${text}"  →  ${n}`);
    console.log(`    path: ${cssPath(el)}`);
    console.log();
  }
}

// ── 2. Find elements containing "יתרה" ───────────────────────────────────────
console.log('══════════════════════════════════════════════════════');
console.log(' ELEMENTS CONTAINING "יתרה" (balance label)');
console.log('══════════════════════════════════════════════════════');

const yitraEls = allEls.filter(el => (el.textContent || '').includes('יתרה'));
if (yitraEls.length === 0) {
  console.log('  (none — page might not be logged in or not the home page)');
} else {
  for (const el of yitraEls.slice(0, 10)) {
    const text = (el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 120);
    console.log(`  ${el.tagName.toLowerCase()}.${[...el.classList].join('.')}`);
    console.log(`    text: "${text}"`);
    console.log(`    path: ${cssPath(el)}`);
    console.log();
  }
}

// ── 3. Class names containing "balance" / "amount" ───────────────────────────
console.log('══════════════════════════════════════════════════════');
console.log(' CLASSES CONTAINING "balance" OR "amount"');
console.log('══════════════════════════════════════════════════════');

const balanceEls = allEls.filter(el => {
  const cls = (el.className || '').toLowerCase();
  return cls.includes('balance') || cls.includes('amount') || cls.includes('yitra');
});

if (balanceEls.length === 0) {
  console.log('  (none)');
} else {
  for (const el of balanceEls.slice(0, 20)) {
    const text = (el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 80);
    console.log(`  ${el.tagName.toLowerCase()}.${[...el.classList].join('.')}`);
    console.log(`    text: "${text}"`);
    console.log();
  }
}

// ── 4. Page URL / title (sanity check) ───────────────────────────────────────
console.log('══════════════════════════════════════════════════════');
console.log(` PAGE TITLE: "${doc.title}"`);
const baseEl = doc.querySelector('base');
console.log(` BASE HREF:  ${baseEl ? baseEl.href : '(none)'}`);
console.log('══════════════════════════════════════════════════════\n');
