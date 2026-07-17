/** Refresh safe display identity for an already-paired personal Worker. */

import dotenv from 'dotenv';
dotenv.config({ override: true });

import fs from 'node:fs';
import { pathToFileURL } from 'node:url';
import { fetchDeviceProfile } from './api/client.js';
import { DEVICE_TOKEN_FILE } from './utils/paths.js';

export async function refreshDeviceProfile() {
  if (!fs.existsSync(DEVICE_TOKEN_FILE)) return { ok: false, reason: 'not-paired' };

  const current = JSON.parse(fs.readFileSync(DEVICE_TOKEN_FILE, 'utf8'));
  if (!current.deviceToken) return { ok: false, reason: 'not-paired' };

  const profile = await fetchDeviceProfile();
  const next = {
    ...current,
    label: typeof profile.label === 'string' ? profile.label.trim() : current.label,
    ownerName: typeof profile.owner_name === 'string' ? profile.owner_name.trim() : current.ownerName || '',
    language: profile.language === 'he' ? 'he' : profile.language === 'en' ? 'en' : current.language || '',
    profileUpdatedAt: new Date().toISOString(),
  };
  fs.writeFileSync(DEVICE_TOKEN_FILE, JSON.stringify(next, null, 2), { mode: 0o600 });
  return { ok: true, ownerName: next.ownerName, language: next.language };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  refreshDeviceProfile()
    .then((result) => {
      console.log(JSON.stringify(result));
      process.exitCode = result.ok ? 0 : 2;
    })
    .catch((err) => {
      console.log(JSON.stringify({ ok: false, error: err.message }));
      process.exitCode = 1;
    });
}
