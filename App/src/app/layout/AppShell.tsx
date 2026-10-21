import { makeStyles, tokens } from '@fluentui/react-components'
import type { ReactNode } from 'react'
import { Link, Outlet } from 'react-router'

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    minHeight: '100vh',
    backgroundColor: tokens.colorNeutralBackground3,
  },
  header: {
    position: 'sticky',
    top: 0,
    zIndex: 10,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalL,
    height: '72px',
    boxSizing: 'border-box',
    paddingInline: tokens.spacingHorizontalXXL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    boxShadow: tokens.shadow2,
  },
  brand: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground1,
    textDecoration: 'none',
  },
  logo: {
    width: 'auto',
    height: '20px',
    objectFit: 'contain',
  },
  // Pushes the actions to the right edge.
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    marginLeft: 'auto',
  },
})

/** The page frame: the top bar with the logo on the left and `actions` on the right edge, then the page. */
export function AppShell({
  homeTo,
  actions,
}: {
  homeTo: string
  actions: ReactNode
}) {
  const styles = useStyles()

  return (
    <div className={styles.root}>
      <header className={styles.header}>
        <Link to={homeTo} className={styles.brand}>
          <img className={styles.logo} src="/hh_logo_small.png" alt="" />
        </Link>
        <div className={styles.actions}>{actions}</div>
      </header>
      <Outlet />
    </div>
  )
}
