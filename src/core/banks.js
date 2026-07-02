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
  isracard: {
    companyId: 'isracard',
    warmupUrl: 'https://digital.isracard.co.il/personalarea/Login',
    credFields: ['id', 'card6Digits', 'password'],
  },
  max: {
    companyId: 'max',
    warmupUrl: 'https://www.max.co.il/homepage/welcome',
    credFields: ['username', 'password'],
  },
  discount: {
    companyId: 'discount',
    warmupUrl: 'https://start.telebank.co.il/login/#/LOGIN_PAGE',
    credFields: ['id', 'password', 'num'],
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
    throw new Error(`Credentials for ${source} missing fields: ${missing.join(', ')}`);
  }
}
