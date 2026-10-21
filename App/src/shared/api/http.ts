/** A non-2xx answer from the API, with its RFC 9457 problem details when it sent them. */
export class ApiError extends Error {
  readonly status: number
  readonly problem: unknown

  constructor(status: number, problem: unknown) {
    super(`API answered ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

/** Calls the API on a relative path and parses the JSON answer. Stopgap until the generated client (ADR-F005). */
export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init)
  if (!response.ok) {
    throw new ApiError(response.status, await response.json().catch(() => null))
  }
  return (await response.json()) as T
}
