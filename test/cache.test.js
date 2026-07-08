import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { saveScrape } from '../src/core/cache.js';
import { DATA_DIR } from '../src/utils/paths.js';

test('saveScrape does not write raw bank data by default', () => {
  const previous = process.env.DEBUG_SAVE_SCRAPES;
  delete process.env.DEBUG_SAVE_SCRAPES;

  try {
    const file = path.join(DATA_DIR, 'unit-test-latest.json');
    fs.rmSync(file, { force: true });
    saveScrape('unit-test', [{ accountNumber: 'secret', txns: [{ description: 'secret txn' }] }]);
    assert.equal(fs.existsSync(file), false);
  } finally {
    if (previous === undefined) delete process.env.DEBUG_SAVE_SCRAPES;
    else process.env.DEBUG_SAVE_SCRAPES = previous;
  }
});
