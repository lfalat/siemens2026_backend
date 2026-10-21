import { Button, makeStyles } from '@fluentui/react-components'
import { ArrowLeftRegular } from '@fluentui/react-icons'
import { Link, useParams } from 'react-router'
import { jobOffers } from './jobOffers.ts'

const useStyles = makeStyles({
  main: {
    maxWidth: '900px',
    width: '100%',
    boxSizing: 'border-box',
    margin: '0 auto',
    padding: '64px 32px 88px',
  },
  back: {
    marginBottom: '32px',
    color: '#475569',
  },
  card: {
    padding: '40px',
    border: '1px solid #E5E7EB',
    borderRadius: '12px',
    backgroundColor: '#FFFFFF',
    boxShadow: '0 8px 24px rgba(31, 41, 51, 0.06)',
  },
  heading: {
    margin: 0,
    color: '#1F2933',
    fontSize: 'clamp(32px, 5vw, 48px)',
  },
  summary: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gap: '20px',
    margin: '28px 0 40px',
    padding: '24px 0',
    borderTop: '1px solid #E5E7EB',
    borderBottom: '1px solid #E5E7EB',
  },
  label: {
    display: 'block',
    marginBottom: '6px',
    color: '#687280',
    fontSize: '13px',
  },
  value: {
    color: '#1F2933',
    fontWeight: 700,
  },
  sectionHeading: {
    margin: '0 0 12px',
    color: '#1F2933',
    fontSize: '21px',
  },
  description: {
    margin: '0 0 32px',
    color: '#475569',
    lineHeight: 1.7,
  },
  details: {
    display: 'grid',
    gap: '14px',
    margin: '0 0 32px',
    color: '#475569',
  },
  notFound: {
    color: '#475569',
  },
  '@media (max-width: 700px)': {
    main: {
      padding: '40px 24px 64px',
    },
    card: {
      padding: '28px 24px',
    },
    summary: {
      gridTemplateColumns: '1fr',
    },
  },
})

export function JobOfferDetailPage() {
  const styles = useStyles()
  const { offerId } = useParams()
  const offer = jobOffers.find((item) => item.id === offerId)

  if (!offer) {
    return (
      <main className={styles.main}>
        <Link to="/aktualne-pozicie">Späť na pracovné pozície</Link>
        <p className={styles.notFound}>Pracovná pozícia nebola nájdená.</p>
      </main>
    )
  }

  return (
    <main className={styles.main}>
      <Button
        as="a"
        href="/aktualne-pozicie"
        appearance="subtle"
        className={styles.back}
        icon={<ArrowLeftRegular />}
      >
        Späť na pracovné pozície
      </Button>
      <article className={styles.card}>
        <h1 className={styles.heading}>{offer.title}</h1>
        <div className={styles.summary}>
          <div>
            <span className={styles.label}>Lokalita</span>
            <span className={styles.value}>{offer.location}</span>
          </div>
          <div>
            <span className={styles.label}>Typ úväzku</span>
            <span className={styles.value}>{offer.employmentType}</span>
          </div>
          <div>
            <span className={styles.label}>Oddelenie</span>
            <span className={styles.value}>{offer.department}</span>
          </div>
        </div>
        <h2 className={styles.sectionHeading}>Podrobnosti pracovnej pozície</h2>
        <p className={styles.description}>{offer.description}</p>
        <div className={styles.details}>
          <div>
            <strong>Výška mzdy (EUR):</strong> {offer.salary}
          </div>
          <div>
            <strong>Frekvencia vyplácania:</strong> {offer.salaryFrequency}
          </div>
          <div>
            <strong>Počet otvorených miest:</strong> {offer.openings}
          </div>
        </div>
        <Button appearance="primary">Mám záujem o túto pozíciu</Button>
      </article>
    </main>
  )
}
