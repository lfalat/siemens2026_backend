import { MsalProvider } from '@azure/msal-react'
import { createStaffAuth } from '../../providers/createStaffAuth.ts'
import { StaffAuthContext } from '../../../shared/auth/StaffAuthContext.ts'
import { RequireSignIn } from '../../providers/RequireSignIn.tsx'
import { StaffLayout } from '../../layout/StaffLayout.tsx'

/**
 * Builds the /staff route the first time it is visited: reads /config.json, starts MSAL and finishes a sign-in
 * that the redirect bridge handed over. React Router calls this once per page load.
 */
export async function loadStaffRoute() {
  const auth = await createStaffAuth()

  function StaffRoot() {
    return (
      <MsalProvider instance={auth.instance}>
        <StaffAuthContext value={auth}>
          <RequireSignIn>
            <StaffLayout />
          </RequireSignIn>
        </StaffAuthContext>
      </MsalProvider>
    )
  }

  return { Component: StaffRoot }
}
