import { ApiError } from '../../shared/api/http.ts'
import { useCurrentStaff } from '../../shared/auth/useCurrentStaff.ts'

export function StaffHomePage() {
  const { data: me, error, isPending } = useCurrentStaff()

  if (isPending) return <main>Loading…</main>

  if (error) {
    const status = error instanceof ApiError ? error.status : undefined
    return (
      <main>
        <p>
          {status === 403
            ? 'You are signed in, but your account has no HealthyHires role.'
            : status === 401
              ? 'The API did not accept your sign-in. Sign out and sign in again.'
              : 'The API could not be reached.'}
        </p>
      </main>
    )
  }

  return (
    <main>
      <h1>Welcome, {me.name ?? me.username}</h1>
      <p>Roles: {me.roles.join(', ') || 'none'}</p>
    </main>
  )
}
