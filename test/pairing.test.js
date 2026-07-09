import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pair } from '../src/pairing.js';

// pair() touches this machine's real agent-private.key (dev machines keep a
// live Default Host key at the repo root) and makes a live network call once
// past its guards — neither is safe to exercise from a unit test. Only the
// synchronous, side-effect-free format check is covered here; the pairing
// round trip itself is covered by the manual verification pass against a
// running server (see the pairing feature's plan).

test('pair() rejects a malformed code before touching disk or network', async () => {
  await assert.rejects(() => pair('short'), /8 letters\/numbers/);
  await assert.rejects(() => pair('has spaces'), /8 letters\/numbers/);
  await assert.rejects(() => pair(''), /8 letters\/numbers/);
  await assert.rejects(() => pair(undefined), /8 letters\/numbers/);
});
