import { Input, makeStyles } from '@fluentui/react-components'
import { SearchRegular } from '@fluentui/react-icons'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { jobOffers } from './jobOffers.ts'

const useStyles = makeStyles({
  main: {
    maxWidth: '1120px',
    width: '100%',
    boxSizing: 'border-box',
    margin: '0 auto',
    padding: '64px 32px 88px',
  },
  heading: {
    margin: 0,
    color: '#1F2933',
    fontSize: 'clamp(32px, 5vw, 48px)',
  },
  intro: {
    maxWidth: '650px',
    margin: '12px 0 36px',
    color: '#475569',
    fontSize: '17px',
    lineHeight: 1.6,
  },
  filter: {
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    maxWidth: '520px',
    marginBottom: '28px',
  },
  filterIcon: {
    color: '#687280',
  },
  tableWrapper: {
    overflowX: 'auto',
    border: '1px solid #E5E7EB',
    borderRadius: '12px',
    boxShadow: '0 4px 16px rgba(31, 41, 51, 0.06)',
  },
  table: {
    width: '100%',
    minWidth: '680px',
    borderCollapse: 'collapse',
    backgroundColor: '#FFFFFF',
  },
  headerCell: {
    padding: '18px 20px',
    color: '#475569',
    fontSize: '13px',
    fontWeight: 700,
    textAlign: 'left',
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    backgroundColor: '#F7F8FA',
    borderBottom: '1px solid #E5E7EB',
  },
  cell: {
    padding: '20px',
    color: '#475569',
    borderBottom: '1px solid #E5E7EB',
  },
  row: {
    ':last-child td': {
      borderBottom: 'none',
    },
    ':hover': {
      backgroundColor: '#FFF7F1',
    },
  },
  title: {
    color: '#1F2933',
    fontWeight: 700,
    textDecoration: 'none',
    ':hover': {
      color: '#D8631D',
      textDecoration: 'underline',
    },
  },
  empty: {
    padding: '36px 20px',
    color: '#687280',
    textAlign: 'center',
  },
})

export function JobOffersPage() {
  const styles = useStyles()
  const [filter, setFilter] = useState('')
  const normalizedFilter = filter.trim().toLocaleLowerCase('sk-SK')
  const filteredOffers = useMemo(
    () =>
      jobOffers.filter((offer) =>
        [offer.title, offer.location, offer.employmentType].some((value) =>
          value.toLocaleLowerCase('sk-SK').includes(normalizedFilter),
        ),
      ),
    [normalizedFilter],
  )

  return (
    <main className={styles.main}>
      <h1 className={styles.heading}>Aktuálne pracovné pozície</h1>
      <p className={styles.intro}>
        Nájdite si príležitosť, ktorá vám vyhovuje. Kliknutím na pracovnú
        pozíciu zobrazíte všetky podrobnosti.
      </p>
      <div className={styles.filter}>
        <SearchRegular className={styles.filterIcon} aria-hidden="true" />
        <Input
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          placeholder="Filtrujte podľa názvu, lokality alebo typu úväzku"
          aria-label="Filtrovať pracovné pozície"
        />
      </div>
      <div className={styles.tableWrapper}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th className={styles.headerCell}>Názov pozície</th>
              <th className={styles.headerCell}>Lokalita</th>
              <th className={styles.headerCell}>Typ úväzku</th>
            </tr>
          </thead>
          <tbody>
            {filteredOffers.map((offer) => (
              <tr className={styles.row} key={offer.id}>
                <td className={styles.cell}>
                  <Link className={styles.title} to={`/aktualne-pozicie/${offer.id}`}>
                    {offer.title}
                  </Link>
                </td>
                <td className={styles.cell}>{offer.location}</td>
                <td className={styles.cell}>{offer.employmentType}</td>
              </tr>
            ))}
            {filteredOffers.length === 0 && (
              <tr>
                <td className={styles.empty} colSpan={3}>
                  Žiadna pracovná pozícia nezodpovedá zadanému filtru.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </main>
  )
}
