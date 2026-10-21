import { ApiError, NetworkError } from './http.ts'

/** The parts of the API's RFC 9457 problem details the UI shows (docs/api/errors/errors.md). */
interface Problem {
  detail?: string
  errors?: Record<string, string[]>
}

/** The message to show for a failed call: the API's own Slovak `detail` when it sent one. */
export function problemMessage(error: Error): string {
  if (error instanceof ApiError) {
    const detail = (error.problem as Problem | null)?.detail
    if (detail) return detail
  }
  // A gateway error with no problem body comes from the proxy in front of the API (Caddy, or Vite's in development),
  // not from the API itself: the API is down or restarting.
  if (
    error instanceof NetworkError ||
    (error instanceof ApiError && [502, 503, 504].includes(error.status))
  ) {
    return 'Server je nedostupný. Skontrolujte pripojenie a skúste to znova.'
  }
  return 'Niečo sa pokazilo. Skúste to znova.'
}

/**
 * The API's validation messages, one per field. The API keys them by the command's property (`OpenPositions`);
 * they are returned under the request body's camelCase name (`openPositions`), which is what a form registers.
 */
export function problemFieldErrors(error: Error): Record<string, string> {
  if (!(error instanceof ApiError)) return {}
  const errors = (error.problem as Problem | null)?.errors ?? {}
  return Object.fromEntries(
    Object.entries(errors)
      .filter(([, messages]) => messages.length > 0)
      .map(([field, messages]) => [
        field.charAt(0).toLowerCase() + field.slice(1),
        messages[0],
      ]),
  )
}
