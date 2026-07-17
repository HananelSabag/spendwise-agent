/**
 * Bank registry — everything the agent knows about each supported bank.
 *
 * credFields: exact credential field names israeli-bank-scrapers expects.
 * The SpendWise client collects credentials with these keys and seals them;
 * the agent validates the shape before scraping.
 */

export const BANKS = {
  yahav: {
    companyId: 'yahav',
    warmupUrl: 'https://login.yahav.co.il/login/',
    credFields: ['username', 'password', 'nationalID'],
  },
  hapoalim: {
    companyId: 'hapoalim',
    warmupUrl: 'https://login.bankhapoalim.co.il/',
    credFields: ['userCode', 'password'],
  },
  leumi: {
    companyId: 'leumi',
    warmupUrl: 'https://www.leumi.co.il/',
    credFields: ['username', 'password'],
  },
  mizrahi: {
    companyId: 'mizrahi',
    warmupUrl: 'https://www.mizrahi-tefahot.co.il/',
    credFields: ['username', 'password'],
  },
  discount: {
    companyId: 'discount',
    warmupUrl: 'https://start.telebank.co.il/login/#/LOGIN_PAGE',
    credFields: ['id', 'password', 'num'],
  },
  mercantile: {
    companyId: 'mercantile',
    warmupUrl: 'https://start.telebank.co.il/login/#/LOGIN_PAGE',
    credFields: ['id', 'password', 'num'],
  },
  otsar_hahayal: {
    companyId: 'otsarHahayal',
    warmupUrl: 'https://online.bankotsar.co.il/',
    credFields: ['username', 'password'],
  },
  beinleumi: {
    companyId: 'beinleumi',
    warmupUrl: 'https://online.fibi.co.il/',
    credFields: ['username', 'password'],
  },
  massad: {
    companyId: 'massad',
    warmupUrl: 'https://online.bankmassad.co.il/',
    credFields: ['username', 'password'],
  },
  pagi: {
    companyId: 'pagi',
    warmupUrl: 'https://online.pagi.co.il/',
    credFields: ['username', 'password'],
  },
  isracard: {
    companyId: 'isracard',
    warmupUrl: 'https://digital.isracard.co.il/personalarea/Login',
    credFields: ['id', 'card6Digits', 'password'],
  },
  amex: {
    companyId: 'amex',
    warmupUrl: 'https://digital.americanexpress.co.il/personalarea/Login',
    credFields: ['id', 'card6Digits', 'password'],
  },
  visa_cal: {
    companyId: 'visaCal',
    warmupUrl: 'https://www.cal-online.co.il/',
    credFields: ['username', 'password'],
  },
  max: {
    companyId: 'max',
    warmupUrl: 'https://www.max.co.il/homepage/welcome',
    credFields: ['username', 'password'],
  },
};

export function assertKnownBank(source) {
  if (!BANKS[source]) throw new Error(`Unknown bank source: ${source}`);
  return BANKS[source];
}

/** Throws when a decrypted credentials object is missing required fields. */
export function assertCredentialShape(source, credentials) {
  const missing = BANKS[source].credFields.filter((f) => !credentials?.[f]);
  if (missing.length > 0) {
    const error = new Error(`Credentials for ${source} missing fields: ${missing.join(', ')}`);
    error.code = 'CREDENTIALS_INVALID_FORMAT';
    error.terminal = true;
    throw error;
  }
}
