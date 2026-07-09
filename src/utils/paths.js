/** Central path definitions — everything resolves from the repo root. */

import path from 'node:path';
import { fileURLToPath } from 'node:url';

// src/utils/ → repo root
export const ROOT_DIR = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const DATA_DIR = path.join(ROOT_DIR, 'scraped-data');
export const PROFILE_DIR = path.join(ROOT_DIR, '.chrome-profile');
export const PRIVATE_KEY_FILE = path.join(ROOT_DIR, 'agent-private.key');
export const DEVICE_TOKEN_FILE = path.join(ROOT_DIR, '.agent-device.json');
export const STATE_FILE = path.join(ROOT_DIR, '.agent-state.json');
export const LOCK_FILE = path.join(ROOT_DIR, '.agent.lock');
