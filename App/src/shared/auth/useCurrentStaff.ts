import { useQuery } from '@tanstack/react-query'
import { ApiError, request } from '../api/http.ts'
import { getAccessToken } from './staffAuth.ts'
import { useStaffAuth } from './StaffAuthContext.ts'

/** `GET /api/v1/me`. TODO: replace with the generated OpenAPI client (ADR-F005). */
export interface CurrentStaff {
  id: string
  name: string | null
  username: string | null
  roles: string[]
}

/**
 * The signed-in user as the API sees them. A success proves the whole chain: token, audience, scope and an
 * assigned app role. Roles here only shape the UI; the API enforces them.
 */
export function useCurrentStaff() {
  const auth = useStaffAuth()

  return useQuery({
    queryKey: ['me'],
    queryFn: async () =>
      request<CurrentStaff>('/api/v1/me', {
        headers: { Authorization: `Bearer ${await getAccessToken(auth)}` },
      }),
    staleTime: Infinity,
    // 401 and 403 will not change on a retry.
    retry: (failureCount, error) =>
      !(
        error instanceof ApiError &&
        (error.status === 401 || error.status === 403)
      ) && failureCount < 3,
  })
}
