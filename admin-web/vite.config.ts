import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  // Dev-only proxy (multi-vertical local test): the backend hosts have no CORS (the gateway
  // normally fronts them), so we keep the browser same-origin and proxy per-service prefixes to
  // each host. Used with the relative VITE_*_URL values in .env.local.
  server: {
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
