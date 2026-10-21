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

/** The request got no answer at all: offline, the connection dropped, or the server never responded. */
export class NetworkError extends Error {
  constructor(cause: unknown) {
    super('The API could not be reached', { cause })
    this.name = 'NetworkError'
  }
}

/** `fetch`'s options, plus the idempotency key of a command. */
export interface RequestOptions extends RequestInit {
  /**
   * Sent as `Idempotency-Key`, which the API requires on every POST/PATCH command (ADR-A016). Every attempt of one
   * user action must carry the same key; `useCommand` manages it.
   */
  idempotencyKey?: string
}

/** Calls the API on a relative path and parses the JSON answer. Stopgap until the generated client (ADR-F005). */
export async function request<T>(
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const { idempotencyKey, ...init } = options
  const headers = new Headers(init.headers)
  if (idempotencyKey) headers.set('Idempotency-Key', idempotencyKey)

  let response: Response
  try {
    response = await fetch(path, { ...init, headers })
  } catch (error) {
    // A cancelled request is the caller's doing, not a failure worth retrying.
    if (init.signal?.aborted) throw error
    throw new NetworkError(error)
  }

  if (!response.ok) {
    throw new ApiError(response.status, await response.json().catch(() => null))
  }
  // A command without a result answers 204, with no body to parse.
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}
