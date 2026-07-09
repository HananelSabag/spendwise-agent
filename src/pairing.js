/**
 * Device pairing — turns this machine into a user's OWN sync agent instead
 * of the shared Default Host.
 *
 *   node src/pairing.js <CODE>
 *
 * The user gets an 8-character code from the SpendWise website (Bank Sync
 * → "My own computer" → generates a code, 10-minute expiry) and enters it
 * here (or via the Windows Worker's pairing screen, which shells out to
 * this same script). On success this machine gets its own X25519 keypair
 * (same primitive as tools/generate-keys.js) and a device token; the server
 * then scopes every future job-claim to this user only — see
 * SpendWise/server/routes/agentPairingRoutes.js and bankAgentRoutes.js.
 *
 * Refuses to run if this machine is already paired/keyed — pairing again
 * would silently orphan whatever credentials are already sealed to the
 * existing key ("better lost than stolen", same as generate-keys.js).
 */

import dotenv from 'dotenv';
dotenv.config({ override: true });

import nacl from 'tweetnacl';
import util from 'tweetnacl-util';
import fs from 'node:fs';
import os from 'node:os';
import { pathToFileURL } from 'node:url';
import { PRIVATE_KEY_FILE, DEVICE_TOKEN_FILE } from './utils/paths.js';

function apiBase() {
  const url = process.env.API_URL;
  if (!url) throw new Error('API_URL is not set');
  return url.replace(/\/+$/, '');
}

/** Pair this machine to a SpendWise account. Returns { label }. */
export async function pair(code, label = os.hostname()) {
  if (typeof code !== 'string' || !/^[A-Z0-9]{8}$/i.test(code)) {
    throw new Error('Pairing code must be 8 letters/numbers');
  }
  if (fs.existsSync(PRIVATE_KEY_FILE) || fs.existsSync(DEVICE_TOKEN_FILE)) {
    throw new Error('This machine already has a key — unpair on the website first if you meant to re-pair');
  }

  const keyPair = nacl.box.keyPair();
  const publicKey = util.encodeBase64(keyPair.publicKey);

  const res = await fetch(`${apiBase()}/agent-pairing/confirm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code: code.toUpperCase(), public_key: publicKey, label }),
  });
  const text = await res.text();
  let payload;
  try { payload = JSON.parse(text); } catch { payload = { raw: text }; }
  if (!res.ok) {
    // Route handlers here return { error: "string" }; the app's global
    // fallback (unmatched routes, unexpected crashes) instead returns
    // { error: { code, message } } — handle both shapes rather than
    // stringifying an object into a useless "[object Object]".
    const message =
      typeof payload.error === 'string' ? payload.error
      : payload.error?.message ? payload.error.message
      : `Pairing failed (HTTP ${res.status})`;
    throw new Error(message);
  }

  // Write the key first — losing the device token after this point still
  // leaves a usable (if inert) key on disk, not a half-paired machine with
  // neither.
  fs.writeFileSync(PRIVATE_KEY_FILE, util.encodeBase64(keyPair.secretKey), { mode: 0o600 });
  fs.writeFileSync(
    DEVICE_TOKEN_FILE,
    JSON.stringify({ deviceToken: payload.device_token, label, pairedAt: new Date().toISOString() }, null, 2),
    { mode: 0o600 },
  );

  return { label };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const code = process.argv[2];
  pair(code)
    .then((result) => {
      console.log(JSON.stringify({ ok: true, ...result }));
      // Not process.exit(0) — forcing an immediate exit right after an async
      // network call has completed can race libuv's handle cleanup on
      // Windows (crashes with "Assertion failed: !(handle->flags &
      // UV_HANDLE_CLOSING)"). Setting exitCode and letting the event loop
      // drain naturally avoids it.
      process.exitCode = 0;
    })
    .catch((err) => {
      console.log(JSON.stringify({ ok: false, error: err.message }));
      process.exitCode = 1;
    });
}
