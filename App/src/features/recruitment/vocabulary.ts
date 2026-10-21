// The Recruitment module's value sets and their Slovak labels. Free of staff-only imports, so the public pages can
// use them without loading MSAL (ADR-F004).

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
export const paymentTypes = ['monthly', 'hourly', 'yearly'] as const

export type PaymentType = (typeof paymentTypes)[number]

export const paymentTypeLabels: Record<PaymentType, string> = {
  monthly: 'Mesačná mzda',
  hourly: 'Hodinová mzda',
  yearly: 'Ročná mzda',
}

/** What the amount is per, after the euro sign: 8 €/hod., 1 400 €/mesiac, 15 000 €/rok. */
const salaryUnits: Record<PaymentType, string> = {
  hourly: 'hod.',
  monthly: 'mesiac',
  yearly: 'rok',
}

const wholeEuros = new Intl.NumberFormat('sk-SK', { maximumFractionDigits: 0 })
const euroCents = new Intl.NumberFormat('sk-SK', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

/** A wage as people read it: `1 400 €/mesiac`, `8,50 €/hod.`; cents only when there are any. */
export function formatSalary(salary: number, paymentType: PaymentType): string {
  const amount = Number.isInteger(salary)
    ? wholeEuros.format(salary)
    : euroCents.format(salary)
  return `${amount} €/${salaryUnits[paymentType]}`
}
