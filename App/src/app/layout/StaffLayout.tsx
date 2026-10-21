import { useMsal } from '@azure/msal-react'
import { Button, makeStyles, Text } from '@fluentui/react-components'
import {
  BriefcaseRegular,
  HomeRegular,
  PeopleRegular,
  PersonCircleRegular,
  SignOutRegular,
} from '@fluentui/react-icons'
import { Link, NavLink, Outlet } from 'react-router'
import { useCurrentStaff } from '../../shared/auth/useCurrentStaff.ts'

const useStyles = makeStyles({
  root: {
    minHeight: '100vh',
    backgroundColor: '#F1F3F5',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    minHeight: '72px',
    boxSizing: 'border-box',
    padding: '12px 32px',
    backgroundColor: '#1F2933',
  },
  body: {
    display: 'flex',
    minHeight: 'calc(100vh - 72px)',
  },
  sidebar: {
    position: 'fixed',
    top: '72px',
    bottom: 0,
    left: 0,
    width: '260px',
    boxSizing: 'border-box',
    padding: '28px 16px',
    backgroundColor: '#1F2933',
    overflowY: 'auto',
  },
  navigation: {
    display: 'grid',
    gap: '8px',
  },
  navigationLink: {
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    minHeight: '44px',
    boxSizing: 'border-box',
    padding: '0 16px',
    borderRadius: '6px',
    color: '#9CA3AF',
    fontSize: '15px',
    fontWeight: 600,
    textDecoration: 'none',
    ':hover': {
      backgroundColor: '#475569',
      color: '#FFFFFF',
    },
  },
  navigationIcon: {
    color: '#9CA3AF',
    fontSize: '20px',
  },
  activeNavigationLink: {
    backgroundColor: '#475569',
    color: '#FFFFFF',
  },
  activeNavigationIcon: {
    color: '#F59E0B',
  },
  content: {
    flex: 1,
    minWidth: 0,
    marginLeft: '260px',
  },
  logo: {
    display: 'block',
    width: 'auto',
    height: 'auto',
    objectFit: 'contain',
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: '20px',
    marginLeft: 'auto',
  },
  role: {
    color: '#FFFFFF',
    fontSize: '14px',
    fontWeight: 600,
  },
  signOut: {
    backgroundColor: '#475569',
    color: '#FFFFFF',
    border: '1px solid #475569',
    ':hover': {
      backgroundColor: '#1F2933',
      color: '#FFFFFF',
    },
  },
})

export function StaffLayout() {
  const styles = useStyles()
  const { instance } = useMsal()
  const { data: me } = useCurrentStaff()
  const account = instance.getActiveAccount()
  const role = me?.roles[0] ?? 'Načítava sa…'

  return (
    <div className={styles.root}>
      <header className={styles.header}>
        <Link to="/staff" aria-label="Healthy Hires">
          <img
            className={styles.logo}
            src="/hh_logo_small.png"
            alt="Healthy Hires"
          />
        </Link>
        <div className={styles.actions}>
          <Text className={styles.role}>
            <PersonCircleRegular aria-hidden="true" /> {role}
          </Text>
          <Button
            className={styles.signOut}
            icon={<SignOutRegular />}
            // Clears the tokens in this tab and ends the Entra session in this browser (ADR-F006).
            onClick={() => void instance.logoutRedirect({ account })}
          >
            Odhlásiť sa
          </Button>
        </div>
      </header>
      <div className={styles.body}>
        <aside className={styles.sidebar}>
          <nav className={styles.navigation} aria-label="Hlavná navigácia">
            <NavLink
              to="/staff"
              end
              className={({ isActive }) =>
                `${styles.navigationLink} ${
                  isActive ? styles.activeNavigationLink : ''
                }`
              }
            >
              {({ isActive }) => (
                <>
                  <HomeRegular
                    className={`${styles.navigationIcon} ${
                      isActive ? styles.activeNavigationIcon : ''
                    }`}
                    style={{
                      color: isActive ? '#F59E0B' : '#9CA3AF',
                    }}
                    aria-hidden="true"
                  />
                  Hlavný prehľad
                </>
              )}
            </NavLink>
            <NavLink
              to="/staff/jobs"
              className={({ isActive }) =>
                `${styles.navigationLink} ${
                  isActive ? styles.activeNavigationLink : ''
                }`
              }
            >
              {({ isActive }) => (
                <>
                  <BriefcaseRegular
                    className={`${styles.navigationIcon} ${
                      isActive ? styles.activeNavigationIcon : ''
                    }`}
                    style={{
                      color: isActive ? '#F59E0B' : '#9CA3AF',
                    }}
                    aria-hidden="true"
                  />
                  Pracovné pozície
                </>
              )}
            </NavLink>
            <NavLink
              to="/staff/users"
              className={({ isActive }) =>
                `${styles.navigationLink} ${
                  isActive ? styles.activeNavigationLink : ''
                }`
              }
            >
              {({ isActive }) => (
                <>
                  <PeopleRegular
                    className={`${styles.navigationIcon} ${
                      isActive ? styles.activeNavigationIcon : ''
                    }`}
                    style={{
                      color: isActive ? '#F59E0B' : '#9CA3AF',
                    }}
                    aria-hidden="true"
                  />
                  Užívateľia
                </>
              )}
            </NavLink>
          </nav>
        </aside>
        <div className={styles.content}>
          <Outlet />
        </div>
      </div>
    </div>
  )
}
