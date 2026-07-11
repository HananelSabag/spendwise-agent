/**
 * Reconcile captured RAW scraper truth with production rows.
 * Defaults to preview only. Add --apply to commit.
 *
 * Usage: node tools/reconcile-raw-to-db.js --user 1 --cycle-day 9 [--apply]
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import dotenv from 'dotenv';
import { mapAccounts } from '../src/core/scraper.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const agentRoot = path.resolve(here, '..');
const serverRoot = path.resolve(agentRoot, '..', 'SpendWise', 'server');
dotenv.config({ path: path.join(serverRoot, '.env') });
process.env.DB_POOL_MAX = '1';
process.chdir(serverRoot);

const require = createRequire(import.meta.url);
const db = require(path.join(serverRoot, 'config', 'db.js'));
const { ingestAccounts } = require(path.join(serverRoot, 'services', 'bankSyncService.js'));

const args = process.argv.slice(2);
const valueAfter = (name, fallback) => {
  const index = args.indexOf(name);
  return index >= 0 && args[index + 1] ? args[index + 1] : fallback;
};
const apply = args.includes('--apply');
const userId = Number(valueAfter('--user', '1'));
const cycleDay = Number(valueAfter('--cycle-day', '9'));
if (!Number.isInteger(userId) || userId < 1) throw new Error('invalid --user');
if (!Number.isInteger(cycleDay) || cycleDay < 1 || cycleDay > 31) throw new Error('invalid --cycle-day');

const sources = ['leumi', 'max', 'visa_cal'];

async function main() {
  const client = await db.getClient();
  try {
    const preview = await client.query(`
      SELECT user_id, bank_source, count(*)::int AS rows
        FROM transactions
       WHERE bank_source IS NOT NULL
         AND transaction_datetime IS NOT NULL
         AND date IS DISTINCT FROM (transaction_datetime AT TIME ZONE 'Asia/Jerusalem')::date
       GROUP BY user_id, bank_source
       ORDER BY user_id, bank_source
    `);
    const rawCounts = Object.fromEntries(sources.map((source) => {
      const raw = JSON.parse(fs.readFileSync(path.join(agentRoot, 'scraped-data', `raw-${source}.json`), 'utf8'));
      return [source, raw.reduce((sum, account) => sum + (account.txns?.length || 0), 0)];
    }));

    if (!apply) {
      process.stdout.write(`${JSON.stringify({ mode: 'preview', userId, cycleDay, rawCounts, shiftedDates: preview.rows }, null, 2)}\n`);
      return;
    }

    await client.query('BEGIN');
    const ingest = {};
    for (const source of sources) {
      const raw = JSON.parse(fs.readFileSync(path.join(agentRoot, 'scraped-data', `raw-${source}.json`), 'utf8'));
      ingest[source] = await ingestAccounts(client, userId, source, mapAccounts(source, raw));
    }

    const repaired = await client.query(`
      UPDATE transactions
         SET date = (transaction_datetime AT TIME ZONE 'Asia/Jerusalem')::date,
             updated_at = now()
       WHERE bank_source IS NOT NULL
         AND transaction_datetime IS NOT NULL
         AND date IS DISTINCT FROM (transaction_datetime AT TIME ZONE 'Asia/Jerusalem')::date
      RETURNING user_id, bank_source
    `);
    await client.query(
      'UPDATE users SET billing_cycle_day = $2, updated_at = now() WHERE id = $1',
      [userId, cycleDay],
    );
    await client.query('COMMIT');

    const repairedBySource = {};
    for (const row of repaired.rows) {
      const key = `${row.user_id}:${row.bank_source}`;
      repairedBySource[key] = (repairedBySource[key] || 0) + 1;
    }
    process.stdout.write(`${JSON.stringify({ mode: 'applied', userId, cycleDay, ingest, repairedDates: repaired.rowCount, repairedBySource }, null, 2)}\n`);
  } catch (error) {
    if (apply) await client.query('ROLLBACK').catch(() => {});
    throw error;
  } finally {
    client.release();
  }
}

main()
  .catch((error) => {
    process.stderr.write(`${error.stack || error.message}\n`);
    process.exitCode = 1;
  })
  .finally(() => db.pool.end());
