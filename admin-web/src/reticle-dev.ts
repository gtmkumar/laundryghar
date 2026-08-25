// Dev-only. Imported automatically by @reticlehq/vite-plugin — you do not need to import it.
// Self-guards on import.meta.env.DEV, so it is a no-op in a production build.
import { registerCapabilities, registerStore } from '@reticlehq/react';
import { useAuthStore } from '@/stores/authStore';

if (import.meta.env.DEV) {
  registerStore('auth', useAuthStore);

  registerCapabilities({
    testids: [], // none found — add data-testid to your key elements
    signals: [], // names you pass to reticle.signal()
    stores: ['auth'],
  });
}
