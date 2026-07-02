# spendwise-agent

The local sync agent for [SpendWise](https://github.com/HananelSabag/SpendWise) —
scrapes Israeli banks and feeds transactions into SpendWise, with bank
credentials **end-to-end encrypted** so no server ever sees them.

## Authors

| Name | Role |
|---|---|
| **Hananel Sabag** | Creator & maintainer |
| **Yuda Sabag** | Collaborator |

## Why an agent on a home machine?

Israeli banks sit behind Cloudflare bot protection that blocks datacenter
IPs — cloud scraping simply gets banned. A residential IP with a real,
headful Chrome and a persistent profile passes cleanly. The agent turns
that constraint into the security model: the decryption key lives only
here, so the cloud can be breached end-to-end without leaking a single
bank password.

## Architecture

```
SpendWise client               SpendWise server                THIS MACHINE
┌────────────────────┐         ┌─────────────────────┐         ┌──────────────────────┐
│ "Connect a Bank"   │ encrypt │ bank_connections     │  claim  │ agent.js             │
│ wizard — seals     │ ──────► │ (ciphertext only —   │ ◄────── │  1. poll jobs        │
│ credentials in the │         │  server CANNOT read) │         │  2. decrypt in memory│
│ browser (X25519)   │         │ bank_sync_jobs queue │  result │  3. scrape (Chrome)  │
└────────────────────┘         │ cron 2×/day          │ ──────► │  4. report accounts  │
                               └─────────────────────┘         └──────────────────────┘
                                                                   ▲ Task Scheduler
                                                                     every 30 min
```

- **Outbound HTTPS only.** The agent never listens on any port. No inbound
  connections, no port forwarding, nothing to scan.
- **Nothing sensitive on disk.** Credentials are decrypted per-job in
  memory and dropped immediately. The `.env` holds only the API URL and
  the queue key.
- **Bank-lockout protection at every layer**: server enqueues ≤2 jobs/day
  per connection, manual sync capped 2/day + 3h gap, auto-pause after 3
  consecutive failures, and the agent enforces its own 3h local cooldown.

## Setup

```bash
npm install                 # also applies patches/ via patch-package
npm run keys                # one-time: writes agent-private.key,
                            # prints the public key for Render
cp .env.example .env        # fill API_URL + BANK_AGENT_KEY
npm run agent               # single run: claim → scrape → report → exit
npm test                    # unit tests (crypto envelope, mapping)
```

Server side (Render env vars): `BANK_AGENT_KEY` (same value as .env here)
and `BANK_AGENT_PUBLIC_KEY` (printed by `npm run keys`).

### Windows Task Scheduler

Create a basic task, every 30 minutes, action:
`wscript.exe C:\path\to\spendwise-agent\run-agent-hidden.vbs`
The agent exits in ~2s when there are no jobs.

## Modes

| Command | What it does |
|---|---|
| `npm run agent` | **Production.** Polls the SpendWise job queue, decrypts per-job credentials, scrapes, reports. |
| `npm run standalone` | Dev/fallback. Credentials from `.env`, POSTs to the legacy `/bank-sync` endpoint. |
| `OFFLINE=1 npm run standalone` | Replays the last saved scrape from `scraped-data/` — no browser, no bank contact. |

## Supported banks

| Bank | Source id | Credentials |
|---|---|---|
| Bank Yahav | `yahav` | username, password, national ID |
| Isracard | `isracard` | ID, card last-6, password |
| Max | `max` | username, password |
| Discount | `discount` | ID, password, identification code |

Yahav account balance is extracted by
`patches/israeli-bank-scrapers+6.7.8.patch` — the upstream library returns
transactions only; the patch reads the balance from the portal home page
before navigating away, and snapshots the HTML to `scraped-data/` so
selectors can be fixed offline (`node tools/analyze-balance-html.js`)
without re-hitting the bank.

## Security model

- **X25519 sealed envelopes** (tweetnacl): browser-encrypted with this
  agent's public key; format `ephemeralPk(32) ‖ nonce(24) ‖ box`.
- `agent-private.key` is the crown jewel — gitignored, mode 600, exists
  only on this machine. Losing it leaks nothing (users re-enter
  credentials); leaking it is the only way to decrypt anything.
- Envelope decryption failures, tampering, and wrong keys are covered by
  unit tests (`test/sealed.test.js`).
- Dedup on the server (unique index on `user_id + bank_sync_id`) makes
  re-runs idempotent — replays can never create duplicate transactions.

## Project structure

```
src/
├── agent.js            production entry — job queue loop
├── standalone.js       dev entry — .env credentials, OFFLINE replay
├── core/
│   ├── banks.js        bank registry + credential shape validation
│   ├── browser.js      Chrome lifecycle (withBrowser = guaranteed cleanup)
│   ├── scraper.js      scrape with retry-once, strict txn mapping
│   └── cache.js        raw-data persistence for OFFLINE replay
├── crypto/sealed.js    envelope open/seal (mirror of the client util)
├── api/client.js       claim / report / notify — the only network surface
└── utils/              paths, leveled logger, cooldown state, lock
tools/                  generate-keys, analyze-balance-html
test/                   node --test unit tests
patches/                israeli-bank-scrapers fixes (auto-applied)
```

## License

Private — all rights reserved.
