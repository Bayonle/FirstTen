import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

const apiTarget =
  process.env.services__api__https__0 ??
  process.env.services__api__http__0 ??
  'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
      },
      '/hubs': {
        target: apiTarget,
        changeOrigin: true,
        secure: false,
        ws: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    css: true,
  },
})
