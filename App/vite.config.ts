import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv, type ProxyOptions } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // Where the API runs locally: the AppHost's apiservice endpoint, or ApiService started on its own. Read from
  // App/.env or the environment; no VITE_ prefix, so it stays in this config and never reaches the bundle.
  const env = loadEnv(mode, process.cwd(), '')
  const apiUrl = env.API_URL || 'http://localhost:5180'

  // In production Caddy serves the app and the API on one origin (container-deployment.md §1.2). The proxy keeps
  // that true locally: the browser only ever talks to the dev server, so the API needs no CORS policy, and the
  // frontend calls relative paths (/api/v1/...) in every environment.
  const proxy: Record<string, ProxyOptions> = {
    '/api': { target: apiUrl },
    // SignalR hubs (ADR-F008): WebSockets are upgraded through the proxy.
    '/hubs': { target: apiUrl, ws: true },
  }

  return {
    plugins: [react()],
    // Fixed port: http://localhost:5173 is the SPA's registered redirect URI, and Entra accepts no other.
    server: { proxy, port: 5173, strictPort: true },
    // `vite preview` serves the production build, so it gets the same routes.
    preview: { proxy },
  }
})
