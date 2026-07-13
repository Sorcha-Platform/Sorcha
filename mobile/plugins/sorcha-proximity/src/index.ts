import { registerPlugin } from '@capacitor/core';

import type { SorchaProximityPlugin } from './definitions';

/**
 * The BLE proximity transport.
 *
 * `web` deliberately resolves to the unsupported stub rather than throwing: the wallet is a PWA that really
 * does run in a plain browser (capacitor.config.json uses `server.url`), and the capability probe is how the
 * UI decides to hide in-person sharing rather than offer something broken.
 */
const SorchaProximity = registerPlugin<SorchaProximityPlugin>('SorchaProximity', {
  web: () => import('./web').then((m) => new m.SorchaProximityWeb()),
});

export * from './definitions';
export { SorchaProximity };
