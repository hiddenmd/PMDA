/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const apiUrl = process.env.PMDA_API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    // Same origin for the browser: no CORS in development.
    proxy: {
      '/api': { target: apiUrl, changeOrigin: true },
      '/hubs': { target: apiUrl, changeOrigin: true, ws: true },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
  },
})
