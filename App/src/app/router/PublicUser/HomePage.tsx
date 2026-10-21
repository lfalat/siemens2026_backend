import { Button, makeStyles } from '@fluentui/react-components'
import { ArrowRightRegular } from '@fluentui/react-icons'

const useStyles = makeStyles({
  main: {
    overflow: 'hidden',
    backgroundColor: '#FFFFFF',
  },
  hero: {
    position: 'relative',
    display: 'grid',
    justifyItems: 'center',
    minHeight: '560px',
    boxSizing: 'border-box',
    padding: '88px 32px 96px',
    textAlign: 'center',
    background:
      'linear-gradient(135deg, #FFF7F1 0%, #FFFFFF 52%, #FFF1E8 100%)',
  },
  glow: {
    position: 'absolute',
    width: '420px',
    height: '420px',
    top: '-180px',
    right: '-120px',
    borderRadius: '50%',
    backgroundColor: '#FDE1D0',
    opacity: 0.65,
  },
  content: {
    position: 'relative',
    zIndex: 1,
    display: 'grid',
    justifyItems: 'center',
    gap: '24px',
    width: '100%',
    maxWidth: '760px',
  },
  logo: {
    width: '220px',
    height: 'auto',
  },
  eyebrow: {
    margin: 0,
    color: '#D8631D',
    fontSize: '14px',
    fontWeight: 700,
    letterSpacing: '0.12em',
    textTransform: 'uppercase',
  },
  heading: {
    maxWidth: '700px',
    margin: 0,
    color: '#1F2933',
    fontSize: 'clamp(38px, 6vw, 68px)',
    lineHeight: 1.05,
    letterSpacing: '-0.04em',
  },
  description: {
    maxWidth: '600px',
    margin: 0,
    color: '#475569',
    fontSize: '18px',
    lineHeight: 1.6,
  },
  jobsButton: {
    minHeight: '54px',
    marginTop: '8px',
    paddingInline: '28px',
    fontSize: '16px',
    fontWeight: 700,
  },
  details: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gap: '24px',
    maxWidth: '1080px',
    boxSizing: 'border-box',
    margin: '0 auto',
    padding: '72px 32px 88px',
  },
  detail: {
    padding: '28px',
    border: '1px solid #E5E7EB',
    borderRadius: '12px',
    backgroundColor: '#FFFFFF',
    boxShadow: '0 8px 24px rgba(31, 41, 51, 0.06)',
  },
  detailTitle: {
    margin: '0 0 10px',
    color: '#1F2933',
    fontSize: '19px',
  },
  detailText: {
    margin: 0,
    color: '#687280',
    lineHeight: 1.6,
  },
  '@media (max-width: 700px)': {
    hero: {
      minHeight: '520px',
      padding: '64px 24px 72px',
    },
    details: {
      gridTemplateColumns: '1fr',
      padding: '48px 24px 64px',
    },
  },
})

export function HomePage() {
  const styles = useStyles()

  return (
    <main className={styles.main}>
      <section className={styles.hero}>
        <div className={styles.glow} aria-hidden="true" />
        <div className={styles.content}>
          <img
            className={styles.logo}
            src="/hh_logo_large.png"
            alt="Healthy Hires"
          />
          <p className={styles.eyebrow}>Práca, ktorá dáva zmysel</p>
          <h1 className={styles.heading}>
            Nájdite miesto, kde môžete rásť.
          </h1>
          <p className={styles.description}>
            Objavte pracovné príležitosti v HealthyHires a pridajte sa k tímu,
            ktorý tvorí lepšiu budúcnosť. Bez registrácie a zbytočných
            prekážok.
          </p>
          <Button
            as="a"
            href="/aktualne-pozicie"
            appearance="primary"
            className={styles.jobsButton}
            iconPosition="after"
            icon={<ArrowRightRegular />}
          >
            Aktuálne pracovné pozície
          </Button>
        </div>
      </section>
      <section id="aktualne-pozicie" className={styles.details}>
        <article className={styles.detail}>
          <h2 className={styles.detailTitle}>Vyberte si svoju príležitosť</h2>
          <p className={styles.detailText}>
            Prehľadne si pozrite otvorené pracovné pozície a nájdite tú, ktorá
            najlepšie zodpovedá vašim skúsenostiam.
          </p>
        </article>
        <article className={styles.detail}>
          <h2 className={styles.detailTitle}>Jednoduché prihlásenie</h2>
          <p className={styles.detailText}>
            O svoju vysnívanú prácu sa môžete uchádzať priamo cez pracovnú
            ponuku, bez vytvárania používateľského účtu.
          </p>
        </article>
        <article className={styles.detail}>
          <h2 className={styles.detailTitle}>Váš ďalší krok</h2>
          <p className={styles.detailText}>
            Pripravte si životopis a motivačný list. Tešíme sa, že sa
            dozvieme viac o vás.
          </p>
        </article>
      </section>
    </main>
  )
}
