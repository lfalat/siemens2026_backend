import {
  Button,
  Field,
  Input,
  makeStyles,
  Select,
  Textarea,
} from '@fluentui/react-components'

const useStyles = makeStyles({
  main: {
    display: 'block',
    width: '100%',
    boxSizing: 'border-box',
    margin: 0,
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
  form: {
    display: 'grid',
    gap: '24px',
  },
  section: {
    display: 'grid',
    gap: '24px',
    padding: '28px',
    backgroundColor: '#FFFFFF',
    border: '1px solid #D1D5DB',
    borderRadius: '12px',
    boxShadow: '0 2px 8px rgba(31, 41, 51, 0.06)',
  },
  sectionHeading: {
    margin: 0,
    color: '#000000',
    fontSize: '20px',
  },
  row: {
    display: 'grid',
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
    gap: '20px',
  },
  salaryRow: {
    gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
  },
  openingsRow: {
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
  },
  field: {
    display: 'grid',
    gap: '8px',
  },
  label: {
    fontWeight: 700,
  },
  input: {
    width: '100%',
    boxSizing: 'border-box',
  },
  description: {
    width: '100%',
    boxSizing: 'border-box',
  },
  actions: {
    display: 'flex',
    justifyContent: 'flex-start',
    gap: '12px',
  },
  cancelButton: {
    padding: '12px 24px',
    backgroundColor: '#FFFFFF',
    color: '#000000',
    border: '1px solid #D1D5DB',
    ':hover': {
      backgroundColor: '#F1F3F5',
      color: '#000000',
    },
  },
  draftButton: {
    padding: '12px 24px',
    backgroundColor: '#9CA3AF',
    color: '#000000',
    border: '1px solid #9CA3AF',
    ':hover': {
      backgroundColor: '#6B7280',
      color: '#000000',
    },
  },
  publishButton: {
    padding: '12px 24px',
    backgroundColor: '#F59E0B',
    color: '#000000',
    border: '1px solid #F59E0B',
    ':hover': {
      backgroundColor: '#D97706',
      color: '#000000',
    },
  },
})

export function StaffJobsPage() {
  const styles = useStyles()

  return (
    <div className={styles.main}>
      <h1 className={styles.heading}>Vytvoriť pracovnú pozíciu</h1>
      <p className={styles.subtitle}>
        Vyplňte údaje potrebné na pracovnej pozície
      </p>
      <form className={styles.form}>
        <section className={styles.section}>
          <h2 className={styles.sectionHeading}>Základné informácie</h2>
          <div className={styles.row}>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Názov pozície</span>}
              required
            >
              <Input
                className={styles.input}
                placeholder="Napr. Vývojár softvéru"
              />
            </Field>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Lokalita</span>}
              required
            >
              <Input className={styles.input} placeholder="Napr. Bratislava" />
            </Field>
          </div>
          <div className={styles.row}>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Oddelenie</span>}
              required
            >
              <Input className={styles.input} placeholder="Názov oddelenia" />
            </Field>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Typ úväzku</span>}
              required
            >
              <Select className={styles.input} defaultValue="">
                <option value="" disabled>
                  Vyberte typ úväzku
                </option>
                <option value="full-time">Plný úväzok</option>
                <option value="part-time">Čiastočný úväzok</option>
                <option value="contract">Dohoda</option>
              </Select>
            </Field>
          </div>
          <div className={`${styles.row} ${styles.salaryRow}`}>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Výška mzdy</span>}
              required
            >
              <Input className={styles.input} placeholder="Napr. 1800 EUR" />
            </Field>
            <Field
              className={styles.field}
              label={
                <span className={styles.label}>Frekvencia vyplácania</span>
              }
              required
            >
              <Select className={styles.input} defaultValue="">
                <option value="" disabled>
                  Vyberte frekvenciu
                </option>
                <option value="year">Rok</option>
                <option value="month">Mesiac</option>
                <option value="hour">Hodina</option>
              </Select>
            </Field>
          </div>
          <div className={`${styles.row} ${styles.openingsRow}`}>
            <Field
              className={styles.field}
              label={
                <span className={styles.label}>Počet otvorených miest</span>
              }
              required
            >
              <Input
                className={styles.input}
                type="number"
                min={1}
                defaultValue="1"
              />
            </Field>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Recruiter</span>}
              required
            >
              <Select className={styles.input} defaultValue="">
                <option value="" disabled>
                  Vyberte rekrútera
                </option>
              </Select>
            </Field>
          </div>
        </section>
        <section className={styles.section}>
          <Field
            className={styles.field}
            label={<span className={styles.label}>Opis pracovnej pozície</span>}
            required
          >
            <Textarea
              className={styles.description}
              placeholder="Zadajte opis"
              rows={7}
            />
          </Field>
        </section>
        <div className={styles.actions}>
          <Button type="button" className={styles.cancelButton}>
            Zrušiť
          </Button>
          <Button type="button" className={styles.draftButton}>
            Uložiť koncept
          </Button>
          <Button type="submit" className={styles.publishButton}>
            Zverejniť pozíciu
          </Button>
        </div>
      </form>
    </div>
  )
}
