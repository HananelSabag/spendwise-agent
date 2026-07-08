/**
 * SpendWise API client — the agent's only network surface.
 * Outbound HTTPS only; the agent never listens on any port.
 */

import { logger } from '../utils/log.js';

const log = logger('api');

function base() {
  const url = process.env.API_URL;
  if (!url) throw new Error('API_URL is not set');
  const parsed = new URL(url);
  const isLoopback = ['localhost', '127.0.0.1', '::1'].includes(parsed.hostname);
  if (parsed.protocol !== 'https:' && !(parsed.protocol === 'http:' && isLoopback)) {
    throw new Error('API_URL must use https:// unless it points to localhost/127.0.0.1');
  }
  return url.replace(/\/+$/, '');
}

function agentKey() {
  const key = process.env.BANK_AGENT_KEY;
  if (!key) throw new Error('BANK_AGENT_KEY is not set');
  return key;
}

async function request(method, pathName, body) {
  const res = await fetch(`${base()}${pathName}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      'X-Agent-Key': agentKey(),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  if (res.status === 401) {
    throw new Error('Server rejected X-Agent-Key — check BANK_AGENT_KEY matches Render');
  }
  if (!res.ok) {
    throw new Error(`${method} ${pathName} → HTTP ${res.status}: ${text.slice(0, 300)}`);
  }
  try { return JSON.parse(text); } catch { return { raw: text }; }
}

/** Atomically claim up to `limit` pending sync jobs. */
export async function claimJobs(limit = 5) {
  const { jobs = [] } = await request('POST', '/bank-agent/jobs/claim', { limit });
  if (jobs.length > 0) log.info(`received ${jobs.length} sync request(s)`);
  return jobs;
}

/** Report a successful scrape (accounts payload) for a job. */
export function reportSuccess(jobId, accounts) {
  return request('POST', `/bank-agent/jobs/${jobId}/result`, { success: true, accounts });
}

/**
 * Report a failed scrape for a job.
 * `transient: true` marks expected, self-resolving declines (e.g. the local
 * scrape cooldown) — the server records the job as failed but does NOT count
 * it toward the connection's consecutive_failures / auto-pause threshold.
 */
export function reportFailure(jobId, error, { transient = false } = {}) {
  return request('POST', `/bank-agent/jobs/${jobId}/result`, {
    success: false,
    error: String(error).slice(0, 500),
    transient,
  });
}

/** Optional push notification via ntfy (best-effort, never throws). */
export async function notify(message) {
  const { NTFY_URL, NTFY_TOPIC } = process.env;
  if (!NTFY_URL || !NTFY_TOPIC) return;
  try {
    await fetch(`${NTFY_URL.replace(/\/+$/, '')}/${NTFY_TOPIC}`, {
      method: 'POST',
      headers: { Title: 'spendwise-agent' },
      body: message,
    });
  } catch { /* notifications are best-effort */ }
}
