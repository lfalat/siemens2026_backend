import { Button, Input, makeStyles, Select } from '@fluentui/react-components'
import {
  ChevronLeftRegular,
  ChevronRightRegular,
  SearchRegular,
} from '@fluentui/react-icons'
import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { usePublishedJobPosts } from '../../../features/recruitment/publicJobPosts.ts'
import {
  employmentTypeLabels,
  employmentTypes,
  formatSalary,
  type EmploymentType,
} from '../../../features/recruitment/vocabulary.ts'
import { problemMessage } from '../../../shared/api/problem.ts'

const useStyles = makeStyles({
  main: {
    // index.css centres every <main>'s children (`display: grid; place-items: center`), and browsers now apply
    // justify-items to block layout too, so both are reset: this page is a left-aligned column.
    display: 'block',
    justifyItems: 'stretch',
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
    lineHeight: 1.15,
  },
  intro: {
    maxWidth: '650px',
    margin: '12px 0 36px',
    color: '#475569',
    fontSize: '17px',
    lineHeight: 1.6,
  },
  filters: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '16px',
    marginBottom: '28px',
  },
  search: {
    flex: '1 1 320px',
  },
  typeFilter: {
    flex: '0 1 300px',
  },
  tableWrapper: {
    overflowX: 'auto',
    border: '1px solid #E5E7EB',
    borderRadius: '12px',
    boxShadow: '0 4px 16px rgba(31, 41, 51, 0.06)',
  },
  table: {
    width: '100%',
    minWidth: '760px',
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
  pager: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    marginTop: '20px',
    color: '#475569',
  },
  pagerButtons: {
    display: 'flex',
    gap: '8px',
  },
})

/** Posts per page. */
const PAGE_SIZE = 10

/** How long typing pauses before the search is sent. */
const SEARCH_DELAY_MS = 300

type FilterChanges = Partial<Record<'strana' | 'hladat' | 'typ', string>>

/** The URL's query with `changes` applied; a change to the filter, not the page, starts again on page 1. */
function withChanges(current: URLSearchParams, changes: FilterChanges) {
  const next = new URLSearchParams(current)
  if (!('strana' in changes)) next.delete('strana')
  for (const [key, value] of Object.entries(changes)) {
    if (value) next.set(key, value)
    else next.delete(key)
  }
  return next
}

function isEmploymentType(value: string | null): value is EmploymentType {
  return employmentTypes.some((type) => type === value)
}

export function JobOffersPage() {
  const styles = useStyles()
  // The page and the filter live in the URL, so the back button from a post returns to the same page.
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number.parseInt(params.get('strana') ?? '', 10) || 1)
  const search = params.get('hladat') ?? ''
  const typeParam = params.get('typ')
  const employmentType = isEmploymentType(typeParam) ? typeParam : undefined
  const [searchInput, setSearchInput] = useState(search)

  /** A filter change replaces the history entry; a page change adds one, so the back button steps through pages. */
  function update(changes: FilterChanges) {
    setParams((current) => withChanges(current, changes), {
      replace: !('strana' in changes),
    })
  }

  useEffect(() => {
    const trimmed = searchInput.trim()
    if (trimmed === search) return
    const timer = setTimeout(
      () =>
        setParams(
          (current) => withChanges(current, { hladat: trimmed || undefined }),
          { replace: true },
        ),
      SEARCH_DELAY_MS,
    )
    return () => clearTimeout(timer)
  }, [searchInput, search, setParams])

  const { data, isPending, isError, error, isPlaceholderData } =
    usePublishedJobPosts({
      offset: (page - 1) * PAGE_SIZE,
      limit: PAGE_SIZE,
      search: search || undefined,
      employmentType,
    })

  const filtered = Boolean(search || employmentType)
  const first = data && data.items.length > 0 ? data.offset + 1 : 0
  const last = data ? data.offset + data.items.length : 0

  return (
    <main className={styles.main}>
      <h1 className={styles.heading}>Aktuálne pracovné pozície</h1>
      <p className={styles.intro}>
        Nájdite si príležitosť, ktorá vám vyhovuje. Kliknutím na pracovnú
        pozíciu zobrazíte všetky podrobnosti.
      </p>
      <div className={styles.filters}>
        <Input
          className={styles.search}
          contentBefore={<SearchRegular aria-hidden="true" />}
          value={searchInput}
          maxLength={200}
          onChange={(event) => setSearchInput(event.target.value)}
          placeholder="Filtrujte podľa názvu alebo lokality"
          aria-label="Filtrovať pracovné pozície"
        />
        <Select
          className={styles.typeFilter}
          value={employmentType ?? ''}
          onChange={(_, { value }) => update({ typ: value || undefined })}
          aria-label="Filtrovať podľa typu pracovného pomeru"
        >
          <option value="">Všetky typy pracovného pomeru</option>
          {employmentTypes.map((type) => (
            <option key={type} value={type}>
              {employmentTypeLabels[type]}
            </option>
          ))}
        </Select>
      </div>
      <div className={styles.tableWrapper}>
        <table className={styles.table} aria-busy={isPlaceholderData}>
          <thead>
            <tr>
              <th className={styles.headerCell}>Názov pozície</th>
              <th className={styles.headerCell}>Lokalita</th>
              <th className={styles.headerCell}>Typ pracovného pomeru</th>
              <th className={styles.headerCell}>Mzda</th>
            </tr>
          </thead>
          <tbody>
            {data?.items.map((offer) => (
              <tr className={styles.row} key={offer.id}>
                <td className={styles.cell}>
                  <Link
                    className={styles.title}
                    to={`/aktualne-pozicie/${offer.id}`}
                  >
                    {offer.title}
                  </Link>
                </td>
                <td className={styles.cell}>{offer.location}</td>
                <td className={styles.cell}>
                  {employmentTypeLabels[offer.employmentType]}
                </td>
                <td className={styles.cell}>
                  {formatSalary(offer.salary, offer.paymentType)}
                </td>
              </tr>
            ))}
            {(isPending || isError || data?.items.length === 0) && (
              <tr>
                <td className={styles.empty} colSpan={4}>
                  {isPending
                    ? 'Načítavam pracovné pozície…'
                    : isError
                      ? problemMessage(error)
                      : data.total > 0
                        ? 'Na tejto strane už nie sú žiadne pracovné pozície.'
                        : filtered
                          ? 'Žiadna pracovná pozícia nezodpovedá zadanému filtru.'
                          : 'Momentálne nemáme žiadne otvorené pracovné pozície.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
      {data && data.items.length > 0 && (
        <div className={styles.pager}>
          <span>
            Zobrazené {first}–{last} z {data.total}
          </span>
          <div className={styles.pagerButtons}>
            <Button
              icon={<ChevronLeftRegular />}
              disabled={page <= 1 || isPlaceholderData}
              onClick={() =>
                update({ strana: page > 2 ? String(page - 1) : undefined })
              }
            >
              Predchádzajúca
            </Button>
            <Button
              icon={<ChevronRightRegular />}
              iconPosition="after"
              disabled={last >= data.total || isPlaceholderData}
              onClick={() => update({ strana: String(page + 1) })}
            >
              Ďalšia
            </Button>
          </div>
        </div>
      )}
    </main>
  )
}
