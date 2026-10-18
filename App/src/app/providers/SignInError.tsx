import { ServerError, type AuthError } from '@azure/msal-browser'
import { useMsal } from '@azure/msal-react'
import { useStaffAuth } from '../../shared/auth/StaffAuthContext.ts'

/** Shown when Entra ID refused the sign-in. MSAL v5 messages are not readable, so only the codes are used. */
export function SignInError({ error }: { error: AuthError }) {
  const { instance } = useMsal()
  const { apiScope } = useStaffAuth()

  // AADSTS50105: the user is not assigned to the HealthyHires enterprise application (iam.md §2.1).
  const notAssigned = error instanceof ServerError && error.errorNo === '50105'

  return (
    <main>
      <h1>Sign-in failed</h1>
      <p>
        {notAssigned
          ? 'Your account has no access to HealthyHires. Ask an administrator to assign you a role.'
          : `Entra ID refused the sign-in (${error.errorCode}).`}
      </p>
      {error.correlationId && <p>Correlation id: {error.correlationId}</p>}
      <button
        type="button"
        onClick={() =>
          // select_account, so a user who picked the wrong account can choose another one.
          void instance.loginRedirect({
            scopes: [apiScope],
            prompt: 'select_account',
          })
        }
      >
        Try another account
      </button>
    </main>
  )
}
