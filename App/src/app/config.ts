/**
 * Environment-specific settings, read at startup from /config.json (ADR-F013). Caddy generates the file in
 * production; locally it is App/public/config.json, copied from App/config.example.json. None of it is secret.
 */
export interface AppConfig {
  /** The company tenant. */
  entraTenantId: string
  /** The Application (client) id of the HealthyHires **SPA** registration. */
  entraClientId: string
  /** The API's delegated scope, `api://<api-client-id>/access_as_user`. */
  apiScope: string
}

export async function loadConfig(): Promise<AppConfig> {
  const response = await fetch('/config.json', { cache: 'no-store' })
  if (!response.ok) {
    throw new Error(`/config.json answered ${response.status}`)
  }

  // The dev server answers index.html for a missing file, so a parse failure usually means it is missing.
  const config = (await response.json().catch(() => {
    throw new Error(
      '/config.json is missing — copy App/config.example.json to App/public/config.json',
    )
  })) as Partial<AppConfig>

  // Caught here rather than as an unreadable error on Entra's sign-in page.
  const invalid = [
    !isGuid(config.entraTenantId) && 'entraTenantId',
    !isGuid(config.entraClientId) && 'entraClientId',
    !config.apiScope?.startsWith('api://') && 'apiScope',
  ].filter(Boolean)
  if (invalid.length > 0) {
    throw new Error(`/config.json has no valid ${invalid.join(', ')}`)
  }

  return config as AppConfig
}

function isGuid(value: string | undefined): boolean {
  return /^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(value ?? '')
}
