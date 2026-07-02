import { test } from 'node:test';
import assert from 'node:assert/strict';
import nacl from 'tweetnacl';
import util from 'tweetnacl-util';
import { seal, open } from '../src/crypto/sealed.js';

function keypair() {
  const pair = nacl.box.keyPair();
  return {
    pub: util.encodeBase64(pair.publicKey),
    priv: util.encodeBase64(pair.secretKey),
  };
}

test('roundtrip: seal then open returns the original object', () => {
  const { pub, priv } = keypair();
  const creds = { username: 'user1', password: 'p@ss!×עברית', nationalID: '123456789' };
  const envelope = seal(creds, pub);
  assert.deepEqual(open(envelope, priv), creds);
});

test('envelope is base64 and within server size limits', () => {
  const { pub } = keypair();
  const envelope = seal({ username: 'u', password: 'p' }, pub);
  assert.match(envelope, /^[A-Za-z0-9+/=]+$/);
  assert.ok(envelope.length >= 32 && envelope.length <= 4096);
});

test('wrong private key is rejected', () => {
  const alice = keypair();
  const mallory = keypair();
  const envelope = seal({ secret: 'x' }, alice.pub);
  assert.throws(() => open(envelope, mallory.priv), /Decryption failed/);
});

test('tampered envelope is rejected', () => {
  const { pub, priv } = keypair();
  const envelope = seal({ secret: 'x' }, pub);
  const tampered = envelope.slice(0, -8) + 'AAAAAAA=';
  assert.throws(() => open(tampered, priv));
});

test('truncated envelope is rejected', () => {
  const { priv } = keypair();
  assert.throws(() => open('QUJD', priv), /too short/i);
});

test('each seal produces a unique envelope (fresh ephemeral key + nonce)', () => {
  const { pub } = keypair();
  const creds = { username: 'same', password: 'same' };
  assert.notEqual(seal(creds, pub), seal(creds, pub));
});
