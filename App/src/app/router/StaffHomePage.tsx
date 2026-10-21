import { ApiError } from '../../shared/api/http.ts'
import { useCurrentStaff } from '../../shared/auth/useCurrentStaff.ts'
import { makeStyles } from '@fluentui/react-components'

const useStyles = makeStyles({
  main: {
    maxWidth: '1180px',
    margin: '0 auto',
    padding: '48px 32px',
  },
  heading: {
    margin: 0,
    color: '#1F2933',
    fontSize: '32px',
  },
  subtitle: {
    margin: '8px 0 32px',
    color: '#475569',
  },
  cards: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
    gap: '20px',
  },
  card: {
    padding: '24px',
    backgroundColor: '#FFFFFF',
    border: '1px solid #D1D5DB',
    borderRadius: '12px',
    boxShadow: '0 2px 8px rgba(31, 41, 51, 0.06)',
  },
  cardTitle: {
    margin: 0,
    color: '#006779',
    fontSize: '16px',
  },
  cardText: {
    margin: '12px 0 0',
    color: '#475569',
    lineHeight: 1.5,
  },
  error: {
    color: '#C62828',
  },
})

export function StaffHomePage() {
  const styles = useStyles()
  const { data: me, error, isPending } = useCurrentStaff()

  if (isPending) return <main className={styles.main}>Načítava sa…</main>

  if (error) {
    const status = error instanceof ApiError ? error.status : undefined
    return (
      <main className={styles.main}>
        <p className={styles.error}>
          {status === 403
            ? 'Ste prihlásený, ale váš účet nemá pridelenú Healthy Hires rolu.'
            : status === 401
              ? 'API neprijal vaše prihlásenie. Odhláste sa a prihláste znova.'
              : 'API nie je dostupné.'}
        </p>
      </main>
    )
  }

  return (
    <main className={styles.main}>
      <h1 className={styles.heading}>Vitajte {me.name ?? me.username}</h1>
      <p className={styles.subtitle}>Hlavný prehľad Healthy Hires</p>
      <div className={styles.cards}>
        <section className={styles.card}>
          <h2 className={styles.cardTitle}>Váš prístup</h2>
          <p className={styles.cardText}>
            Roly: {me.roles.join(', ') || 'bez pridelenej roly'}
          </p>
        </section>
        <section className={styles.card}>
          <h2 className={styles.cardTitle}>Stav účtu</h2>
          <p className={styles.cardText}>Účet je úspešne prihlásený.</p>
        </section>
        <section className={styles.card}>
          <h2 className={styles.cardTitle}>Ďalšie kroky</h2>
          <p className={styles.cardText}>
            Vyberte si funkciu z ponuky podľa svojej pridelenej roly.
          </p>
        </section>
      </div>
    </main>
  )
}
