import { Button, makeStyles } from '@fluentui/react-components'
import { ArrowLeftRegular } from '@fluentui/react-icons'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { usePublishedJobPost } from '../../../features/recruitment/publicJobPosts.ts'
import {
  employmentTypeLabels,
  formatSalary,
} from '../../../features/recruitment/vocabulary.ts'
import { ApiError } from '../../../shared/api/http.ts'
import { problemMessage } from '../../../shared/api/problem.ts'

const useStyles = makeStyles({
  main: {
    // index.css centres every <main>'s children (`display: grid; place-items: center`), and browsers now apply
    // justify-items to block layout too, so both are reset: this page is a left-aligned column.
    display: 'block',
    justifyItems: 'stretch',
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
    lineHeight: 1.15,
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
    // Keeps the paragraphs the hiring manager typed.
    whiteSpace: 'pre-line',
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
  const navigate = useNavigate()
  const location = useLocation()
  const { offerId = '' } = useParams()
  const { data: offer, error } = usePublishedJobPost(offerId)

  // Back to the list as it was left (page, filter) when the visitor came from it; otherwise to its first page.
  function back() {
    if (location.key !== 'default') void navigate(-1)
    else void navigate('/aktualne-pozicie')
  }

  if (!offer) {
    return (
      <main className={styles.main}>
        <Link to="/aktualne-pozicie">Späť na pracovné pozície</Link>
        <p className={styles.notFound}>
          {error instanceof ApiError && error.status === 404
            ? 'Pracovná pozícia nebola nájdená.'
            : error
              ? problemMessage(error)
              : 'Načítavam pracovnú pozíciu…'}
        </p>
      </main>
    )
  }

  return (
    <main className={styles.main}>
      <Button
        appearance="subtle"
        className={styles.back}
        icon={<ArrowLeftRegular />}
        onClick={back}
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
            <span className={styles.label}>Typ pracovného pomeru</span>
            <span className={styles.value}>
              {employmentTypeLabels[offer.employmentType]}
            </span>
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
            <strong>Mzda:</strong>{' '}
            {formatSalary(offer.salary, offer.paymentType)}
          </div>
          <div>
            <strong>Počet otvorených miest:</strong> {offer.openPositions}
          </div>
        </div>
        <Button appearance="primary">Mám záujem o túto pozíciu</Button>
      </article>
    </main>
  )
}
