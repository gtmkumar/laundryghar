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
