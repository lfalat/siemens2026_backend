import { Button, makeStyles } from '@fluentui/react-components'

const useStyles = makeStyles({
  main: {
    display: 'grid',
    placeItems: 'center',
    minHeight: '100vh',
    boxSizing: 'border-box',
    padding: '24px',
    backgroundColor: '#FFFFFF',
    textAlign: 'center',
  },
  content: {
    display: 'grid',
    justifyItems: 'center',
    gap: '16px',
    maxWidth: '440px',
  },
  heading: {
    margin: 0,
    color: '#1F2933',
    fontSize: '32px',
  },
  description: {
    margin: 0,
    color: '#475569',
    lineHeight: 1.5,
  },
  backButton: {
    marginTop: '8px',
    backgroundColor: '#F59E0B',
    color: '#000000',
    fontWeight: 600,
    ':hover': {
      backgroundColor: '#D97706',
      color: '#000000',
    },
  },
})

export function AccessDeniedPage({ backTo = '/' }: { backTo?: string }) {
  const styles = useStyles()

  return (
    <main className={styles.main}>
      <section className={styles.content}>
        <h1 className={styles.heading}>Prístup zamietnutý</h1>
        <p className={styles.description}>
          Nemáte oprávnenie na zobrazenie tejto časti
        </p>
        <Button as="a" href={backTo} className={styles.backButton}>
          Späť na hlavný prehľad
        </Button>
      </section>
    </main>
  )
}

export function StaffAccessDeniedPage() {
  return <AccessDeniedPage backTo="/staff" />
}
