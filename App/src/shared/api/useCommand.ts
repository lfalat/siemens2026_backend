import { useMutation, type UseMutationOptions } from '@tanstack/react-query'
import { useRef } from 'react'
import { ApiError, NetworkError } from './http.ts'

/** How many times a command is re-sent after a failure that a re-send may fix. */
const MAX_RETRIES = 3

/**
 * A POST/PATCH command sent with an `Idempotency-Key` (ADR-A016, docs/api/idempotency/idempotency.md).
 *
 * The key belongs to one user action, not to one HTTP attempt. It is reused while the same variables are sent
 * again, by the automatic retries below or by the user submitting again after an error, and renewed once the command
 * succeeds or the variables change. So when an attempt did reach the API but its answer was lost, the next one
 * gets the original response replayed instead of running the command a second time.
 *
 * @param mutationFn Sends the command, passing `idempotencyKey` on to `request`.
 */
export function useCommand<TData, TVariables>(
  mutationFn: (variables: TVariables, idempotencyKey: string) => Promise<TData>,
  options?: Omit<
    UseMutationOptions<TData, Error, TVariables>,
    'mutationFn' | 'retry'
  >,
) {
  const pending = useRef<{ variables: string; key: string } | null>(null)

  return useMutation({
    ...options,
    mutationFn: (variables) => {
      // Changed variables are a different action: reusing the key would be answered 422.
      const serialized = JSON.stringify(variables)
      if (pending.current?.variables !== serialized) {
        pending.current = { variables: serialized, key: crypto.randomUUID() }
      }
      return mutationFn(variables, pending.current.key)
    },
    onSuccess: (...args) => {
      // Done: submitting the same values again is a new action, e.g. a second, identical job post.
      pending.current = null
      return options?.onSuccess?.(...args)
    },
    // Safe only because of the key: a re-send whose original went through is replayed, not run again.
    retry: (failureCount, error) =>
      failureCount < MAX_RETRIES && isTransient(error),
  })
}

/**
 * A failure a re-send may fix: no answer at all, or the API briefly unavailable (503, or 504 from the proxy). A 502
 * is not retried, because the API also uses it for an upstream that refused the call.
 */
function isTransient(error: Error): boolean {
  if (error instanceof NetworkError) return true
  return (
    error instanceof ApiError && (error.status === 503 || error.status === 504)
  )
}
