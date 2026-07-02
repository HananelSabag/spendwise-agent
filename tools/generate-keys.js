/**
 * One-time X25519 keypair generation.
 *
 *   npm run keys
 *
 * Writes agent-private.key (gitignored — NEVER share) and prints the
 * public key for the Render env var BANK_AGENT_PUBLIC_KEY.
 *
 * Losing the private key leaks nothing, but every user must re-enter
 * their bank credentials ("better lost than stolen").
 */

import nacl from 'tweetnacl';
import util from 'tweetnacl-util';
import fs from 'node:fs';
import { PRIVATE_KEY_FILE } from '../src/utils/paths.js';

if (fs.existsSync(PRIVATE_KEY_FILE)) {
  console.error('agent-private.key already exists — refusing to overwrite.');
  console.error('A new keypair invalidates every stored credential; delete the');
  console.error('file manually first if you really mean it.');
  process.exit(1);
}

const pair = nacl.box.keyPair();
fs.writeFileSync(PRIVATE_KEY_FILE, util.encodeBase64(pair.secretKey), { mode: 0o600 });

console.log('✔ Private key written to agent-private.key (KEEP SECRET, gitignored)');
console.log('');
console.log('Public key — paste into Render env var BANK_AGENT_PUBLIC_KEY:');
console.log('');
console.log(util.encodeBase64(pair.publicKey));
