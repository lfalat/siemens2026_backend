import {
  InteractionRequiredAuthError,
  type AuthError,
  type IPublicClientApplication,
} from '@azure/msal-browser'

/** The staff area's MSAL instance and settings, created once when /staff is first opened. */
export interface StaffAuth {
  instance: IPublicClientApplication
  /** The API scope every token is requested for. */
  apiScope: string
  /** Why the sign-in that just returned from Entra failed, e.g. AADSTS50105 for a user with no app role. */
  redirectError: AuthError | null
}

/**
 * An access token for the API, renewed silently when needed. When Entra wants the user back — the 24-hour
 * refresh token has expired, or consent or MFA is required — the browser is redirected and this never resolves.
 */
export async function getAccessToken(auth: StaffAuth): Promise<string> {
  const { instance, apiScope } = auth
  const account = instance.getActiveAccount()
  if (!account) throw new Error('Not signed in')

  const request = { scopes: [apiScope], account }
  try {
    return (await instance.acquireTokenSilent(request)).accessToken
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      await instance.acquireTokenRedirect(request)
      return new Promise<never>(() => {})
    }
    throw error
  }
}
