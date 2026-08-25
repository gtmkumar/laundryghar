import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

import { reticle } from '@reticlehq/vite-plugin'
// Re-enabled 2026-08-25 on SDK 2.11.0. It was disabled on 2.7.0 for an infinite reload loop
// (reticle_feedback filed 2026-08-15); 2.7.0 was also four minors behind the 2.11.0 daemon, i.e. a
// version skew, which is its own source of undefined behaviour. Dev-only — the plugin self-guards.
// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss(), reticle()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  // Dev-only proxy (multi-vertical local test): the backend hosts have no CORS (the gateway
  // normally fronts them), so we keep the browser same-origin and proxy per-service prefixes to
  // each host. Used with the relative VITE_*_URL values in .env.local.
  server: {
    // PINNED. There was no `port` here, so Vite took its default 5173 and would silently walk to
    // 5174, 5175… when that was busy — while scripts/run-stack.sh advertised (and freed) 5174.
    // A dev server whose address depends on what else happens to be running is a dev server whose
    // OAuth redirect URIs and CORS origins are a guess. strictPort makes a clash fail loudly
    // instead of moving the goalposts.
    port: 5174,
    strictPort: true,
    proxy: {
      '/core':     { target: 'http://localhost:5056', changeOrigin: true, rewrite: (p) => p.replace(/^\/core/, '') },
      '/ops':      { target: 'http://localhost:5015', changeOrigin: true, rewrite: (p) => p.replace(/^\/ops/, '') },
      '/commerce': { target: 'http://localhost:5242', changeOrigin: true, rewrite: (p) => p.replace(/^\/commerce/, '') },
    },
  },
  build: {
    rolldownOptions: {
      output: {
        advancedChunks: {
          groups: [
            // Long-cached vendor chunk: React core changes far less often
            // than app code, so split it from the main bundle.
            {
              name: 'react-vendor',
              test: /node_modules[\\/](react|react-dom|scheduler|react-router)/,
            },
          ],
        },
      },
    },
  },
})
