/**
 * Asymmetric credential envelope (tweetnacl X25519).
 *
 * Wire format (single base64 string):
 *   ephemeralPublicKey(32) || nonce(24) || nacl.box ciphertext
 *
 * The SpendWise client seals bank credentials in the user's browser with
 * this agent's public key; the server stores only the envelope. Only the
 * private key on THIS machine can open it.
 *
 * Browser mirror: SpendWise/client/src/utils/sealedBox.js — keep in sync.
 */

import nacl from 'tweetnacl';
import util from 'tweetnacl-util';

const EPK_LEN = nacl.box.publicKeyLength;   // 32
const NONCE_LEN = nacl.box.nonceLength;     // 24

/** Encrypt an object for a recipient public key (base64). Used by tests. */
export function seal(obj, recipientPublicKeyB64) {
  const recipientPk = util.decodeBase64(recipientPublicKeyB64);
  const eph = nacl.box.keyPair();
  const nonce = nacl.randomBytes(NONCE_LEN);
  const message = util.decodeUTF8(JSON.stringify(obj));
  const box = nacl.box(message, nonce, recipientPk, eph.secretKey);

  const envelope = new Uint8Array(EPK_LEN + NONCE_LEN + box.length);
  envelope.set(eph.publicKey, 0);
  envelope.set(nonce, EPK_LEN);
  envelope.set(box, EPK_LEN + NONCE_LEN);
  eph.secretKey.fill(0);
  return util.encodeBase64(envelope);
}

/** Decrypt an envelope (base64) with our private key (base64). */
export function open(envelopeB64, privateKeyB64) {
  const envelope = util.decodeBase64(envelopeB64);
  if (envelope.length < EPK_LEN + NONCE_LEN + nacl.box.overheadLength) {
    throw new Error('Envelope too short');
  }
  const ephPk = envelope.slice(0, EPK_LEN);
  const nonce = envelope.slice(EPK_LEN, EPK_LEN + NONCE_LEN);
  const box = envelope.slice(EPK_LEN + NONCE_LEN);

  const opened = nacl.box.open(box, nonce, ephPk, util.decodeBase64(privateKeyB64));
  if (!opened) throw new Error('Decryption failed — wrong key or corrupted envelope');
  return JSON.parse(util.encodeUTF8(opened));
}
