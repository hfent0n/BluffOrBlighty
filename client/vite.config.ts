import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In dev the .NET API runs on :5080; Vite proxies the SignalR hub to it.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/hub': { target: 'http://localhost:5080', ws: true, changeOrigin: true },
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
    },
  },
})
