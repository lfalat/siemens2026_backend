import { Link, Outlet } from 'react-router'

export function PublicLayout() {
  return (
    <>
      <header>
        <Link to="/">HealthyHires</Link>
        <Link to="/staff">Staff sign-in</Link>
      </header>
      <Outlet />
    </>
  )
}
