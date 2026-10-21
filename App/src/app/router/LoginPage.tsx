import { Button, makeStyles } from '@fluentui/react-components'

const useStyles = makeStyles({
  main: {
    display: 'grid',
    placeItems: 'center',
    minHeight: '100vh',
    boxSizing: 'border-box',
    padding: '24px',
    backgroundColor: '#FFFFFF',
  },
  card: {
    display: 'grid',
    justifyItems: 'center',
    gap: '20px',
    width: '100%',
    maxWidth: '440px',
    boxSizing: 'border-box',
    padding: '48px 40px 36px',
    border: '1px solid #D1D5DB',
    borderRadius: '12px',
    boxShadow: '0 8px 24px rgba(31, 41, 51, 0.08)',
    textAlign: 'center',
  },
  logo: {
    display: 'block',
    width: '220px',
    height: 'auto',
    marginBottom: '8px',
  },
  heading: {
    margin: 0,
    color: '#1F2933',
    fontSize: '28px',
    lineHeight: 1.2,
  },
  description: {
    maxWidth: '320px',
    margin: 0,
    color: '#475569',
    fontSize: '15px',
    lineHeight: 1.5,
  },
  signInButton: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '12px',
    width: '100%',
    minHeight: '52px',
    marginTop: '4px',
    backgroundColor: '#FFFFFF',
    color: '#000000',
    fontSize: '15px',
    fontWeight: 600,
    border: '1px solid #D1D5DB',
    ':hover': {
      backgroundColor: '#FFFFFF',
      color: '#000000',
    },
  },
  microsoftLogo: {
    width: '24px',
    height: '24px',
    objectFit: 'contain',
  },
  footer: {
    margin: '4px 0 0',
    color: '#6B7280',
    fontSize: '12px',
    lineHeight: 1.4,
  },
})

export function LoginPage() {
  const styles = useStyles()

  return (
    <main className={styles.main}>
      <section className={styles.card}>
        <img
          className={styles.logo}
          src="/hh_logo_large.png"
          alt="Healthy Hires"
        />
        <h2 className={styles.heading}>Vitajte!</h2>
        <p className={styles.description}>
          Ak chcete pokračovať, prihláste sa pomocou firemného konta Microsoft.
        </p>
        <Button
          as="a"
          href="/staff"
          className={styles.signInButton}
        >
          <img
            className={styles.microsoftLogo}
            src="/ms_logo_mid.png"
            alt=""
          />
          <span>Prihláste sa pomocou Microsoftu -&gt;</span>
        </Button>
        <p className={styles.footer}>
          Bezpečný prístup pre oprávnených členov tímu
        </p>
      </section>
    </main>
  )
}
