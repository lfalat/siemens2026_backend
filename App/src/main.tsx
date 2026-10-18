import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './app/App.tsx'
import './index.css'

// Entra ID sends every sign-in, token renewal and sign-out back to the registered redirect URI — the site root —
// with `state` in the hash or the query. MSAL v5 expects a "redirect bridge" there: it hands the response to the
// page that started the flow (the hidden iframe's parent, or the /staff page after a redirect) and renders
// nothing itself. The bridge is a separate chunk, so the public area still never loads MSAL (ADR-F004).
// Only the root is the redirect URI: a `state` parameter on any other page belongs to that page.
function isAuthResponse(): boolean {
  const { pathname, hash, search } = window.location
  return (
    pathname === '/' &&
    (new URLSearchParams(hash.slice(1)).has('state') ||
      new URLSearchParams(search).has('state'))
  )
}

if (isAuthResponse()) {
  const { broadcastResponseToMainFrame } =
    await import('@azure/msal-browser/redirect-bridge')
  // A stale or foreign `state` (a reloaded bridge URL, a bookmark) has nothing to hand over: start over.
  await broadcastResponseToMainFrame().catch(() => window.location.replace('/'))
} else {
  const root = document.getElementById('root')
  if (!root) throw new Error('Missing #root element')

  createRoot(root).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}
