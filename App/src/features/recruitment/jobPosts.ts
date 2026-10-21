import { useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { request } from '../../shared/api/http.ts'
import { useCommand } from '../../shared/api/useCommand.ts'
import { getAccessToken } from '../../shared/auth/staffAuth.ts'
import { useStaffAuth } from '../../shared/auth/StaffAuthContext.ts'

/** The forms of employment the API accepts; mirrors `EmploymentTypes` in RecruitmentVocabulary.cs. */
export const employmentTypes = [
  'full_time',
  'part_time',
  'fixed_term',
  'work_performance_agreement',
  'work_activity_agreement',
  'student_work_agreement',
  'self_employed',
] as const

export type EmploymentType = (typeof employmentTypes)[number]

export const employmentTypeLabels: Record<EmploymentType, string> = {
  full_time: 'Trvalý pracovný pomer (TPP)',
  part_time: 'Skrátený úväzok',
  fixed_term: 'Pracovný pomer na dobu určitú',
  work_performance_agreement: 'Dohoda o vykonaní práce',
  work_activity_agreement: 'Dohoda o pracovnej činnosti',
  student_work_agreement: 'Dohoda o brigádnickej práci študentov',
  self_employed: 'Živnosť',
}

/** How the wage is stated; mirrors `PaymentTypes` in RecruitmentVocabulary.cs. */
export const paymentTypes = ['monthly', 'hourly'] as const

export type PaymentType = (typeof paymentTypes)[number]

export const paymentTypeLabels: Record<PaymentType, string> = {
  monthly: 'Mesačná mzda',
  hourly: 'Hodinová mzda',
}

const required = 'Toto pole je povinné.'
const text = z
  .string()
  .trim()
  .min(1, required)
  .max(200, 'Text môže mať najviac 200 znakov.')
const positions = 'Zadajte celé číslo od 1 do 1000.'
const salary = 'Zadajte kladné číslo s najviac 2 desatinnými miestami.'

/** The job post form's rules; keep them in line with `CreateJobPostCommand` (ADR-F010). */
export const jobPostFormSchema = z.object({
  title: text,
  location: text,
  department: text,
  description: z
    .string()
    .trim()
    .min(1, required)
    .max(10000, 'Text môže mať najviac 10000 znakov.'),
  employmentType: z.enum(employmentTypes, { error: 'Vyberte typ úväzku.' }),
  paymentType: z.enum(paymentTypes, { error: 'Vyberte frekvenciu.' }),
  openPositions: z
    .number({ error: positions })
    .int(positions)
    .min(1, positions)
    .max(1000, positions),
  // NUMERIC(10,2) in the database: compared in cents, since 0.1 + 0.2 is not exact in floating point.
  salary: z
    .number({ error: salary })
    .positive(salary)
    .max(99999999.99, salary)
    .refine(
      (value) => Math.abs(value * 100 - Math.round(value * 100)) < 1e-6,
      salary,
    ),
  // Undefined while the list is still loading, since a disabled select has no value.
  recruiterId: z
    .string({ error: 'Vyberte rekrútera.' })
    .min(1, 'Vyberte rekrútera.'),
})

export type JobPostForm = z.infer<typeof jobPostFormSchema>

/** The body of `POST /api/v1/recruitment/job-posts`. TODO: replace with the generated OpenAPI client (ADR-F005). */
export interface CreateJobPostBody extends JobPostForm {
  /** `false` saves a draft (koncept). */
  isPublished: boolean
}

/** A job post as the API returns it. */
export interface JobPost extends CreateJobPostBody {
  id: string
  createdAt: string
  version: number
}

/** One entry of `GET /api/v1/recruitment/staff-users`. */
export interface StaffUser {
  id: string
  displayName: string
  roles: string[]
}

/** The staff with the Recruiter role, the people a job post's recruiter is chosen from. */
export function useRecruiters() {
  const auth = useStaffAuth()

  return useQuery({
    queryKey: ['recruitment', 'staff-users', 'Recruiter'],
    queryFn: async () => {
      const { users } = await request<{ users: StaffUser[] }>(
        '/api/v1/recruitment/staff-users?role=Recruiter',
        { headers: { Authorization: `Bearer ${await getAccessToken(auth)}` } },
      )
      return users
    },
    staleTime: 5 * 60 * 1000,
  })
}

/** `POST /api/v1/recruitment/job-posts`, with its idempotency key (ADR-A016). Hiring managers and admins only. */
export function useCreateJobPost() {
  const auth = useStaffAuth()

  return useCommand(async (body: CreateJobPostBody, idempotencyKey) =>
    request<JobPost>('/api/v1/recruitment/job-posts', {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${await getAccessToken(auth)}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(body),
      idempotencyKey,
    }),
  )
}
