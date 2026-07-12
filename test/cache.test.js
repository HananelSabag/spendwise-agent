import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { saveScrape } from '../src/core/cache.js';
import { writeRawScrape } from '../src/core/rawReport.js';
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

test('writeRawScrape scopes diagnostic files by user', () => {
  const source = 'unit-test';
  const scope = 'user-34/unsafe';
  const jsonFile = path.join(DATA_DIR, 'raw-unit-test-user-34unsafe.json');
  const htmlFile = path.join(DATA_DIR, 'raw-unit-test-user-34unsafe.html');

  try {
    const report = writeRawScrape(source, [{ accountNumber: 'scoped' }], scope);
    assert.equal(report, htmlFile);
    assert.equal(fs.existsSync(jsonFile), true);
    assert.equal(fs.existsSync(htmlFile), true);
  } finally {
    fs.rmSync(jsonFile, { force: true });
    fs.rmSync(htmlFile, { force: true });
  }
});
