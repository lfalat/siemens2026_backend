import {
  Button,
  Field,
  Input,
  makeStyles,
  MessageBar,
  MessageBarBody,
  Select,
  Textarea,
} from '@fluentui/react-components'
import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import {
  employmentTypeLabels,
  employmentTypes,
  jobPostFormSchema,
  paymentTypeLabels,
  paymentTypes,
  useCreateJobPost,
  useRecruiters,
  type JobPostForm,
} from '../../../features/recruitment/jobPosts.ts'
import {
  problemFieldErrors,
  problemMessage,
} from '../../../shared/api/problem.ts'
import { useCurrentStaff } from '../../../shared/auth/useCurrentStaff.ts'

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
    // A validation message makes its field taller; the others in the row keep their height instead of stretching.
    alignItems: 'start',
  },
  salaryRow: {
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
  },
  openingsRow: {
    gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
  },
  field: {
    display: 'grid',
    gap: '8px',
    alignContent: 'start',
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

const formFields = [
  'title',
  'location',
  'department',
  'description',
  'employmentType',
  'salary',
  'paymentType',
  'openPositions',
  'recruiterId',
] as const satisfies readonly (keyof JobPostForm)[]

/** The roles the API lets create a job post (StaffPolicies.HiringManager); here they only shape the UI. */
const creatorRoles = ['HiringManager', 'Admin']

export function StaffJobsPage() {
  const styles = useStyles()
  const navigate = useNavigate()
  const createJobPost = useCreateJobPost()
  const recruiters = useRecruiters()
  const { data: me } = useCurrentStaff()
  const [saved, setSaved] = useState<string | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  const canCreate =
    me?.roles.some((role) => creatorRoles.includes(role)) ?? false
  const busy = createJobPost.isPending || !canCreate

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<JobPostForm>({
    resolver: zodResolver(jobPostFormSchema),
    defaultValues: {
      title: '',
      location: '',
      department: '',
      description: '',
      openPositions: 1,
    },
  })

  const fieldError = (name: keyof JobPostForm) =>
    errors[name]?.message
      ? {
          validationState: 'error' as const,
          validationMessage: errors[name].message,
        }
      : {}

  function submit(values: JobPostForm, isPublished: boolean) {
    setSaved(null)
    setFailure(null)
    createJobPost.mutate(
      { ...values, isPublished },
      {
        onSuccess: (post) => {
          reset()
          setSaved(
            post.isPublished
              ? `Pozícia „${post.title}“ bola zverejnená.`
              : `Pozícia „${post.title}“ bola uložená ako koncept.`,
          )
        },
        onError: (error) => {
          // The API's rules are authoritative (ADR-F010): its messages go onto the fields they name.
          const fields = problemFieldErrors(error)
          for (const name of formFields) {
            if (fields[name]) setError(name, { message: fields[name] })
          }
          setFailure(problemMessage(error))
        },
      },
    )
  }

  return (
    <div className={styles.main}>
      <h1 className={styles.heading}>Vytvoriť pracovnú pozíciu</h1>
      <p className={styles.subtitle}>
        Vyplňte údaje potrebné na vytvorenie pracovnej pozície
      </p>
      <form
        className={styles.form}
        noValidate
        onSubmit={(event) => {
          // Both buttons submit; the one pressed decides between a draft and a published post.
          const submitter = (event.nativeEvent as SubmitEvent).submitter
          const isPublished = submitter?.getAttribute('name') === 'publish'
          void handleSubmit((values) => submit(values, isPublished))(event)
        }}
      >
        {me && !canCreate && (
          <MessageBar intent="warning">
            <MessageBarBody>
              Pracovnú pozíciu môže vytvoriť iba hiring manažér.
            </MessageBarBody>
          </MessageBar>
        )}
        {saved && (
          <MessageBar intent="success">
            <MessageBarBody>{saved}</MessageBarBody>
          </MessageBar>
        )}
        {failure && (
          <MessageBar intent="error">
            <MessageBarBody>{failure}</MessageBarBody>
          </MessageBar>
        )}
        <section className={styles.section}>
          <h2 className={styles.sectionHeading}>Základné informácie</h2>
          <div className={styles.row}>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Názov pozície</span>}
              required
              {...fieldError('title')}
            >
              <Input
                className={styles.input}
                placeholder="Napr. Vývojár softvéru"
                {...register('title')}
              />
            </Field>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Lokalita</span>}
              required
              {...fieldError('location')}
            >
              <Input
                className={styles.input}
                placeholder="Napr. Bratislava"
                {...register('location')}
              />
            </Field>
          </div>
          <div className={styles.row}>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Oddelenie</span>}
              required
              {...fieldError('department')}
            >
              <Input
                className={styles.input}
                placeholder="Názov oddelenia"
                {...register('department')}
              />
            </Field>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Typ úväzku</span>}
              required
              {...fieldError('employmentType')}
            >
              <Select
                className={styles.input}
                defaultValue=""
                {...register('employmentType')}
              >
                <option value="" disabled>
                  Vyberte typ úväzku
                </option>
                {employmentTypes.map((type) => (
                  <option key={type} value={type}>
                    {employmentTypeLabels[type]}
                  </option>
                ))}
              </Select>
            </Field>
          </div>
          <div className={`${styles.row} ${styles.salaryRow}`}>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Výška mzdy (EUR)</span>}
              required
              {...fieldError('salary')}
            >
              <Input
                className={styles.input}
                type="number"
                min={0.01}
                step={0.01}
                placeholder="Kladné číslo s najviac 2 desatinnými miestami"
                {...register('salary', { valueAsNumber: true })}
              />
            </Field>
            <Field
              className={styles.field}
              label={
                <span className={styles.label}>Frekvencia vyplácania</span>
              }
              required
              {...fieldError('paymentType')}
            >
              <Select
                className={styles.input}
                defaultValue=""
                {...register('paymentType')}
              >
                <option value="" disabled>
                  Vyberte frekvenciu
                </option>
                {paymentTypes.map((type) => (
                  <option key={type} value={type}>
                    {paymentTypeLabels[type]}
                  </option>
                ))}
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
              {...fieldError('openPositions')}
            >
              <Input
                className={styles.input}
                type="number"
                min={1}
                max={1000}
                step={1}
                {...register('openPositions', { valueAsNumber: true })}
              />
            </Field>
            <Field
              className={styles.field}
              label={<span className={styles.label}>Recruiter</span>}
              required
              {...(recruiters.isError
                ? {
                    validationState: 'error' as const,
                    validationMessage: problemMessage(recruiters.error),
                  }
                : fieldError('recruiterId'))}
            >
              <Select
                className={styles.input}
                defaultValue=""
                disabled={!recruiters.data}
                {...register('recruiterId')}
              >
                <option value="" disabled>
                  {recruiters.isPending
                    ? 'Načítavam rekrutérov…'
                    : 'Vyberte rekrútera'}
                </option>
                {recruiters.data?.map((recruiter) => (
                  <option key={recruiter.id} value={recruiter.id}>
                    {recruiter.displayName}
                  </option>
                ))}
              </Select>
            </Field>
          </div>
        </section>
        <section className={styles.section}>
          <Field
            className={styles.field}
            label={<span className={styles.label}>Opis pracovnej pozície</span>}
            required
            {...fieldError('description')}
          >
            <Textarea
              className={styles.description}
              placeholder="Zadajte opis"
              rows={7}
              {...register('description')}
            />
          </Field>
        </section>
        <div className={styles.actions}>
          <Button
            type="button"
            className={styles.cancelButton}
            disabled={createJobPost.isPending}
            onClick={() => void navigate('/staff')}
          >
            Zrušiť
          </Button>
          <Button
            type="submit"
            name="draft"
            className={styles.draftButton}
            disabled={busy}
          >
            Uložiť koncept
          </Button>
          <Button
            type="submit"
            name="publish"
            className={styles.publishButton}
            disabled={busy}
          >
            Zverejniť pozíciu
          </Button>
        </div>
      </form>
    </div>
  )
}
