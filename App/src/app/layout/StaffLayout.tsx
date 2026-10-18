import { useMsal } from '@azure/msal-react'
import { Link, Outlet } from 'react-router'

export function StaffLayout() {
  const { instance } = useMsal()
  const account = instance.getActiveAccount()

  return (
    <>
      <header>
        <Link to="/staff">HealthyHires — staff</Link>
        <span>{account?.name ?? account?.username}</span>
        <button
          type="button"
          // Clears the tokens in this tab and ends the Entra session in this browser (ADR-F006).
          onClick={() => void instance.logoutRedirect({ account })}
        >
          Sign out
        </button>
      </header>
      <Outlet />
    </>
  )
}
