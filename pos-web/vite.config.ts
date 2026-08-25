import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

export default defineConfig({
  // PINNED, same reasoning as admin-web: with no `port` Vite takes 5173 and silently walks upward
  // when it is busy, so which app answers which port depends on start order. 5175 keeps it clear of
  // admin-web (5174) and Vite's default. strictPort turns a clash into an error instead of a move.
  server: {
    port: 5175,
    strictPort: true,
    // Dev-only proxy, mirroring admin-web. pos-web's .env pointed every service at the AppHost
    // gateway on :8080 — a port another project's container owns on this machine, so every POS
    // call was being answered by an unrelated uvicorn service with 404. Proxying to the standalone
    // hosts keeps the browser same-origin (these hosts ship no CORS) and needs no gateway at all.
    proxy: {
      '/core':     { target: 'http://localhost:5056', changeOrigin: true, rewrite: (p) => p.replace(/^\/core/, '') },
      '/ops':      { target: 'http://localhost:5015', changeOrigin: true, rewrite: (p) => p.replace(/^\/ops/, '') },
      '/commerce': { target: 'http://localhost:5242', changeOrigin: true, rewrite: (p) => p.replace(/^\/commerce/, '') },
    },
  },
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
})
