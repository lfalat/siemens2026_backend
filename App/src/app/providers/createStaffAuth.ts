import {
  AuthError,
  EventType,
  PublicClientApplication,
  type AuthenticationResult,
} from '@azure/msal-browser'
import type { StaffAuth } from '../../shared/auth/staffAuth.ts'
import { loadConfig } from '../config.ts'

/** Starts MSAL for the staff area (ADR-F006, iam.md §2.2). */
export async function createStaffAuth(): Promise<StaffAuth> {
  const config = await loadConfig()
  const origin = window.location.origin

  const instance = new PublicClientApplication({
    auth: {
      clientId: config.entraClientId,
      authority: `https://login.microsoftonline.com/${config.entraTenantId}`,
      // The site root is the registered redirect URI; main.tsx runs the redirect bridge there.
      redirectUri: origin,
      postLogoutRedirectUri: origin,
    },
    // Closing the tab ends the local session (ADR-F006).
    cache: { cacheLocation: 'sessionStorage' },
  })
  await instance.initialize()

  instance.addEventCallback((event) => {
    // The payload is the whole result; its account is what becomes active.
    if (event.eventType === EventType.LOGIN_SUCCESS) {
      instance.setActiveAccount((event.payload as AuthenticationResult).account)
    }
  })

  // Picks up the response the bridge cached in sessionStorage. MsalProvider calls this again; MSAL returns the
  // same result, so the error can only be caught here.
  let redirectError: AuthError | null = null
  try {
    const result = await instance.handleRedirectPromise({
      navigateToLoginRequestUrl: true,
    })
    if (result?.account) instance.setActiveAccount(result.account)
  } catch (error) {
    if (!(error instanceof AuthError)) throw error
    redirectError = error
  }

  if (!instance.getActiveAccount()) {
    const [account] = instance.getAllAccounts()
    if (account) instance.setActiveAccount(account)
  }

  return { instance, apiScope: config.apiScope, redirectError }
}
