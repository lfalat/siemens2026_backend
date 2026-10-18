import { InteractionStatus } from '@azure/msal-browser'
import { useMsal } from '@azure/msal-react'
import { useEffect, type ReactNode } from 'react'
import { useStaffAuth } from '../../shared/auth/StaffAuthContext.ts'
import { SignInError } from './SignInError.tsx'

/**
 * Sends a signed-out visitor to Entra ID and renders the staff area once they are back. Only UX: the API checks
 * the token and the role on every call (ADR-F004).
 */
export function RequireSignIn({ children }: { children: ReactNode }) {
  const { instance, inProgress, accounts } = useMsal()
  const { apiScope, redirectError } = useStaffAuth()
  const signedIn = accounts.length > 0

  // Not after a failed sign-in: Entra would refuse again (e.g. AADSTS50105) and the page would loop.
  const shouldSignIn =
    !signedIn && !redirectError && inProgress === InteractionStatus.None

  useEffect(() => {
    if (shouldSignIn) void instance.loginRedirect({ scopes: [apiScope] })
  }, [shouldSignIn, instance, apiScope])

  if (redirectError && !signedIn) return <SignInError error={redirectError} />
  if (!signedIn) return <p>Signing in…</p>
  return children
}
