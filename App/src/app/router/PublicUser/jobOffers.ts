export type JobOffer = {
  id: string
  title: string
  location: string
  employmentType: string
  department: string
  salary: string
  salaryFrequency: string
  openings: number
  description: string
}

export const jobOffers: JobOffer[] = [
  {
    id: 'software-developer',
    title: 'Vývojár softvéru',
    location: 'Bratislava',
    employmentType: 'Plný úväzok',
    department: 'Technológie',
    salary: '2 400 - 3 600 EUR',
    salaryFrequency: 'Mesiac',
    openings: 2,
    description:
      'Budete súčasťou tímu, ktorý vyvíja spoľahlivé digitálne riešenia pre našich zákazníkov. Hľadáme človeka, ktorý rád spolupracuje, učí sa nové technológie a premieňa nápady na kvalitný produkt.',
  },
  {
    id: 'recruitment-specialist',
    title: 'Špecialista náboru',
    location: 'Košice',
    employmentType: 'Plný úväzok',
    department: 'Ľudské zdroje',
    salary: '1 800 - 2 600 EUR',
    salaryFrequency: 'Mesiac',
    openings: 1,
    description:
      'Pomôžte nám budovať skvelé tímy. Budete viesť náborový proces, komunikovať s kandidátmi a spolupracovať s manažérmi na hľadaní najvhodnejších talentov.',
  },
  {
    id: 'ux-designer',
    title: 'UX dizajnér',
    location: 'Bratislava',
    employmentType: 'Čiastočný úväzok',
    department: 'Produkt',
    salary: '1 500 - 2 200 EUR',
    salaryFrequency: 'Mesiac',
    openings: 1,
    description:
      'Navrhujte používateľské skúsenosti, ktoré sú jednoduché, zrozumiteľné a príjemné. Od výskumu potrieb používateľov až po prototypovanie budete mať vplyv na celý produkt.',
  },
]
