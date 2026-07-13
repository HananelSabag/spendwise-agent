import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mapAccounts } from '../src/core/scraper.js';
import { BANKS, assertCredentialShape } from '../src/core/banks.js';

test('mapAccounts maps a valid account', () => {
  const raw = [{
    accountNumber: '120-353778',
    balance: 612.5,
    txns: [
      { date: '2026-06-20T21:00:00.000Z', description: 'קצבת ילדים', chargedAmount: 611, identifier: 9001005001417 },
      { date: '2026-06-18T21:00:00.000Z', description: 'משיכה', chargedAmount: -305 },
    ],
  }];
  const [acc] = mapAccounts('yahav', raw);
  assert.equal(acc.account_number, '120-353778');
  assert.equal(acc.balance, 612.5);
  assert.equal(acc.txns.length, 2);
  assert.equal(acc.txns[0].identifier, '9001005001417');   // stringified
  assert.equal(acc.txns[1].identifier, undefined);         // absent stays absent
});

test('mapAccounts preserves card purchase and statement dates separately', () => {
  const raw = [{
    accountNumber: '9962',
    txns: [{
      date: '2026-07-09T12:00:00.000Z',
      processedDate: '2026-07-10T00:00:00.000Z',
      description: 'Card purchase',
      chargedAmount: -275.5,
      identifier: 'cal-1',
      status: 'completed',
    }],
  }];

  const [account] = mapAccounts('visa_cal', raw);
  assert.equal(account.txns[0].date, '2026-07-09T12:00:00.000Z');
  assert.equal(account.txns[0].processed_date, '2026-07-10T00:00:00.000Z');
  assert.equal(account.txns[0].status, 'completed');
});

test('mapAccounts preserves provider memo as bank notes', () => {
  const [account] = mapAccounts('leumi', [{
    accountNumber: '1234',
    txns: [{
      chargedAmount: 13327.75,
      date: '2026-07-08T21:00:00.000Z',
      description: 'Salary transfer',
      memo: 'Monthly salary',
    }],
  }]);

  assert.equal(account.txns[0].notes, 'Monthly salary');
});

test('mapAccounts preserves FX, transaction kind, and installment metadata', () => {
  const [account] = mapAccounts('max', [{
    accountNumber: '2254',
    txns: [{
      chargedAmount: -61.72,
      chargedCurrency: 'ILS',
      originalAmount: -20,
      originalCurrency: 'USD',
      type: 'installments',
      installments: { number: 5, total: 10 },
      date: '2026-07-02T12:00:00.000Z',
      description: 'Provider charge',
    }],
  }]);

  assert.equal(account.txns[0].original_amount, -20);
  assert.equal(account.txns[0].original_currency, 'USD');
  assert.equal(account.txns[0].charged_currency, 'ILS');
  assert.equal(account.txns[0].txn_kind, 'installments');
  assert.equal(account.txns[0].installment_number, 5);
  assert.equal(account.txns[0].installment_total, 10);
});

test('mapAccounts uses the original ILS amount for a zero-valued pending MAX authorization', () => {
  const [account] = mapAccounts('max', [{
    accountNumber: '2254',
    txns: [{
      chargedAmount: 0,
      originalAmount: -384.95,
      originalCurrency: 'ILS',
      date: '2026-07-11T18:57:00.000Z',
      description: 'Pending purchase',
      status: 'pending',
      identifier: null,
    }],
  }]);

  assert.equal(account.txns[0].charged_amount, -384.95);
  assert.equal(account.txns[0].original_amount, -384.95);
  assert.equal(account.txns[0].status, 'pending');
});

test('mapAccounts uses the original ILS amount for a zero-valued completed CAL row', () => {
  const [account] = mapAccounts('visa_cal', [{
    accountNumber: '9962',
    txns: [{
      chargedAmount: 0,
      originalAmount: -48,
      originalCurrency: '₪',
      date: '2026-06-03T09:06:35.000Z',
      description: 'Completed purchase',
      status: 'completed',
      identifier: 'cal-48',
    }],
  }]);

  assert.equal(account.txns[0].charged_amount, -48);
  assert.equal(account.txns[0].status, 'completed');
});

test('mapAccounts does not treat a foreign pending amount as converted ILS', () => {
  const [account] = mapAccounts('max', [{
    accountNumber: '2254',
    txns: [{
      chargedAmount: 0,
      originalAmount: -20,
      originalCurrency: 'USD',
      date: '2026-07-11T18:57:00.000Z',
      description: 'Pending foreign purchase',
      status: 'pending',
    }],
  }]);

  assert.equal(account.txns[0].charged_amount, 0);
});

test('mapAccounts extracts installment position from provider memo fallback', () => {
  const [account] = mapAccounts('max', [{
    accountNumber: '2254',
    txns: [{
      chargedAmount: -518.98,
      date: '2026-07-10T00:00:00.000Z',
      description: 'Municipality',
      memo: 'תשלום 5 מתוך 10',
      type: 'installments',
    }],
  }]);

  assert.equal(account.txns[0].installment_number, 5);
  assert.equal(account.txns[0].installment_total, 10);
});

test('mapAccounts ignores invalid processed dates and unknown statuses', () => {
  const raw = [{
    accountNumber: 'x',
    txns: [{
      date: '2026-07-10T08:00:00.000Z',
      processedDate: 'not-a-date',
      description: 'Pending purchase',
      chargedAmount: -50,
      status: 'mystery',
    }],
  }];

  const [account] = mapAccounts('max', raw);
  assert.equal(account.txns[0].processed_date, undefined);
  assert.equal(account.txns[0].status, undefined);
});

test('mapAccounts drops malformed transactions instead of sending garbage', () => {
  const raw = [{
    accountNumber: 'x',
    txns: [
      { date: 'not-a-date', description: 'bad date', chargedAmount: 100 },
      { date: '2026-06-20T21:00:00.000Z', description: 'bad amount', chargedAmount: 'NaN!' },
      { date: '2026-06-20T21:00:00.000Z', description: 'good', chargedAmount: -50 },
    ],
  }];
  const [acc] = mapAccounts('yahav', raw);
  assert.equal(acc.txns.length, 1);
  assert.equal(acc.txns[0].description, 'good');
});

test('mapAccounts: missing/invalid balance becomes null, real zero stays 0', () => {
  const raw = [
    { accountNumber: 'a', txns: [] },                    // no balance
    { accountNumber: 'b', balance: 0, txns: [] },        // real zero
    { accountNumber: 'c', balance: NaN, txns: [] },      // invalid
  ];
  const mapped = mapAccounts('yahav', raw);
  assert.equal(mapped[0].balance, null);
  assert.equal(mapped[1].balance, 0);
  assert.equal(mapped[2].balance, null);
});

test('assertCredentialShape enforces per-bank required fields', () => {
  assert.doesNotThrow(() =>
    assertCredentialShape('yahav', { username: 'u', password: 'p', nationalID: '1' }));
  assert.throws(() =>
    assertCredentialShape('yahav', { username: 'u' }), /missing fields: password, nationalID/);
  assert.doesNotThrow(() =>
    assertCredentialShape('max', { username: 'u', password: 'p' }));
});

test('BANKS registry exposes supported SpendWise source ids with scraper company ids', () => {
  assert.equal(BANKS.visa_cal.companyId, 'visaCal');
  assert.equal(BANKS.otsar_hahayal.companyId, 'otsarHahayal');
  assert.deepEqual(BANKS.hapoalim.credFields, ['userCode', 'password']);
  assert.deepEqual(BANKS.amex.credFields, ['id', 'card6Digits', 'password']);
});

test('assertCredentialShape covers newly exposed banks and credit companies', () => {
  assert.doesNotThrow(() =>
    assertCredentialShape('hapoalim', { userCode: 'u', password: 'p' }));
  assert.doesNotThrow(() =>
    assertCredentialShape('visa_cal', { username: 'u', password: 'p' }));
  assert.doesNotThrow(() =>
    assertCredentialShape('mercantile', { id: '1', password: 'p', num: '7' }));
  assert.throws(() =>
    assertCredentialShape('amex', { id: '1', password: 'p' }), /missing fields: card6Digits/);
});
