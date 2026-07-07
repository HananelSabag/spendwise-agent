# SpendWise Agent — Secure Local Bank Sync

The local sync companion for **[SpendWise](https://github.com/HananelSabag/SpendWise)**.
It logs into Israeli banks from your own machine and feeds transactions into
SpendWise — with bank credentials **encrypted end-to-end**, so no server can
ever read them.

## Authors & Collaborators

| Name | GitHub | Role |
|------|--------|------|
| **Hananel Sabag** | [@HananelSabag](https://github.com/HananelSabag) | Creator & maintainer |
| **Yuda Sabag** | yudasabag@gmail.com | Collaborator |

> **Portfolio Project** — a security-first bank-sync agent using X25519 sealed
> encryption, a pull-based job queue, and a native Windows tray worker.

## ⚠️ Important Notice — Portfolio Project

Shared for **educational and portfolio purposes**. Credentials, private keys,
and environment files are excluded for security and never committed.

---

## Why run it locally?

Israeli banks sit behind Cloudflare bot protection that blocks datacenter IPs —
cloud scraping simply gets banned, and a login from a foreign server IP trips
the bank's own fraud detection. A residential IP with a real, headful Chrome
passes cleanly. That constraint becomes the security model: **the decryption
key lives only on your machine**, so the cloud can be breached end-to-end
without leaking a single bank password.

## Architecture

```
SpendWise client              SpendWise server (Render)          THIS MACHINE
┌──────────────────┐  seal   ┌───────────────────────┐  claim   ┌────────────────────┐
│ "Connect a bank" │ ──────► │ bank_connections       │ ◄─────── │ agent.js           │
│  encrypts creds  │         │ (ciphertext only —     │          │  1. poll for jobs  │
│  in the browser  │         │  server CANNOT read)   │          │  2. decrypt in RAM │
│  (X25519)        │         │ bank_sync_jobs queue   │  result  │  3. scrape (Chrome)│
└──────────────────┘         │ cron 2×/day            │ ──────►  │  4. report results │
                             └───────────────────────┘          └────────────────────┘
                                                                    ▲ SpendWise Worker
                                                                      (tray, every 30m)
```

- **Outbound HTTPS only.** The agent never listens on a port. No inbound
  connections, no port forwarding, nothing to scan.
- **Nothing sensitive on disk.** Credentials are decrypted per-job in memory
  and dropped immediately. `.env` holds only the API URL and the queue key.
- **Bank-lockout protection at every layer:** the server enqueues ≤2 jobs/day
  per connection, manual sync is capped at 2/day with a 3-hour gap, a
  connection auto-pauses after 3 consecutive failures, and the agent enforces
  its own 3-hour cooldown independently.

## The SpendWise Worker (Windows)

A small native tray app (`worker/`) so you always know sync is running —
no console windows, no taskbar clutter.

- **Start Worker** → runs the agent now and every 30 minutes
- Live status, run counters, and last-sync result
- **Launch on Windows startup** → survives reboots automatically; you never
  have to remember to restart it
- Minimises to the system tray

```powershell
# one-time: add a "SpendWise Worker" shortcut to Desktop + Start Menu
powershell -ExecutionPolicy Bypass -File worker\Install-Worker.ps1
```

Double-click **SpendWise Worker**, hit **Start Worker**, tick **Launch on
Windows startup**. Done — it runs quietly forever.

> Prefer fully headless (runs even when logged out)? Use Task Scheduler:
> ```powershell
> schtasks /Create /TN "SpendWise Agent" /TR "wscript.exe \"%CD%\run-agent-hidden.vbs\"" /SC MINUTE /MO 30 /F
> ```

## Setup

```bash
npm install                 # also applies patches/ via patch-package
npm run keys                # one-time: writes agent-private.key,
                            #           prints the public key for Render
cp .env.example .env        # fill API_URL + BANK_AGENT_KEY
npm run agent               # single run: claim → scrape → report → exit
npm test                    # unit tests (crypto envelope, mapping)
```

Server side (Render env vars): `BANK_AGENT_KEY` (same value as `.env` here) and
`BANK_AGENT_PUBLIC_KEY` (printed by `npm run keys`).
For local development only, `API_URL=http://127.0.0.1:5000/api/v1` is allowed;
non-local agent targets must use HTTPS.

## Modes

| Command | What it does |
|---------|--------------|
| **SpendWise Worker** | Recommended. Tray app — start once, runs forever, survives reboots. |
| `npm run agent` | One-shot: poll the queue, decrypt, scrape, report, exit. |
| `npm run standalone` | Dev/fallback: credentials from `.env`, legacy `/bank-sync` endpoint. |
| `OFFLINE=1 npm run standalone` | Replay the last saved scrape — no browser, no bank contact. |

## Supported banks

| Bank | Source id | Credentials |
|------|-----------|-------------|
| Bank Yahav | `yahav` | username, password, national ID |
| Bank Hapoalim | `hapoalim` | user code, password |
| Bank Leumi | `leumi` | username, password |
| Mizrahi Bank | `mizrahi` | username, password |
| Discount Bank | `discount` | ID, password, identification code |
| Mercantile Bank | `mercantile` | ID, password, identification code |
| Bank Otsar Hahayal | `otsar_hahayal` | username, password |
| Beinleumi | `beinleumi` | username, password |
| Massad | `massad` | username, password |
| Pagi | `pagi` | username, password |
| Isracard | `isracard` | ID, card last-6, password |
| Amex | `amex` | ID, card last-6, password |
| Visa Cal / CAL | `visa_cal` | username, password |
| Max | `max` | username, password |

Multiple accounts under one login are supported — each is tracked separately,
and you can toggle any account's sync on/off from SpendWise. Yahav balance is
recovered by `patches/israeli-bank-scrapers+6.7.8.patch`.

## Security model

- **X25519 sealed envelopes** (tweetnacl): browser-encrypted with this agent's
  public key; format `ephemeralPk(32) ‖ nonce(24) ‖ box`.
- `agent-private.key` is the crown jewel — gitignored, mode 600, exists only on
  this machine. Losing it leaks nothing (users just re-enter credentials);
  leaking it is the only way to decrypt anything.
- Wrong-key, tampered, and truncated envelopes are covered by unit tests.
- Server-side dedup (unique index on `user_id + bank_sync_id`) makes re-runs
  idempotent — a replay can never create duplicate transactions.

## Project structure

```
src/
├── agent.js            production entry — job-queue loop
├── standalone.js       dev entry — .env credentials, OFFLINE replay
├── core/               bank registry, browser lifecycle, scraper, cache
├── crypto/sealed.js    envelope open/seal (mirror of the client util)
├── api/client.js       claim / report / notify — the only network surface
└── utils/              paths, leveled logger, cooldown state, lock
worker/                 SpendWise Worker desktop app (C# WinForms + build scripts)
SpendWise Default Worker/ local ignored bundle for the default hosted worker
tools/                  key generation, offline balance-HTML analyzer
test/                   node --test unit tests
patches/                israeli-bank-scrapers fixes (auto-applied)
```

## License

Private — all rights reserved.
